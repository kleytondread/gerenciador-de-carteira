// Tema claro/escuro via data-bs-theme (Bootstrap 5.3), com persistência em localStorage.
// Carregado de forma síncrona no <head> para aplicar o tema antes do primeiro paint (sem flash).
window.walTheme = {
    _key: 'wal-theme',

    apply(theme) {
        document.documentElement.setAttribute('data-bs-theme', theme);
        try { localStorage.setItem(this._key, theme); } catch { /* storage indisponível: tema só não persiste */ }
        // Recolore gráficos já renderizados; walCharts pode ainda não existir na carga inicial
        if (window.walCharts && walCharts.applyTheme) walCharts.applyTheme(theme === 'dark');
    },

    // Lido pelo Blazor (PreferenciasExibicao) para o MudBlazor começar no mesmo tema
    atual() {
        return document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'dark' : 'light';
    },

    toggle() {
        const novo = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
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
