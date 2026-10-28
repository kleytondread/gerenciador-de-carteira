window.walCharts = window.walCharts || {};
window.walCharts._instances = window.walCharts._instances || {};

// Cores de eixos/grade por tema — a cor da série (azul primário) funciona bem nos dois temas.
window.walCharts._themeColors = (isDark) => isDark
    ? { text: '#adb5bd', grid: 'rgba(255, 255, 255, 0.08)' }
    : { text: '#666666', grid: 'rgba(0, 0, 0, 0.08)' };

window.walCharts._isDark = () =>
    document.documentElement.getAttribute('data-bs-theme') === 'dark';

// opcoes.medirVariacao: liga a medição de variação por clique e arraste (ver _ligarMedicao).
// opcoes.formato: 'valor' (padrão) ou 'percentual' — série em % acumulado (ex.: 12.5 = 12,5%);
//   formata eixo e tooltip em % e faz a medição calcular a rentabilidade composta do período.
window.walCharts.renderLine = (canvasId, labels, data, label, opcoes) => {
    // Destrói a instância anterior primeiro: se o canvas saiu da tela (período sem dados, troca de ativo),
    // o gráfico antigo não pode ficar preso a um elemento que não existe mais.
    window.walCharts.destroy(canvasId);

    const canvas = document.getElementById(canvasId);
    if (!canvas || !data || data.length === 0) return;

    const cores = walCharts._themeColors(walCharts._isDark());
    const medir = !!(opcoes && opcoes.medirVariacao);
    const percentual = !!(opcoes && opcoes.formato === 'percentual');
    // Valores ocultos (olho): mantém o desenho da linha, mas sem números no eixo nem no tooltip;
    // a medição mostra só a variação em %
    const oculto = !!(opcoes && opcoes.ocultarValores) && !percentual;
    const formatarPct = (v) => `${walCharts._percentualEixo.format(v)}%`;

    const chart = window.walCharts._instances[canvasId] = new Chart(canvas, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: label,
                data: data,
                borderColor: '#0d6efd',
                backgroundColor: 'rgba(13, 110, 253, 0.1)',
                fill: true,
                tension: 0.15,
                pointRadius: 0,
                borderWidth: 2
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            // Folga à direita do último ponto: sem ela, a última data fica colada na borda e é quase
            // impossível parar o mouse em cima dela (ver _bordaDireitaPlugin)
            layout: { padding: { right: walCharts._folgaDireita } },
            // Com a medição, o tooltip acompanha o eixo X (não exige acertar o ponto, que tem raio 0)
            interaction: medir ? { mode: 'index', intersect: false } : undefined,
            plugins: {
                legend: { display: false },
                tooltip: {
                    enabled: true,
                    callbacks: percentual
                        ? { label: (ctx) => `${ctx.dataset.label}: ${formatarPct(ctx.parsed.y)}` }
                        : oculto
                            ? { label: (ctx) => `${ctx.dataset.label}: R$ •••••` }
                            : {}
                }
            },
            scales: {
                x: {
                    ticks: { maxTicksLimit: 8, color: cores.text },
                    grid: { color: cores.grid }
                },
                y: {
                    beginAtZero: false,
                    ticks: percentual
                        ? { color: cores.text, callback: (v) => formatarPct(v) }
                        : { color: cores.text, display: !oculto },
                    grid: { color: cores.grid }
                }
            }
        },
        plugins: medir ? [walCharts._bordaDireitaPlugin, walCharts._medicaoPlugin] : [walCharts._bordaDireitaPlugin]
    });

    chart.$walFormato = percentual ? 'percentual' : oculto ? 'valor-oculto' : 'valor';
    if (medir) walCharts._ligarMedicao(chart);
};

// Pixels de folga entre o último ponto e a borda direita do gráfico
window.walCharts._folgaDireita = 8;

// O cursor na folga à direita do último ponto ativa a última data (ponto em destaque e tooltip).
// O Chart.js ignora eventos fora da área de plotagem e manteria a data anterior ativa.
window.walCharts._bordaDireitaPlugin = {
    id: 'walBordaDireita',
    afterEvent(chart, args) {
        const e = args.event;
        const a = chart.chartArea;
        if (e.type !== 'mousemove' || !a) return;
        if (e.x <= a.right || e.x > a.right + walCharts._folgaDireita + 4 || e.y < a.top || e.y > a.bottom) return;

        const ultimo = chart.data.labels.length - 1;
        const atual = chart.getActiveElements();
        if (atual.length === 1 && atual[0].index === ultimo) return;

        const ativos = [{ datasetIndex: 0, index: ultimo }];
        chart.setActiveElements(ativos);
        if (chart.options.plugins.tooltip.enabled) chart.tooltip.setActiveElements(ativos, { x: e.x, y: e.y });
        args.changed = true;
    }
};

// ---------------------------------------------------------------------------------------------
// Medição de variação (estilo Google Finance): clicar num ponto e arrastar até outro mostra a
// variação entre os dois, sempre do mais antigo para o mais recente. A seleção fica na tela
// depois de soltar o mouse; um clique simples a remove.
// ---------------------------------------------------------------------------------------------

window.walCharts._moeda = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
window.walCharts._percentual = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
window.walCharts._percentualEixo = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 });

// Cores de alta/baixa do Bootstrap (success/danger), na variação legível de cada tema
window.walCharts._coresMedicao = (isDark, alta) => ({
    serie: alta ? (isDark ? '#75b798' : '#198754') : (isDark ? '#ea868f' : '#dc3545'),
    fundo: isDark ? 'rgba(33, 37, 41, 0.92)' : 'rgba(255, 255, 255, 0.92)',
    secundario: isDark ? '#adb5bd' : '#6c757d'
});

// Texto principal da medição entre dois valores da série (v0 = mais antigo, v1 = mais recente).
//   'valor':      diferença em R$ e variação simples em %.
//   'percentual': a série é rentabilidade acumulada em %, então o período rende (1 + v1) / (1 + v0) - 1
//                 — a diferença simples (v1 - v0) erraria sempre que já houvesse ganho ou perda acumulado.
window.walCharts._resultadoMedicao = (formato, v0, v1) => {
    const sinal = (x) => x > 0 ? '+' : x < 0 ? '−' : '';
    const pct = (x) => `${sinal(x)}${walCharts._percentual.format(Math.abs(x))}%`;

    if (formato === 'percentual') {
        const base = 1 + v0 / 100;
        if (base <= 0) return { linha1: '—', alta: true };
        const periodo = ((1 + v1 / 100) / base - 1) * 100;
        return { linha1: `${pct(periodo)} no período`, alta: periodo >= 0 };
    }

    const diff = v1 - v0;
    const variacao = v0 !== 0 ? pct(diff / v0 * 100) : '—';
    // 'valor-oculto': mesma conta, sem mostrar o valor em R$
    if (formato === 'valor-oculto') return { linha1: `${variacao} no período`, alta: diff >= 0 };
    return { linha1: `${sinal(diff)}${walCharts._moeda.format(Math.abs(diff))}  (${variacao})`, alta: diff >= 0 };
};

window.walCharts._medicaoPlugin = {
    id: 'walMedicao',
    afterDraw(chart) {
        const sel = chart.$walMedicao;
        if (!sel || sel.inicio === null || sel.inicio === sel.fim) return;

        const dados = chart.data.datasets[0].data;
        const labels = chart.data.labels;
        const i0 = Math.min(sel.inicio, sel.fim);
        const i1 = Math.max(sel.inicio, sel.fim);
        const v0 = Number(dados[i0]);
        const v1 = Number(dados[i1]);
        const { linha1, alta } = walCharts._resultadoMedicao(chart.$walFormato, v0, v1);

        const isDark = walCharts._isDark();
        const cores = walCharts._coresMedicao(isDark, alta);
        const { ctx, chartArea: area, scales: { x, y } } = chart;
        const x0 = x.getPixelForValue(i0);
        const x1 = x.getPixelForValue(i1);

        ctx.save();

        // Faixa do período selecionado
        ctx.globalAlpha = 0.12;
        ctx.fillStyle = cores.serie;
        ctx.fillRect(x0, area.top, x1 - x0, area.bottom - area.top);
        ctx.globalAlpha = 1;

        // Linhas verticais nas pontas
        ctx.strokeStyle = cores.serie;
        ctx.lineWidth = 1;
        ctx.beginPath();
        for (const px of [x0, x1]) {
            ctx.moveTo(px, area.top);
            ctx.lineTo(px, area.bottom);
        }
        ctx.stroke();

        // Marcadores nos dois pontos da série
        ctx.lineWidth = 2;
        ctx.strokeStyle = isDark ? '#212529' : '#ffffff';
        ctx.fillStyle = cores.serie;
        for (const [px, v] of [[x0, v0], [x1, v1]]) {
            ctx.beginPath();
            ctx.arc(px, y.getPixelForValue(v), 4, 0, Math.PI * 2);
            ctx.fill();
            ctx.stroke();
        }

        // Caixa com o resultado, centralizada sobre a faixa e presa dentro da área do gráfico
        const linha2 = `${labels[i0]} → ${labels[i1]}`;
        const fonte = 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif';

        ctx.font = `600 13px ${fonte}`;
        const w1 = ctx.measureText(linha1).width;
        ctx.font = `12px ${fonte}`;
        const w2 = ctx.measureText(linha2).width;

        const largura = Math.max(w1, w2) + 16;
        const altura = 42;
        const bx = Math.max(area.left, Math.min(area.right - largura, (x0 + x1) / 2 - largura / 2));
        const by = area.top + 6;

        ctx.fillStyle = cores.fundo;
        ctx.strokeStyle = cores.serie;
        ctx.lineWidth = 1;
        ctx.beginPath();
        if (ctx.roundRect) ctx.roundRect(bx, by, largura, altura, 6);
        else ctx.rect(bx, by, largura, altura);
        ctx.fill();
        ctx.stroke();

        ctx.textBaseline = 'alphabetic';
        ctx.textAlign = 'left';
        ctx.font = `600 13px ${fonte}`;
        ctx.fillStyle = cores.serie;
        ctx.fillText(linha1, bx + 8, by + 18);
        ctx.font = `12px ${fonte}`;
        ctx.fillStyle = cores.secundario;
        ctx.fillText(linha2, bx + 8, by + 34);

        ctx.restore();
    }
};

window.walCharts._ligarMedicao = (chart) => {
    const canvas = chart.canvas;
    const total = chart.data.labels.length;
    const sel = chart.$walMedicao = { inicio: null, fim: null, arrastando: false };

    const posicao = (e) => {
        const rect = canvas.getBoundingClientRect();
        return { px: e.clientX - rect.left, py: e.clientY - rect.top };
    };

    const indiceNoPixel = (px) => {
        const i = Math.round(chart.scales.x.getValueForPixel(px));
        return Math.max(0, Math.min(total - 1, i));
    };

    // O tooltip padrão some enquanto há uma seleção, para não cobrir a caixa da medição
    const definirTooltip = (habilitado) => {
        if (chart.options.plugins.tooltip.enabled === habilitado) return;
        chart.options.plugins.tooltip.enabled = habilitado;
        if (!habilitado) chart.tooltip.setActiveElements([], { x: 0, y: 0 });
        chart.update('none');
    };

    const aoPressionar = (e) => {
        if (e.button !== 0) return;
        const { px, py } = posicao(e);
        const a = chart.chartArea;
        // A folga à direita conta como área: dá para começar a medição na última data
        if (px < a.left || px > a.right + walCharts._folgaDireita + 4 || py < a.top || py > a.bottom) return;

        sel.inicio = sel.fim = indiceNoPixel(px);
        sel.arrastando = true;
        // Mantém o arraste mesmo se o ponteiro sair do canvas
        if (canvas.setPointerCapture) canvas.setPointerCapture(e.pointerId);
        definirTooltip(false);
        chart.draw();
    };

    const aoMover = (e) => {
        if (!sel.arrastando) return;
        const i = indiceNoPixel(posicao(e).px);
        if (i !== sel.fim) {
            sel.fim = i;
            chart.draw();
        }
    };

    const aoSoltar = (e) => {
        if (!sel.arrastando) return;
        sel.arrastando = false;
        if (canvas.releasePointerCapture && canvas.hasPointerCapture && canvas.hasPointerCapture(e.pointerId))
            canvas.releasePointerCapture(e.pointerId);
        if (sel.inicio === sel.fim) {
            // Clique sem arraste: remove a seleção
            sel.inicio = sel.fim = null;
            definirTooltip(true);
            chart.draw();
        }
    };

    canvas.addEventListener('pointerdown', aoPressionar);
    canvas.addEventListener('pointermove', aoMover);
    canvas.addEventListener('pointerup', aoSoltar);
    canvas.addEventListener('pointercancel', aoSoltar);

    // No toque, arraste horizontal mede e arraste vertical continua rolando a página
    const estiloAnterior = { touchAction: canvas.style.touchAction, cursor: canvas.style.cursor };
    canvas.style.touchAction = 'pan-y';
    canvas.style.cursor = 'crosshair';

    chart.$walMedicaoRemover = () => {
        canvas.removeEventListener('pointerdown', aoPressionar);
        canvas.removeEventListener('pointermove', aoMover);
        canvas.removeEventListener('pointerup', aoSoltar);
        canvas.removeEventListener('pointercancel', aoSoltar);
        canvas.style.touchAction = estiloAnterior.touchAction;
        canvas.style.cursor = estiloAnterior.cursor;
    };
};

// Recolore os gráficos já em tela ao alternar o tema (chamado por walTheme.apply).
window.walCharts.applyTheme = (isDark) => {
    const cores = walCharts._themeColors(isDark);
    for (const id in walCharts._instances) {
        const chart = walCharts._instances[id];
        chart.options.scales.x.ticks.color = cores.text;
        chart.options.scales.y.ticks.color = cores.text;
        chart.options.scales.x.grid.color = cores.grid;
        chart.options.scales.y.grid.color = cores.grid;
        chart.update();
    }
};

window.walCharts.destroy = (canvasId) => {
    const chart = window.walCharts._instances[canvasId];
    if (chart) {
        // Os listeners da medição ficam no canvas, que o Blazor pode reaproveitar entre renderizações
        if (chart.$walMedicaoRemover) chart.$walMedicaoRemover();
        chart.destroy();
        delete window.walCharts._instances[canvasId];
    }
};
