using Microsoft.AspNetCore.Components;

namespace GerenciadorDeCarteira.Components;

/// <summary>
/// Base das telas que mostram dados da carteira. Cuida do que toda tela repetia:
/// <list type="bullet">
/// <item>DbContext próprio por página (OwningComponentBase), descartado ao sair dela, em vez de um único
/// DbContext para o circuito inteiro do Blazor Server;</item>
/// <item>redesenha quando o usuário oculta/mostra valores ou troca o tema;</item>
/// <item>recarrega quando a carteira muda (transação salva no painel lateral, provento corrigido etc.).</item>
/// </list>
/// A tela só implementa <see cref="CarregarAsync"/>.
/// </summary>
public abstract class PaginaCarteira : OwningComponentBase
{
    // O DbContext da página não aceita duas consultas ao mesmo tempo: um aviso de "carteira mudou" pode chegar
    // enquanto a tela ainda carrega
    private readonly SemaphoreSlim carga = new(1, 1);

    [Inject] protected PreferenciasExibicao Preferencias { get; set; } = default!;

    [Inject] protected NotificacoesCarteira Notificacoes { get; set; } = default!;

    /// <summary>Verdadeiro até a primeira carga terminar (a tela mostra a barra de progresso).</summary>
    protected bool Carregando { get; private set; } = true;

    /// <summary>Valores em R$ devem ser escondidos agora.</summary>
    protected bool Mascarar => Preferencias.Mascarar;

    /// <summary>Busca os dados da tela. Chamado ao abrir e sempre que a carteira muda.</summary>
    protected abstract Task CarregarAsync();

    protected override void OnInitialized()
    {
        Preferencias.Mudou += AoMudarPreferencias;
        Notificacoes.CarteiraAlterada += AoAlterarCarteira;
    }

    /// <summary>Primeira carga. Telas que dependem de parâmetro de rota sobrescrevem e chamam <see cref="CarregarComProgressoAsync"/>.</summary>
    protected override Task OnInitializedAsync() => CarregarComProgressoAsync();

    /// <summary>Carga com a barra de progresso (abrir a tela ou trocar de ativo).</summary>
    protected async Task CarregarComProgressoAsync()
    {
        Carregando = true;
        try
        {
            await RecarregarAsync();
        }
        finally
        {
            Carregando = false;
        }
    }

    /// <summary>Carga sem a barra de progresso (a tela não pisca), uma de cada vez.</summary>
    protected async Task RecarregarAsync()
    {
        await carga.WaitAsync();
        try
        {
            await CarregarAsync();
        }
        finally
        {
            carga.Release();
        }
    }

    /// <summary>Recarga sem a barra de progresso seguida do redesenho (carteira mudou, cotações novas).</summary>
    protected async Task RecarregarERedesenharAsync()
    {
        await RecarregarAsync();
        StateHasChanged();
    }

    private void AoMudarPreferencias() => InvokeAsync(StateHasChanged);

    private void AoAlterarCarteira() => InvokeAsync(RecarregarERedesenharAsync);

    protected override async ValueTask DisposeAsyncCore()
    {
        Preferencias.Mudou -= AoMudarPreferencias;
        Notificacoes.CarteiraAlterada -= AoAlterarCarteira;
        await base.DisposeAsyncCore();
    }
}
