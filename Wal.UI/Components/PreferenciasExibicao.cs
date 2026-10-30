using Microsoft.JSInterop;

namespace GerenciadorDeCarteira.Components;

/// <summary>
/// Preferências de exibição do usuário (ocultar valores e tema), compartilhadas por todas as telas do circuito.
/// Ficam no navegador (localStorage + cookie). Na web o cookie chega já na pré-renderização (<see cref="PreferenciasIniciais"/>);
/// sem ele, só dá para ler depois da primeira renderização interativa.
/// </summary>
public sealed class PreferenciasExibicao(IJSRuntime js, ITemaJanela temaJanela)
{
    private const string ChaveOcultarValores = "wal-ocultar-valores";
    private Task? carga;

    public bool OcultarValores { get; private set; }

    public bool TemaEscuro { get; private set; }

    /// <summary>Falso até ler o navegador. Enquanto isso os valores ficam mascarados, para não aparecerem nem por um instante.</summary>
    public bool Carregada { get; private set; }

    /// <summary>Usa o que veio nos cookies, antes de o circuito poder falar com o navegador.</summary>
    public void DefinirIniciais(PreferenciasIniciais iniciais)
    {
        if (Carregada) return;
        TemaEscuro = iniciais.TemaEscuro;
        OcultarValores = iniciais.OcultarValores;
        Carregada = true;
    }

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
        // Mantém o CSS próprio (data-tema) e o Chart.js no mesmo tema do MudBlazor
        await js.InvokeVoidAsync("walTheme.apply", TemaEscuro ? "dark" : "light");
        temaJanela.Aplicar(TemaEscuro);
        Mudou?.Invoke();
    }

    private async Task CarregarDoNavegadorAsync()
    {
        OcultarValores = await js.InvokeAsync<string?>("walPreferencias.obter", ChaveOcultarValores) == "1";
        TemaEscuro = await js.InvokeAsync<string?>("walTheme.atual") == "dark";
        Carregada = true;
        temaJanela.Aplicar(TemaEscuro);
        Mudou?.Invoke();
    }
}

/// <summary>
/// Preferências lidas dos cookies na pré-renderização. Vão para o circuito interativo como estado persistido
/// (lá não há requisição HTTP para ler cookies), e assim a página não pisca no tema claro nem com valores mascarados.
/// </summary>
public sealed record PreferenciasIniciais(bool TemaEscuro, bool OcultarValores);

/// <summary>De onde vêm as <see cref="PreferenciasIniciais"/>: na web, dos cookies; no app Windows não há (nulo).</summary>
public interface IPreferenciasIniciais
{
    PreferenciasIniciais? Ler();
}

/// <summary>Avisa a janela do app Windows quando o tema muda, para a barra de título acompanhar. Na web não faz nada.</summary>
public interface ITemaJanela
{
    void Aplicar(bool escuro);
}
