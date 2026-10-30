using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor.Services;

namespace GerenciadorDeCarteira.Components;

public static class ServicosTelas
{
    /// <summary>Serviços das telas, iguais na web e no app Windows. Cada app pode trocar as fontes de preferência e tema antes.</summary>
    public static IServiceCollection AddTelasCarteira(this IServiceCollection services)
    {
        services.AddMudServices();
        // Estado de interface por circuito (na web, cada aba do navegador tem o seu)
        services.AddScoped<PreferenciasExibicao>();
        services.AddScoped<NotificacoesCarteira>();
        services.AddScoped<PainelTransacaoEstado>();
        services.TryAddScoped<IPreferenciasIniciais, SemPreferenciasIniciais>();
        services.TryAddSingleton<ITemaJanela, SemTemaJanela>();
        return services;
    }

    private sealed class SemPreferenciasIniciais : IPreferenciasIniciais
    {
        public PreferenciasIniciais? Ler() => null;
    }

    private sealed class SemTemaJanela : ITemaJanela
    {
        public void Aplicar(bool escuro) { }
    }
}
