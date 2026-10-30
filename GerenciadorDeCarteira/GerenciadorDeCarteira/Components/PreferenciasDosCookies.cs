namespace GerenciadorDeCarteira.Components;

/// <summary>Na pré-renderização, lê as preferências dos cookies que o navegador grava (tema e ocultar valores).</summary>
public sealed class PreferenciasDosCookies(IHttpContextAccessor httpContextAccessor) : IPreferenciasIniciais
{
    /// <summary>Nulo antes de o navegador gravar o cookie de tema (primeira visita) e dentro do circuito interativo.</summary>
    public PreferenciasIniciais? Ler() =>
        httpContextAccessor.HttpContext?.Request.Cookies is { } cookies && cookies["wal-tema"] is { } tema
            ? new PreferenciasIniciais(tema == "dark", cookies["wal-ocultar-valores"] == "1")
            : null;
}
