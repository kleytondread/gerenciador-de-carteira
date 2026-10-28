using Microsoft.JSInterop;

namespace GerenciadorDeCarteira.Components;

/// <summary>
/// Preferências de exibição do usuário (ocultar valores e tema), compartilhadas por todas as telas do circuito.
/// Ficam no navegador (localStorage) e só podem ser lidas depois da primeira renderização interativa.
/// </summary>
public sealed class PreferenciasExibicao(IJSRuntime js)
{
    private const string ChaveOcultarValores = "wal-ocultar-valores";
    private Task? carga;

    public bool OcultarValores { get; private set; }

    public bool TemaEscuro { get; private set; }

    /// <summary>Falso até ler o navegador. Enquanto isso os valores ficam mascarados, para não aparecerem nem por um instante.</summary>
    public bool Carregada { get; private set; }

    /// <summary>Valores em R$ devem ser escondidos agora.</summary>
    public bool Mascarar => !Carregada || OcultarValores;

    public event Action? Mudou;

    /// <summary>Lê as preferências uma vez por circuito; chamadas seguintes reaproveitam a mesma leitura.</summary>
    public Task CarregarAsync() => carga ??= CarregarDoNavegadorAsync();

    public async Task AlternarOcultarValoresAsync()
    {
        OcultarValores = !OcultarValores;
        await js.InvokeVoidAsync("walPreferencias.definir", ChaveOcultarValores, OcultarValores ? "1" : "0");
        Mudou?.Invoke();
    }

    public async Task AlternarTemaAsync()
    {
        TemaEscuro = !TemaEscuro;
        // Mantém Bootstrap (telas ainda não migradas) e Chart.js no mesmo tema do MudBlazor
        await js.InvokeVoidAsync("walTheme.apply", TemaEscuro ? "dark" : "light");
        Mudou?.Invoke();
    }

    private async Task CarregarDoNavegadorAsync()
    {
        OcultarValores = await js.InvokeAsync<string?>("walPreferencias.obter", ChaveOcultarValores) == "1";
        TemaEscuro = await js.InvokeAsync<string?>("walTheme.atual") == "dark";
        Carregada = true;
        Mudou?.Invoke();
    }
}
