using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wal.Data;
using Wal.ExternalApis;
using Wal.Services;
using Wal.Services.Common;
using Wal.Services.Dtos;
using Wal.Services.ImportacaoB3;
using Wal.Services.Irpf;
using Wal.Services.TesouroDireto;

namespace Wal.Jobs;

/// <summary>Banco, APIs externas, serviços e jobs do app. Iguais na versão web e na versão Windows.</summary>
public static class ServicosCarteira
{
    /// <summary>
    /// Pasta dos dados (banco e arquivos baixados). Publicado, fica fora da pasta do programa
    /// (%LOCALAPPDATA%\GerenciadorDeCarteira), para atualizar ou apagar o app não levar os dados junto.
    /// Em Development vem do appsettings.Development.json. Caminho relativo parte do <paramref name="contentRoot"/>.
    /// </summary>
    public static string PastaDados(IConfiguration configuration, string contentRoot)
    {
        var pasta = configuration["Dados:Pasta"] is { Length: > 0 } pastaConfigurada
            ? Environment.ExpandEnvironmentVariables(pastaConfigurada)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GerenciadorDeCarteira");
        pasta = Path.GetFullPath(pasta, contentRoot);
        Directory.CreateDirectory(pasta);
        return pasta;
    }

    public static IServiceCollection AddCarteira(this IServiceCollection services, IConfiguration configuration, string pastaDados)
    {
        // Banco com caminho absoluto: relativo, ele seria criado na pasta de onde o processo foi aberto
        var conexao = new SqliteConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        conexao.DataSource = Path.GetFullPath(
            string.IsNullOrWhiteSpace(conexao.DataSource) ? "gestorcarteira.db" : conexao.DataSource, pastaDados);

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(conexao.ToString()));

        services.AddHttpClient<IYahooFinanceClient, YahooFinanceClient>(client =>
        {
            client.BaseAddress = new Uri("https://query1.finance.yahoo.com/v8/finance/");
            client.Timeout = TimeSpan.FromSeconds(30);
            // Yahoo Finance pode rejeitar requests sem User-Agent de browser
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        });

        // Tesouro Direto: preços vêm do arquivo de dados abertos do Tesouro Transparente, salvo em disco
        // (caminho relativo à pasta de dados) e baixado de novo só quando tem pelo menos 5h.
        var tesouroOptions = configuration.GetSection("TesouroDireto").Get<TesouroDiretoOptions>() ?? new TesouroDiretoOptions();
        tesouroOptions.CaminhoArquivo = Path.GetFullPath(
            string.IsNullOrWhiteSpace(tesouroOptions.CaminhoArquivo) ? "Dados/precotaxatesourodireto.csv" : tesouroOptions.CaminhoArquivo,
            pastaDados);
        services.AddSingleton(tesouroOptions);
        services.AddHttpClient<ITesouroDiretoClient, TesouroDiretoClient>(client =>
        {
            client.BaseAddress = new Uri("https://www.tesourotransparente.gov.br/");
            client.Timeout = TimeSpan.FromMinutes(5); // o arquivo tem o histórico de todos os títulos desde 2002
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GestorCarteiraB3/1.0");
        });
        services.AddScoped<ITesouroDiretoService, TesouroDiretoService>();
        services.AddScoped<TesouroSyncJob>();

        services.AddHttpClient<IBancoCentralClient, BancoCentralClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.bcb.gov.br/");
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GestorCarteiraB3/1.0");
        });
        services.AddScoped<IIndicesReferenciaService, IndicesReferenciaService>();
        services.AddScoped<IndiceSyncJob>();

        services.AddScoped<IDadosMercadoService, DadosMercadoService>();
        services.AddScoped<IAtivoJobScheduler, AtivoJobScheduler>();
        services.AddScoped<AtivoSyncJob>();
        services.AddScoped<IPosicaoService, PosicaoService>();
        services.AddScoped<IAtivoService, AtivoService>();
        services.AddScoped<IMovimentacaoService, MovimentacaoService>();
        services.AddScoped<ICarteiraService, CarteiraService>();
        services.AddScoped<IAnaliseCarteiraService, AnaliseCarteiraService>();
        services.AddScoped<IProventoService, ProventoService>();
        services.AddScoped<IImportacaoB3Service, ImportacaoB3Service>();
        services.AddScoped<IIrpfService, IrpfService>();
        // Corretora onde ficam os ativos: vai na discriminação e é a fonte pagadora do Tesouro na declaração do IR
        services.AddSingleton(configuration.GetSection("Irpf:Corretora").Get<CorretoraIrpf>()
            ?? throw new InvalidOperationException("Configure Irpf:Corretora (Nome e Cnpj) no appsettings.json."));

        // Cotação do momento na tela de Posições: só em memória, compartilhada entre as abas abertas
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICotacaoAoVivoService, CotacaoAoVivoService>();

        // Hangfire.Storage.SQLite não tem storage oficial do Hangfire para SQLite;
        // usa o mesmo arquivo de banco da aplicação (storage roda em tabelas próprias, prefixo Hangfire).
        // Atenção: UseSQLiteStorage espera o caminho puro do arquivo, não uma connection string
        // no formato "Data Source=..." do EF Core.
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSQLiteStorage(conexao.DataSource));

        services.AddHangfireServer();

        return services;
    }

    /// <summary>Prepara o banco e agenda os jobs. Chamar uma vez ao abrir o app, antes de mostrar as telas.</summary>
    public static async Task IniciarCarteiraAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Sempre, em qualquer ambiente: numa máquina nova cria o banco inteiro (vazio); num banco existente aplica só
        // as migrations pendentes, então atualizar o app também atualiza o banco
        await db.Database.MigrateAsync();

        var jobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        var opcoes = new RecurringJobOptions { TimeZone = Pregao.Fuso };

        jobs.AddOrUpdate<AtivoSyncJob>(
            "sincronizacao-diaria-ativos",
            job => job.SincronizarTodosAtivosAsync(),
            "0 20 * * *",
            opcoes);

        // A cada 6h; o download em si só acontece quando o arquivo tem pelo menos 5h (TesouroDiretoOptions)
        jobs.AddOrUpdate<TesouroSyncJob>(
            "atualizacao-tesouro-direto",
            job => job.AtualizarAsync(),
            "0 */6 * * *",
            opcoes);

        // Depois da sincronização dos ativos; o Banco Central publica o CDI do dia no fim da tarde
        jobs.AddOrUpdate<IndiceSyncJob>(
            "atualizacao-indices-referencia",
            job => job.SincronizarAsync(),
            "30 20 * * *",
            opcoes);

        // Primeira execução com a tabela de índices vazia: carga imediata, sem esperar o horário do job
        if (!await db.CotacoesIndices.AnyAsync())
            scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>().Enqueue<IndiceSyncJob>(job => job.SincronizarAsync());
    }
}
