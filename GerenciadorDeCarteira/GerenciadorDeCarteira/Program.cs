using GerenciadorDeCarteira.Components;
using GerenciadorDeCarteira.Endpoints;
using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Wal.Data;
using Wal.ExternalApis;
using Wal.Jobs;
using Wal.Services;
using Wal.Services.Common;
using Wal.Services.Dtos;
using Wal.Services.ImportacaoB3;
using Wal.Services.Irpf;
using Wal.Services.TesouroDireto;


// Publicado, a raiz do app (appsettings.json, wwwroot) é a pasta do executável, e não a pasta de onde ele foi
// aberto (um atalho ou o Agendador podem abrir de qualquer lugar). Em desenvolvimento fica o padrão do .NET.
var emDesenvolvimento = string.Equals(
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
    "Development", StringComparison.OrdinalIgnoreCase);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = emDesenvolvimento ? null : AppContext.BaseDirectory
});

// Pasta dos dados (banco e arquivos baixados). Publicado, fica fora da pasta do programa
// (%LOCALAPPDATA%\GerenciadorDeCarteira), para atualizar ou apagar o app não levar os dados junto.
// Em Development é a pasta do projeto (appsettings.Development.json). Caminho relativo parte do ContentRoot.
var pastaDados = builder.Configuration["Dados:Pasta"] is { Length: > 0 } pastaConfigurada
    ? Environment.ExpandEnvironmentVariables(pastaConfigurada)
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GerenciadorDeCarteira");
pastaDados = Path.GetFullPath(pastaDados, builder.Environment.ContentRootPath);
Directory.CreateDirectory(pastaDados);

// Banco com caminho absoluto: relativo, ele seria criado na pasta de onde o processo foi aberto
var conexao = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("DefaultConnection"));
conexao.DataSource = Path.GetFullPath(
    string.IsNullOrWhiteSpace(conexao.DataSource) ? "gestorcarteira.db" : conexao.DataSource, pastaDados);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
// MainLayout lê os cookies de preferência na pré-renderização
builder.Services.AddHttpContextAccessor();
// Estado de interface por circuito (cada aba do navegador tem o seu)
builder.Services.AddScoped<PreferenciasExibicao>();
builder.Services.AddScoped<NotificacoesCarteira>();
builder.Services.AddScoped<PainelTransacaoEstado>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(conexao.ToString()));

builder.Services.AddHttpClient<IYahooFinanceClient, YahooFinanceClient>(client =>
{
    client.BaseAddress = new Uri("https://query1.finance.yahoo.com/v8/finance/");
    client.Timeout = TimeSpan.FromSeconds(30);
    // Yahoo Finance pode rejeitar requests sem User-Agent de browser
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});

// Tesouro Direto: preços vêm do arquivo de dados abertos do Tesouro Transparente, salvo em disco
// (caminho relativo à pasta de dados) e baixado de novo só quando tem pelo menos 5h.
var tesouroOptions = builder.Configuration.GetSection("TesouroDireto").Get<TesouroDiretoOptions>() ?? new TesouroDiretoOptions();
tesouroOptions.CaminhoArquivo = Path.GetFullPath(
    string.IsNullOrWhiteSpace(tesouroOptions.CaminhoArquivo) ? "Dados/precotaxatesourodireto.csv" : tesouroOptions.CaminhoArquivo,
    pastaDados);
builder.Services.AddSingleton(tesouroOptions);
builder.Services.AddHttpClient<ITesouroDiretoClient, TesouroDiretoClient>(client =>
{
    client.BaseAddress = new Uri("https://www.tesourotransparente.gov.br/");
    client.Timeout = TimeSpan.FromMinutes(5); // o arquivo tem o histórico de todos os títulos desde 2002
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GestorCarteiraB3/1.0");
});
builder.Services.AddScoped<ITesouroDiretoService, TesouroDiretoService>();
builder.Services.AddScoped<TesouroSyncJob>();

builder.Services.AddHttpClient<IBancoCentralClient, BancoCentralClient>(client =>
{
    client.BaseAddress = new Uri("https://api.bcb.gov.br/");
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("GestorCarteiraB3/1.0");
});
builder.Services.AddScoped<IIndicesReferenciaService, IndicesReferenciaService>();
builder.Services.AddScoped<IndiceSyncJob>();

builder.Services.AddScoped<IDadosMercadoService, DadosMercadoService>();
builder.Services.AddScoped<IAtivoJobScheduler, AtivoJobScheduler>();
builder.Services.AddScoped<AtivoSyncJob>();
builder.Services.AddScoped<IPosicaoService, PosicaoService>();
builder.Services.AddScoped<IAtivoService, AtivoService>();
builder.Services.AddScoped<IMovimentacaoService, MovimentacaoService>();
builder.Services.AddScoped<ICarteiraService, CarteiraService>();
builder.Services.AddScoped<IAnaliseCarteiraService, AnaliseCarteiraService>();
builder.Services.AddScoped<IProventoService, ProventoService>();
builder.Services.AddScoped<IImportacaoB3Service, ImportacaoB3Service>();
builder.Services.AddScoped<IIrpfService, IrpfService>();
// Corretora onde ficam os ativos: vai na discriminação e é a fonte pagadora do Tesouro na declaração do IR
builder.Services.AddSingleton(builder.Configuration.GetSection("Irpf:Corretora").Get<CorretoraIrpf>()
    ?? throw new InvalidOperationException("Configure Irpf:Corretora (Nome e Cnpj) no appsettings.json."));

// Cotação do momento na tela de Posições: só em memória, compartilhada entre as abas abertas
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICotacaoAoVivoService, CotacaoAoVivoService>();

// Hangfire.Storage.SQLite não tem storage oficial do Hangfire para SQLite;
// usa o mesmo arquivo de banco da aplicação (storage roda em tabelas próprias, prefixo Hangfire).
// Atenção: UseSQLiteStorage espera o caminho puro do arquivo, não uma connection string
// no formato "Data Source=..." do EF Core.
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage(conexao.DataSource));

builder.Services.AddHangfireServer();

var app = builder.Build();

// Sempre, em qualquer ambiente: numa máquina nova cria o banco inteiro (vazio); num banco existente aplica só
// as migrations pendentes, então atualizar o app também atualiza o banco
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// Publicado, o app só escuta HTTP no localhost (appsettings.Production.json): redirecionar para HTTPS levaria a
// uma porta que não existe. Em desenvolvimento o perfil "https" do Visual Studio continua funcionando.
if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAntiforgery();

// Sem autenticação nesta fase (MVP local, sistema inteiro ainda não tem login) — ver spec seção 2.
app.UseHangfireDashboard("/hangfire");

RecurringJob.AddOrUpdate<AtivoSyncJob>(
    "sincronizacao-diaria-ativos",
    job => job.SincronizarTodosAtivosAsync(),
    "0 20 * * *",
    new RecurringJobOptions { TimeZone = Pregao.Fuso });

// A cada 6h; o download em si só acontece quando o arquivo tem pelo menos 5h (TesouroDiretoOptions)
RecurringJob.AddOrUpdate<TesouroSyncJob>(
    "atualizacao-tesouro-direto",
    job => job.AtualizarAsync(),
    "0 */6 * * *",
    new RecurringJobOptions { TimeZone = Pregao.Fuso });

// Depois da sincronização dos ativos; o Banco Central publica o CDI do dia no fim da tarde
RecurringJob.AddOrUpdate<IndiceSyncJob>(
    "atualizacao-indices-referencia",
    job => job.SincronizarAsync(),
    "30 20 * * *",
    new RecurringJobOptions { TimeZone = Pregao.Fuso });

// Primeira execução com a tabela de índices vazia: carga imediata, sem esperar o horário do job
using (var scope = app.Services.CreateScope())
{
    if (!scope.ServiceProvider.GetRequiredService<AppDbContext>().CotacoesIndices.Any())
        BackgroundJob.Enqueue<IndiceSyncJob>(job => job.SincronizarAsync());
}

app.UseMiddleware<ApiExceptionMiddleware>();

app.MapAtivoEndpoints();
app.MapMovimentacaoEndpoints();
app.MapCarteiraEndpoints();

if (app.Environment.IsDevelopment())
{
    // Endpoint de desenvolvimento para validar o ApiExceptionMiddleware (500 padronizado sem stack trace).
    app.MapGet("/api/dev/erro-teste", () =>
    {
        throw new InvalidOperationException("Exceção proposital para teste do middleware.");
    });
}

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
