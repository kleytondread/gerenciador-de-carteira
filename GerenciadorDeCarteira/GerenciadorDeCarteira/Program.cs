using GerenciadorDeCarteira.Components;
using GerenciadorDeCarteira.Endpoints;
using Hangfire;
using Wal.Jobs;


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

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MainLayout lê os cookies de preferência na pré-renderização
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPreferenciasIniciais, PreferenciasDosCookies>();
builder.Services.AddTelasCarteira();

builder.Services.AddCarteira(builder.Configuration,
    ServicosCarteira.PastaDados(builder.Configuration, builder.Environment.ContentRootPath));

var app = builder.Build();

await app.Services.IniciarCarteiraAsync();

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
// As telas ficam no projeto Wal.UI
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Routes).Assembly);

app.Run();
