// Preferências de exibição do usuário, guardadas no navegador (localStorage).
// Se o armazenamento estiver indisponível (janela anônima, bloqueio), a preferência só não persiste.
// As que mudam a primeira renderização também vão para um cookie, lido pelo servidor na pré-renderização.
window.walPreferencias = {
    _cookies: ['wal-ocultar-valores'],

    obter(chave) {
        try { return localStorage.getItem(chave); } catch { return null; }
    },

    definir(chave, valor) {
        try { localStorage.setItem(chave, valor); } catch { /* sem persistência */ }
        if (this._cookies.includes(chave)) this._cookie(chave, valor);
    },

    _cookie(chave, valor) {
        document.cookie = chave + '=' + encodeURIComponent(valor) + '; path=/; max-age=31536000; SameSite=Lax';
    }
};

// Quem salvou a preferência antes de existir o cookie: copia do localStorage
walPreferencias._cookies.forEach(c => walPreferencias._cookie(c, walPreferencias.obter(c) ?? '0'));
