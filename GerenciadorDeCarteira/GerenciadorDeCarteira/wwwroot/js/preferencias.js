// Preferências de exibição do usuário, guardadas no navegador (localStorage).
// Se o armazenamento estiver indisponível (janela anônima, bloqueio), a preferência só não persiste.
window.walPreferencias = {
    obter(chave) {
        try { return localStorage.getItem(chave); } catch { return null; }
    },
    definir(chave, valor) {
        try { localStorage.setItem(chave, valor); } catch { /* sem persistência */ }
    }
};
