window.walCharts = window.walCharts || {};
window.walCharts._instances = window.walCharts._instances || {};

// Cores de eixos/grade por tema — as cores das séries funcionam nos dois temas.
window.walCharts._themeColors = (isDark) => isDark
    ? { text: '#adb5bd', grid: 'rgba(255, 255, 255, 0.08)' }
    : { text: '#666666', grid: 'rgba(0, 0, 0, 0.08)' };

window.walCharts._isDark = () =>
    document.documentElement.getAttribute('data-tema') === 'dark';

window.walCharts._moeda = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
window.walCharts._moedaCompacta = new Intl.NumberFormat('pt-BR', { notation: 'compact', maximumFractionDigits: 1 });
window.walCharts._percentual = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
window.walCharts._percentualEixo = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 });

// Uma série só (cotação do ativo). Mantida para as telas antigas; é um atalho de renderSeries.
window.walCharts.renderLine = (canvasId, labels, data, label, opcoes) =>
    window.walCharts.renderSeries(canvasId, labels, [{ label: label, data: data }], opcoes);

// Várias séries no mesmo eixo. A primeira é a principal: recebe a medição e o preenchimento.
//   series: [{ label, data, cor, tracejado, preenchido }]
//   opcoes.medirVariacao: liga a medição por clique e arraste (ver _ligarMedicao).
//   opcoes.formato: 'valor' (padrão) ou 'percentual' — série em % acumulado (12.5 = 12,5%); formata eixo e
//     tooltip em % e faz a medição calcular a rentabilidade composta do período, de cada série.
//   opcoes.ocultarValores: mantém o desenho, sem R$ no eixo, no tooltip nem na medição.
//   opcoes.marcadores: [{ indice, tipo: 'compra'|'venda'|'provento'|'evento', descricao }] — pontos desenhados
//     sobre a série principal, com a descrição no tooltip (ver _estilosMarcador).
// Proventos menores: ativos que pagam todo mês encheriam a linha de pontos
window.walCharts._estilosMarcador = {
    compra: { rotulo: 'Compras', cor: '#0b7a34', estilo: 'triangle', rotacao: 0, raio: 6 },
    venda: { rotulo: 'Vendas', cor: '#c5221f', estilo: 'triangle', rotacao: 180, raio: 6 },
    provento: { rotulo: 'Proventos', cor: '#2a78d6', estilo: 'circle', rotacao: 0, raio: 3.5 },
    evento: { rotulo: 'Eventos', cor: '#6250d6', estilo: 'rectRot', rotacao: 0, raio: 7 }
};

window.walCharts.renderSeries = (canvasId, labels, series, opcoes) => {
    // Destrói a instância anterior primeiro: se o canvas saiu da tela (período sem dados, troca de ativo),
    // o gráfico antigo não pode ficar preso a um elemento que não existe mais.
    window.walCharts.destroy(canvasId);

    const canvas = document.getElementById(canvasId);
    if (!canvas || !series || series.length === 0 || !series[0].data || series[0].data.length === 0) return;

    opcoes = opcoes || {};
    const cores = walCharts._themeColors(walCharts._isDark());
    const medir = !!opcoes.medirVariacao;
    const percentual = opcoes.formato === 'percentual';
    const oculto = !!opcoes.ocultarValores && !percentual;
    const formatarPct = (v) => `${walCharts._percentualEixo.format(v)}%`;

    const datasets = series.map((s, i) => {
        const cor = s.cor || '#0d6efd';
        return {
            label: s.label,
            data: s.data,
            borderColor: cor,
            backgroundColor: s.preenchido || (i === 0 && s.preenchido === undefined) ? cor + '1a' : 'transparent',
            fill: s.preenchido === undefined ? i === 0 : !!s.preenchido,
            borderDash: s.tracejado ? [6, 4] : [],
            tension: 0.15,
            pointRadius: 0,
            pointHoverRadius: 3,
            borderWidth: i === 0 ? 2 : 1.75,
            spanGaps: true,
            order: i === 0 ? 0 : 1
        };
    });

    // Marcadores: um dataset por tipo, só com valor nos índices marcados (o resto é null e não aparece)
    const marcadores = opcoes.marcadores || [];
    for (const [tipo, e] of Object.entries(walCharts._estilosMarcador)) {
        const doTipo = marcadores.filter(m => m.tipo === tipo && m.indice >= 0 && m.indice < labels.length);
        if (doTipo.length === 0) continue;
        const dados = new Array(labels.length).fill(null);
        const descricoes = new Array(labels.length).fill(null);
        for (const m of doTipo) {
            dados[m.indice] = series[0].data[m.indice];
            // Dois eventos no mesmo pregão viram uma linha só no tooltip
            descricoes[m.indice] = descricoes[m.indice] ? `${descricoes[m.indice]} · ${m.descricao}` : m.descricao;
        }
        datasets.push({
            label: e.rotulo,
            data: dados,
            showLine: false,
            fill: false,
            pointStyle: e.estilo,
            rotation: e.rotacao,
            pointRadius: e.raio,
            pointHoverRadius: e.raio + 2,
            pointBackgroundColor: e.cor,
            pointBorderColor: '#ffffff',
            pointBorderWidth: 1,
            borderColor: e.cor,
            backgroundColor: e.cor,
            order: -1,
            $walMarcador: true,
            $walDescricoes: descricoes
        });
    }

    const rotulo = (ctx) => {
        if (ctx.dataset.$walMarcador) return ctx.dataset.$walDescricoes[ctx.dataIndex];
        if (ctx.parsed.y === null || ctx.parsed.y === undefined) return `${ctx.dataset.label}: —`;
        if (percentual) return `${ctx.dataset.label}: ${formatarPct(ctx.parsed.y)}`;
        if (oculto) return `${ctx.dataset.label}: R$ •••••`;
        return `${ctx.dataset.label}: ${walCharts._moeda.format(ctx.parsed.y)}`;
    };

    const chart = window.walCharts._instances[canvasId] = new Chart(canvas, {
        type: 'line',
        data: { labels: labels, datasets: datasets },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            // Folga à direita do último ponto: sem ela, a última data fica colada na borda e é quase
            // impossível parar o mouse em cima dela (ver _bordaDireitaPlugin)
            layout: { padding: { right: walCharts._folgaDireita } },
            // O tooltip acompanha o eixo X (não exige acertar o ponto, que tem raio 0)
            interaction: { mode: 'index', intersect: false },
            plugins: {
                legend: { display: false },
                tooltip: { enabled: true, callbacks: { label: rotulo } }
            },
            scales: {
                x: {
                    ticks: { maxTicksLimit: 8, maxRotation: 0, color: cores.text },
                    grid: { color: cores.grid }
                },
                y: {
                    beginAtZero: false,
                    ticks: percentual
                        ? { color: cores.text, callback: (v) => formatarPct(v) }
                        : { color: cores.text, display: !oculto, callback: (v) => `R$ ${walCharts._moedaCompacta.format(v)}` },
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
        if (atual.length > 0 && atual[0].index === ultimo) return;

        const ativos = chart.data.datasets.map((_, i) => ({ datasetIndex: i, index: ultimo }));
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

// Cores de alta/baixa (success/danger), na variação legível de cada tema
window.walCharts._coresMedicao = (isDark, alta) => ({
    serie: alta ? (isDark ? '#75b798' : '#198754') : (isDark ? '#ea868f' : '#dc3545'),
    fundo: isDark ? 'rgba(33, 37, 41, 0.92)' : 'rgba(255, 255, 255, 0.92)',
    texto: isDark ? '#e9ecef' : '#212529',
    secundario: isDark ? '#adb5bd' : '#6c757d'
});

window.walCharts._sinalPct = (x) =>
    `${x > 0 ? '+' : x < 0 ? '−' : ''}${walCharts._percentual.format(Math.abs(x))}%`;

// Variação de uma série percentual (rentabilidade acumulada em %) entre dois pontos:
// (1 + v1) / (1 + v0) - 1 — a diferença simples erraria sempre que já houvesse ganho ou perda acumulado.
window.walCharts._periodoPercentual = (v0, v1) => {
    const base = 1 + v0 / 100;
    return base <= 0 ? null : ((1 + v1 / 100) / base - 1) * 100;
};

// Texto principal da medição entre dois valores da série principal (v0 = mais antigo, v1 = mais recente).
//   'valor':      diferença em R$ e variação simples em %.
//   'percentual': rentabilidade composta do período.
window.walCharts._resultadoMedicao = (formato, v0, v1) => {
    if (formato === 'percentual') {
        const periodo = walCharts._periodoPercentual(v0, v1);
        if (periodo === null) return { linha1: '—', alta: true };
        return { linha1: `${walCharts._sinalPct(periodo)} no período`, alta: periodo >= 0 };
    }

    const diff = v1 - v0;
    const variacao = v0 !== 0 ? walCharts._sinalPct(diff / v0 * 100) : '—';
    // 'valor-oculto': mesma conta, sem mostrar o valor em R$
    if (formato === 'valor-oculto') return { linha1: `${variacao} no período`, alta: diff >= 0 };
    const sinal = diff > 0 ? '+' : diff < 0 ? '−' : '';
    return { linha1: `${sinal}${walCharts._moeda.format(Math.abs(diff))}  (${variacao})`, alta: diff >= 0 };
};

window.walCharts._medicaoPlugin = {
    id: 'walMedicao',
    afterDraw(chart) {
        const sel = chart.$walMedicao;
        if (!sel || sel.inicio === null || sel.inicio === sel.fim) return;

        const datasets = chart.data.datasets;
        const labels = chart.data.labels;
        const i0 = Math.min(sel.inicio, sel.fim);
        const i1 = Math.max(sel.inicio, sel.fim);
        const v0 = Number(datasets[0].data[i0]);
        const v1 = Number(datasets[0].data[i1]);
        const { linha1, alta } = walCharts._resultadoMedicao(chart.$walFormato, v0, v1);

        // Rentabilidade: cada benchmark no mesmo período, para comparar com a carteira
        const comparacoes = chart.$walFormato !== 'percentual' ? [] : datasets.slice(1).filter(d => !d.$walMarcador)
            .map(d => {
                const a = d.data[i0], b = d.data[i1];
                if (a === null || a === undefined || b === null || b === undefined) return null;
                const p = walCharts._periodoPercentual(Number(a), Number(b));
                return p === null ? null : { texto: `${d.label}: ${walCharts._sinalPct(p)}`, cor: d.borderColor };
            })
            .filter(c => c !== null);

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

        // Marcadores nos dois pontos da série principal
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
        const linhaDatas = `${labels[i0]} → ${labels[i1]}`;
        const fonte = 'Roboto, system-ui, -apple-system, "Segoe UI", sans-serif';
        const alturaLinha = 16;

        ctx.font = `600 13px ${fonte}`;
        let largura = ctx.measureText(linha1).width;
        ctx.font = `12px ${fonte}`;
        largura = Math.max(largura, ctx.measureText(linhaDatas).width);
        for (const c of comparacoes) largura = Math.max(largura, ctx.measureText(c.texto).width + 14);
        largura += 16;

        const altura = 42 + comparacoes.length * alturaLinha + (comparacoes.length ? 4 : 0);
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
        comparacoes.forEach((c, k) => {
            const ly = by + 18 + (k + 1) * alturaLinha;
            ctx.fillStyle = c.cor;
            ctx.fillRect(bx + 8, ly - 8, 8, 8);
            ctx.fillStyle = cores.texto;
            ctx.fillText(c.texto, bx + 22, ly);
        });

        ctx.fillStyle = cores.secundario;
        ctx.fillText(linhaDatas, bx + 8, by + altura - 8);

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
