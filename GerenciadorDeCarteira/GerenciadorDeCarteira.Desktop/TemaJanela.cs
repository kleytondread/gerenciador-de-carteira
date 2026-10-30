using System.Windows;
using System.Windows.Media;
using GerenciadorDeCarteira.Components;

namespace GerenciadorDeCarteira.Desktop;

/// <summary>
/// A barra de título é WPF (fora do WebView2) e não enxerga o CSS: quando as telas carregam ou trocam o tema,
/// pinta a barra com as mesmas cores da barra superior do app (TemaWal.cs).
/// </summary>
public sealed class TemaJanela : ITemaJanela
{
    public void Aplicar(bool escuro) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var recursos = Application.Current.Resources;
            recursos["TitleBarBackgroundBrush"] = Cor(escuro ? "#1F1F1E" : "#FFFFFF");
            recursos["TitleBarForegroundBrush"] = Cor(escuro ? "#F0EFEC" : "#1D1D1F");
            recursos["TitleBarButtonHoverBrush"] = Cor(escuro ? "#383835" : "#EEF0F2");
            recursos["TitleBarBorderBrush"] = Cor(escuro ? "#383835" : "#E3E5E8");
        });

    private static SolidColorBrush Cor(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}
