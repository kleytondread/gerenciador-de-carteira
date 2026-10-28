using GerenciadorDeCarteira.Components;
using GerenciadorDeCarteira.Endpoints;
using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Wal.Data;
using Wal.ExternalApis;
using Wal.Jobs;
using Wal.Services;
using Wal.Services.TesouroDireto;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
// Estado de interface por circuito (cada aba do navegador tem o seu)
builder.Services.AddScoped<PreferenciasExibicao>();
builder.Services.AddScoped<NotificacoesCarteira>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient<IYahooFinanceClient, YahooFinanceClient>(client =>
{
    client.BaseAddress = new Uri("https://query1.finance.yahoo.com/v8/finance/");
    client.Timeout = TimeSpan.FromSeconds(30);
    // Yahoo Finance pode rejeitar requests sem User-Agent de browser
    client.DefaultRequestHeaders.Add("User-Agent",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});

// Tesouro Direto: preços vêm do arquivo de dados abertos do Tesouro Transparente, salvo em disco
// (caminho relativo à raiz do projeto) e baixado de novo só quando tem pelo menos 5h.
var tesouroOptions = builder.Configuration.GetSection("TesouroDireto").Get<TesouroDiretoOptions>() ?? new TesouroDiretoOptions();
tesouroOptions.CaminhoArquivo = Path.Combine(builder.Environment.ContentRootPath,
    string.IsNullOrWhiteSpace(tesouroOptions.CaminhoArquivo) ? "Dados/precotaxatesourodireto.csv" : tesouroOptions.CaminhoArquivo);
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

// Hangfire.Storage.SQLite não tem storage oficial do Hangfire para SQLite;
// usa o mesmo arquivo de banco da aplicação (storage roda em tabelas próprias, prefixo Hangfire).
// Atenção: UseSQLiteStorage espera o caminho puro do arquivo, não uma connection string
// no formato "Data Source=..." do EF Core — por isso o valor é extraído via SqliteConnectionStringBuilder.
var sqliteDbPath = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("DefaultConnection")).DataSource;

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage(sqliteDbPath));

builder.Services.AddHangfireServer();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

// Sem autenticação nesta fase (MVP local, sistema inteiro ainda não tem login) — ver spec seção 2.
app.UseHangfireDashboard("/hangfire");

RecurringJob.AddOrUpdate<AtivoSyncJob>(
    "sincronizacao-diaria-ativos",
    job => job.SincronizarTodosAtivosAsync(),
    "0 20 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo") });

// A cada 6h; o download em si só acontece quando o arquivo tem pelo menos 5h (TesouroDiretoOptions)
RecurringJob.AddOrUpdate<TesouroSyncJob>(
    "atualizacao-tesouro-direto",
    job => job.AtualizarAsync(),
    "0 */6 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo") });

// Depois da sincronização dos ativos; o Banco Central publica o CDI do dia no fim da tarde
RecurringJob.AddOrUpdate<IndiceSyncJob>(
    "atualizacao-indices-referencia",
    job => job.SincronizarAsync(),
    "30 20 * * *",
    new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo") });

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
