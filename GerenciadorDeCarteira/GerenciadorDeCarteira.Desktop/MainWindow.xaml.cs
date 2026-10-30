using System.Windows;

namespace GerenciadorDeCarteira.Desktop;

public partial class MainWindow : Window
{
    private const string IconeMaximizar = "";
    private const string IconeRestaurar = "";

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        BlazorWebView.Services = services;
        StateChanged += (_, _) => AoMudarEstado();
    }

    private void Minimizar_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizarRestaurar_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Fechar a janela fecha o app (ShutdownMode=OnMainWindowClose)
    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();

    private void AoMudarEstado()
    {
        var maximizada = WindowState == WindowState.Maximized;
        MaximizarRestaurar.Content = maximizada ? IconeRestaurar : IconeMaximizar;
        MaximizarRestaurar.ToolTip = maximizada ? "Restaurar" : "Maximizar";
        // Maximizada sem a moldura do Windows, a janela passa um pouco da tela: a margem compensa
        Raiz.Margin = maximizada ? new Thickness(7) : new Thickness(0);
    }
}
