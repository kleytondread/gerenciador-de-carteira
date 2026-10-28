// Tema claro/escuro: atributo data-tema no <html> (lido pelo CSS e pelo Chart.js), salvo em localStorage e num cookie.
// Carregado de forma síncrona no <head> para aplicar o tema antes do primeiro paint (sem flash).
window.walTheme = {
    _key: 'wal-theme',

    apply(theme) {
        document.documentElement.setAttribute('data-tema', theme);
        try { localStorage.setItem(this._key, theme); } catch { /* storage indisponível: tema só não persiste */ }
        // O servidor lê o cookie na pré-renderização e já manda o HTML no tema certo (sem piscar claro)
        document.cookie = 'wal-tema=' + theme + '; path=/; max-age=31536000; SameSite=Lax';
        // Recolore gráficos já renderizados; walCharts pode ainda não existir na carga inicial
        if (window.walCharts && walCharts.applyTheme) walCharts.applyTheme(theme === 'dark');
    },

    // Lido pelo Blazor (PreferenciasExibicao) para o MudBlazor começar no mesmo tema
    atual() {
        return document.documentElement.getAttribute('data-tema') === 'dark' ? 'dark' : 'light';
    },

    toggle() {
        const novo = document.documentElement.getAttribute('data-tema') === 'dark' ? 'light' : 'dark';
        this.apply(novo);
    },

    init() {
        let theme = null;
        try { theme = localStorage.getItem(this._key); } catch { }
        if (theme !== 'light' && theme !== 'dark') {
            theme = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
        }
        this.apply(theme);
    }
};

walTheme.init();
