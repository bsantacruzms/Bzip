// Renders the benchmark table from the real numbers in assets/benchmark.json.
fetch('assets/benchmark.json')
    .then(function (r) { if (!r.ok) throw new Error('no data'); return r.json(); })
    .then(render)
    .catch(function () {
        document.getElementById('benchmark').innerHTML =
            '<p class="subtle">Benchmark data will appear here once it has been generated.</p>';
    });

function render(d) {
    var meta = document.getElementById('bench-meta');
    if (d.cpu) {
        meta.textContent = 'Measured on ' + d.cpu + ' (' + d.logicalCores + ' cores) — ' +
            d.datasetMB + ' MB dataset (' + d.datasetDescription + '), ' + d.date + '.';
    }

    var results = d.results || [];
    if (!results.length) {
        document.getElementById('benchmark').innerHTML = '<p class="subtle">No benchmark results yet.</p>';
        return;
    }

    var maxComp = Math.max.apply(null, results.map(function (r) { return r.compressSec; }));

    var rows = results.map(function (r) {
        var me = (r.tool || '').toLowerCase().indexOf('boltzip') !== -1;
        var pct = Math.max(3, (r.compressSec / maxComp) * 100);
        return '<tr class="' + (me ? 'me' : '') + '">' +
            '<td class="tool">' + (me ? '⚡ ' : '') + r.tool + '</td>' +
            '<td>' + r.format + '</td>' +
            '<td class="num">' + r.compressSec + 's</td>' +
            '<td><div class="bar"><span style="width:' + pct.toFixed(0) + '%"></span></div></td>' +
            '<td class="num">' + r.extractSec + 's</td>' +
            '<td class="num">' + r.sizeMB + ' MB</td>' +
            '<td class="num">' + r.ratioPct + '%</td>' +
            '</tr>';
    }).join('');

    document.getElementById('benchmark').innerHTML =
        '<table><thead><tr>' +
        '<th>Tool</th><th>Format</th><th>Compress</th><th>Compress (relative)</th>' +
        '<th>Extract</th><th>Size</th><th>Ratio</th>' +
        '</tr></thead><tbody>' + rows + '</tbody></table>';
}
