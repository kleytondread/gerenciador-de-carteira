using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GerenciadorDeCarteira.Desktop;

public partial class MainWindow : Window
{
    private const string IconeMaximizar = "";
    private const string IconeRestaurar = "";

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        // 1600x900 de início; numa tela menor (notebook 1366x768), cabe na área livre do monitor principal
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        BlazorWebView.Services = services;
        StateChanged += (_, _) => AoMudarEstado();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(Mensagens);
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
    }

    // ── Maximizar sem cobrir a barra de tarefas ──
    // Sem a moldura do Windows (WindowStyle=None), maximizar ocupa o monitor inteiro: a janela fica por cima da barra
    // de tarefas e engole os cliques nela. Ao Windows perguntar o tamanho da janela maximizada, a resposta é a área
    // livre do monitor onde ela está (sem a barra, de qualquer lado e em qualquer monitor).

    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 2;

    private static IntPtr Mensagens(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Tamanho = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var limites = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        // Posição relativa ao canto do monitor; tamanho = área livre
        limites.MaxPosicao.X = info.AreaLivre.Esquerda - info.Monitor.Esquerda;
        limites.MaxPosicao.Y = info.AreaLivre.Topo - info.Monitor.Topo;
        limites.MaxTamanho.X = info.AreaLivre.Direita - info.AreaLivre.Esquerda;
        limites.MaxTamanho.Y = info.AreaLivre.Base - info.AreaLivre.Topo;
        Marshal.StructureToPtr(limites, lParam, false);
        // Não marca como tratada: o WPF ainda aplica o MinWidth/MinHeight da janela
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Ponto { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Retangulo { public int Esquerda, Topo, Direita, Base; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Ponto Reservado, MaxTamanho, MaxPosicao, MinRastreio, MaxRastreio;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Tamanho;
        public Retangulo Monitor, AreaLivre;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
