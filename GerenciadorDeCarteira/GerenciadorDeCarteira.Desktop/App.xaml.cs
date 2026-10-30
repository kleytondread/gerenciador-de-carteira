using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using GerenciadorDeCarteira.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Wal.Jobs;

namespace GerenciadorDeCarteira.Desktop;

public partial class App : Application
{
    private IHost? host;
    private Mutex? instanciaUnica;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Uma janela só: duas cópias abertas rodariam dois Hangfire no mesmo banco
        instanciaUnica = new Mutex(true, @"Local\GerenciadorDeCarteira.Desktop", out var primeira);
        if (!primeira)
        {
            TrazerJanelaAbertaParaFrente();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += AoErroNaInterface;
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            Log.Fatal(ev.ExceptionObject as Exception, "Erro fatal não tratado");
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Log.Error(ev.Exception, "Exceção não observada em tarefa em segundo plano");
            ev.SetObserved();
        };

        try
        {
            // A raiz do app (appsettings.json, wwwroot) é sempre a pasta do executável, de onde quer que ele seja aberto
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = e.Args,
                ContentRootPath = AppContext.BaseDirectory,
            });
            var pastaDados = ServicosCarteira.PastaDados(builder.Configuration, builder.Environment.ContentRootPath);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .WriteTo.File(System.IO.Path.Combine(pastaDados, "logs", "desktop-.log"),
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
                .CreateLogger();
            builder.Services.AddSerilog();

            builder.Services.AddWpfBlazorWebView();
#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
#endif
            builder.Services.AddSingleton<ITemaJanela, TemaJanela>();
            builder.Services.AddTelasCarteira();
            builder.Services.AddCarteira(builder.Configuration, pastaDados);
            builder.Services.AddSingleton<MainWindow>();

            host = builder.Build();
            Log.Information("Gerenciador de Carteira abrindo (dados em {Pasta})", pastaDados);

            await host.Services.IniciarCarteiraAsync();
            // Liga o Hangfire. Jobs que perderam o horário com o app fechado rodam agora
            await host.StartAsync();

            host.Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Falha ao abrir o aplicativo");
            Log.CloseAndFlush();
            MessageBox.Show($"Não foi possível abrir o Gerenciador de Carteira.\n\n{ex.Message}",
                "Gerenciador de Carteira", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            // Espera de verdade (o processo acaba quando este método retorna): o Hangfire termina o que está fazendo.
            // Fora do dispatcher do WPF, para não travar esperando a si mesmo.
            Task.Run(() => host.StopAsync(TimeSpan.FromSeconds(5))).Wait();
            host.Dispose();
        }
        Log.CloseAndFlush();
        instanciaUnica?.Dispose();
        base.OnExit(e);
    }

    private void AoErroNaInterface(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Erro não tratado na interface");
        e.Handled = true;
        MessageBox.Show("Ocorreu um erro inesperado e o aplicativo precisa ser fechado. Os detalhes ficam no log, na pasta de dados.",
            "Gerenciador de Carteira", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    private static void TrazerJanelaAbertaParaFrente()
    {
        var atual = Process.GetCurrentProcess();
        var aberta = Process.GetProcessesByName(atual.ProcessName)
            .FirstOrDefault(p => p.Id != atual.Id && p.MainWindowHandle != IntPtr.Zero);
        if (aberta is null) return;

        const int SwRestore = 9;
        if (IsIconic(aberta.MainWindowHandle)) ShowWindow(aberta.MainWindowHandle, SwRestore);
        SetForegroundWindow(aberta.MainWindowHandle);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
}
