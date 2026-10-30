using MudBlazor;

namespace GerenciadorDeCarteira.Components.Layout;

/// <summary>Tema MudBlazor do app (cores do wireframe). Ganho/perda usam Success/Error, sempre junto do sinal +/−.</summary>
public static class TemaWal
{
    public static readonly MudTheme Tema = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1c5cab",
            Success = "#0b7a34",
            Error = "#c5221f",
            Background = "#f4f5f7",
            BackgroundGray = "#eef0f2",
            Surface = "#ffffff",
            AppbarBackground = "#ffffff",
            AppbarText = "#1d1d1f",
            DrawerBackground = "#ffffff",
            DrawerText = "#3c4043",
            DrawerIcon = "#5f6368",
            TextPrimary = "#1d1d1f",
            TextSecondary = "#5f6368",
            ActionDefault = "#3c4043",
            LinesDefault = "#e3e5e8",
            TableLines = "#f0f1f3",
            Divider = "#e3e5e8",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#6da7ec",
            Success = "#4fbf73",
            Error = "#f28b82",
            Background = "#141413",
            BackgroundGray = "#1a1a19",
            Surface = "#1f1f1e",
            AppbarBackground = "#1f1f1e",
            AppbarText = "#f0efec",
            DrawerBackground = "#1f1f1e",
            DrawerText = "#d6d5cf",
            DrawerIcon = "#c3c2b7",
            TextPrimary = "#f0efec",
            TextSecondary = "#c3c2b7",
            ActionDefault = "#d6d5cf",
            LinesDefault = "#383835",
            TableLines = "#2c2c2a",
            Divider = "#383835",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Roboto", "system-ui", "-apple-system", "Segoe UI", "sans-serif"],
            },
            // Botões em caixa normal ("Transação", não "TRANSAÇÃO")
            Button = new ButtonTypography { TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "232px",
        },
    };
}
