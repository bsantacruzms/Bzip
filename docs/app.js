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
        var method = d.iterations && d.statistic
            ? d.statistic + ' of ' + d.iterations + ' measured runs after warm-up; '
            : '';
        meta.textContent = 'Measured on ' + d.cpu + ' (' + d.logicalCores + ' cores), ' +
            method + d.datasetMB + ' MB dataset (' + d.datasetDescription + '), ' + d.date + '.';
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

// Media chart: same numbers, but for already-compressed video/photo/audio.
fetch('assets/benchmark-media.json')
    .then(function (r) { if (!r.ok) throw new Error('no data'); return r.json(); })
    .then(renderMedia)
    .catch(function () {
        var el = document.getElementById('benchmark-media');
        if (el) { el.innerHTML = '<p class="subtle">Media benchmark will appear here once it has been generated.</p>'; }
    });

function renderMedia(d) {
    var host = document.getElementById('benchmark-media');
    if (!host) { return; }
    var results = d.results || [];
    if (!results.length) { host.innerHTML = '<p class="subtle">No media benchmark results yet.</p>'; return; }

    var maxComp = Math.max.apply(null, results.map(function (r) { return r.compressSec; }));
    var rows = results.map(function (r) {
        var me = (r.tool || '').toLowerCase().indexOf('boltzip') !== -1;
        var pct = Math.max(3, (r.compressSec / maxComp) * 100);
        return '<tr class="' + (me ? 'me' : '') + '">' +
            '<td class="tool">' + (me ? '⚡ ' : '') + r.tool + '</td>' +
            '<td>' + r.format + '</td>' +
            '<td class="num">' + r.compressSec + 's</td>' +
            '<td><div class="bar"><span style="width:' + pct.toFixed(0) + '%"></span></div></td>' +
            '<td class="num">' + r.sizeMB + ' MB</td>' +
            '<td class="num">' + r.ratioPct + '%</td>' +
            '</tr>';
    }).join('');

    host.innerHTML =
        '<table><thead><tr>' +
        '<th>Tool</th><th>Format</th><th>Compress</th><th>Compress (relative)</th>' +
        '<th>Size</th><th>Ratio</th>' +
        '</tr></thead><tbody>' + rows + '</tbody></table>';

    var meta = document.getElementById('bench-media-meta');
    if (meta && d.cpu) {
        var method = d.iterations && d.statistic ? d.statistic + ' of ' + d.iterations + ' runs; ' : '';
        meta.textContent = 'Measured on ' + d.cpu + ' (' + d.logicalCores + ' cores), ' + method +
            d.datasetMB + ' MB ' + d.datasetDescription + ', ' + d.date +
            '. Lower compress time is better; a ratio near 100% correctly means no shrink.';
    }
}

// Video chart: the one place BoltZip shrinks media — GPU re-encoding vs archivers that can't.
fetch('assets/benchmark-video.json')
    .then(function (r) { if (!r.ok) throw new Error('no data'); return r.json(); })
    .then(renderVideo)
    .catch(function () {
        var el = document.getElementById('benchmark-video');
        if (el) { el.innerHTML = '<p class="subtle">Video benchmark will appear here once it has been generated.</p>'; }
    });

function renderVideo(d) {
    var host = document.getElementById('benchmark-video');
    if (!host) { return; }
    var results = d.results || [];
    if (!results.length) { host.innerHTML = '<p class="subtle">No video benchmark results yet.</p>'; return; }

    var rows = results.map(function (r) {
        var me = (r.tool || '').toLowerCase().indexOf('boltzip') !== -1;
        var pct = Math.max(1, r.reductionPct);
        return '<tr class="' + (me ? 'me' : '') + '">' +
            '<td class="tool">' + (me ? '⚡ ' : '') + r.tool + '</td>' +
            '<td class="num">' + r.outMB + ' MB</td>' +
            '<td><div class="bar"><span style="width:' + pct.toFixed(0) + '%"></span></div></td>' +
            '<td class="num">' + r.reductionPct + '% smaller</td>' +
            '</tr>';
    }).join('');

    host.innerHTML =
        '<table><thead><tr>' +
        '<th>Tool</th><th>Result</th><th>Size reduction</th><th></th>' +
        '</tr></thead><tbody>' + rows + '</tbody></table>';

    var meta = document.getElementById('bench-video-meta');
    if (meta && d.gpu && results[0]) {
        meta.textContent = 'A ' + d.sourceMB + ' MB ' + d.sourceLabel + ' shrunk to ' + results[0].outMB +
            ' MB on ' + d.gpu + ' at ' + d.quality + ' quality (~' + d.encodeSeconds + 's). ' +
            'Lossless archivers leave video unchanged. ' + d.date + '.';
    }
}

// ---- Download buttons: point to the latest GitHub release assets ----
(function () {
    var REPO = 'bsantacruzms/Bzip';
    var LATEST_PAGE = 'https://github.com/' + REPO + '/releases/latest';

    // Button id -> matcher over the release asset file name.
    var MAP = {
        'dl-win-setup': function (n) { return /-setup\.exe$/i.test(n); },
        'dl-win-msi': function (n) { return /\.msi$/i.test(n); },
        'dl-win-exe': function (n) { return /^BoltZipTool-.*\.exe$/i.test(n); },
        'dl-win-cli': function (n) { return /^bz-.*\.exe$/i.test(n); },
        'dl-mac-arm': function (n) { return /arm64\.dmg$/i.test(n); },
        'dl-mac-x64': function (n) { return /x64\.dmg$/i.test(n); },
        'dl-lin-deb': function (n) { return /amd64\.deb$/i.test(n); },
        'dl-lin-rpm': function (n) { return /x86_64\.rpm$/i.test(n); },
        'dl-lin-tar': function (n) { return /linux-x64\.tar\.gz$/i.test(n); }
    };

    function detectOs() {
        var s = (navigator.userAgent || '') + ' ' + (navigator.platform || '');
        if (/Windows|Win32|Win64/i.test(s)) return 'win';
        if (/Macintosh|Mac OS X|MacIntel/i.test(s)) return 'mac';
        if (/Linux|X11/i.test(s)) return 'linux';
        return 'win';
    }

    function setHref(id, url) {
        var el = document.getElementById(id);
        if (el && url) { el.href = url; }
    }

    function updateHero(assets) {
        var os = detectOs();
        var label = { win: 'Windows', mac: 'macOS', linux: 'Linux' }[os];
        var match = {
            win: function (n) { return /-setup\.exe$/i.test(n); },
            mac: function (n) { return /arm64\.dmg$/i.test(n) || /\.dmg$/i.test(n); },
            linux: function (n) { return /amd64\.deb$/i.test(n) || /linux-x64\.tar\.gz$/i.test(n); }
        }[os];
        var hero = document.getElementById('hero-download');
        if (!hero) { return; }
        hero.textContent = 'Download for ' + label;
        if (assets) {
            var a = assets.filter(function (x) { return match(x.name); })[0];
            if (a) { hero.href = a.browser_download_url; }
        }
    }

    fetch('https://api.github.com/repos/' + REPO + '/releases/latest', {
        headers: { 'Accept': 'application/vnd.github+json' }
    })
        .then(function (r) { if (!r.ok) { throw new Error('no release'); } return r.json(); })
        .then(function (rel) {
            var assets = rel.assets || [];
            var ver = document.getElementById('dl-version');
            if (ver) {
                var when = rel.published_at ? ' · ' + new Date(rel.published_at).toLocaleDateString() : '';
                ver.innerHTML = 'Latest release: <strong>' + (rel.tag_name || 'latest') + '</strong>' + when +
                    ' · <a href="' + LATEST_PAGE + '">all downloads</a>';
            }
            Object.keys(MAP).forEach(function (id) {
                var a = assets.filter(function (x) { return MAP[id](x.name); })[0];
                if (a) { setHref(id, a.browser_download_url); }
            });
            updateHero(assets);
        })
        .catch(function () {
            var ver = document.getElementById('dl-version');
            if (ver) {
                ver.innerHTML = 'Get the newest build on the <a href="' + LATEST_PAGE + '">releases page</a>.';
            }
            updateHero(null);
        });
})();
