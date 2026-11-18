namespace Building.Simulator;

/// <summary>The tiny HTML dev panel served at <c>GET /</c>.</summary>
public static class DevPanel
{
    public const string Html = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><title>Building simulator</title>
        <style>body{font:14px system-ui;margin:24px;max-width:640px}fieldset{margin:12px 0}button{margin:4px}#out{white-space:pre;background:#f4f4f4;padding:8px}</style></head>
        <body><h1>Building simulator</h1>
        <label>Door <select id="door"></select></label>
        <fieldset><legend>Swipe</legend>
          <input id="card" placeholder="card number"> <input id="person" placeholder="person ref"> <input id="holder" placeholder="holder name">
          <button onclick="swipe()">Swipe</button></fieldset>
        <fieldset><legend>Scenarios</legend>
          <button onclick="scenario('forced-door')">Forced door</button>
          <button onclick="scenario('held-open')">Held open</button>
          <button onclick="scenario('fire-alarm')">Fire alarm</button>
          <button onclick="clearScenario('fire-alarm')">Clear fire alarm</button>
          <button onclick="scenario('pacs-slow')">PACS slow</button>
          <button onclick="clearScenario('pacs-slow')">Clear PACS slow</button></fieldset>
        <div id="out"></div>
        <script>
        const out = t => document.getElementById('out').textContent = t;
        const val = id => document.getElementById(id).value || null;
        fetch('/doors').then(r => r.json()).then(ds => { const s = document.getElementById('door');
          ds.forEach(d => { const o = document.createElement('option'); o.value = o.textContent = d.doorRef; s.appendChild(o); }); });
        async function post(url, body) { const r = await fetch(url, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body ?? {}) });
          out(r.status + ' ' + (await r.text())); }
        function swipe() { post('/swipes', { doorRef: val('door'), cardNumber: val('card'), personRef: val('person'), holderName: val('holder') }); }
        function scenario(n) { post('/scenarios/' + n, { doorRef: val('door') }); }
        function clearScenario(n) { post('/scenarios/' + n + '/clear'); }
        </script></body></html>
        """;
}
