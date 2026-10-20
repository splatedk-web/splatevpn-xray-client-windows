// Capture the current WebView UI with public demo data, without starting a VPN.
// npm install --no-save playwright; node scripts/capture-readme.cjs
const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const web = path.join(root, 'wwwroot');
const output = path.join(root, 'docs/screenshots');
const mime = { '.html': 'text/html', '.js': 'application/javascript', '.json': 'application/json', '.css': 'text/css', '.png': 'image/png' };
const server = http.createServer((req, res) => {
  const file = path.resolve(web, '.' + new URL(req.url, 'http://localhost').pathname);
  if (!file.startsWith(web + path.sep)) { res.writeHead(403).end(); return; }
  fs.readFile(file, (error, data) => {
    if (error) { res.writeHead(404).end(); return; }
    res.setHeader('Content-Type', mime[path.extname(file)] || 'application/octet-stream');
    res.end(data);
  });
});
(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  let browser;
  try {
    browser = await chromium.launch({ headless: true, channel: 'msedge' });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.addInitScript(() => {
      window.chrome = { webview: { postMessage() {}, addEventListener(_, listener) { window.__readmeHost = listener; } } };
    });
    await page.goto(`http://127.0.0.1:${server.address().port}/index.html`);
    await page.waitForFunction(() => !!window.__readmeHost && !!window.SplateObjects);
    const host = (type, payload) => page.evaluate(({ type, payload }) => window.__readmeHost({ data: { type, payload } }), { type, payload });
    const project = fs.readFileSync(path.join(root, 'SplateClientDesktop.csproj'), 'utf8');
    const version = project.match(/<Version>([^<]+)<\/Version>/)[1];
    const providerSource = fs.readFileSync(path.join(root, 'Services/SmartDnsProviders.cs'), 'utf8');
    const providers = [...providerSource.matchAll(/new\("([^"]+)", "([^"]+)", "([^"]+)"/g)]
      .map(([, Key, Label, Host]) => ({ Key, Label, Host }));
    const nodes = [
      { id: 'demo-vless', displayName: 'Пример · VLESS Reality', address: 'vpn.example.com', protocol: 'vless', transport: 'xhttp', security: 'reality' },
      { id: 'demo-trojan', displayName: 'Пример · Trojan TLS', address: 'trojan.example.com', protocol: 'trojan', transport: 'tcp', security: 'tls' },
      { id: 'demo-vmess', displayName: 'Пример · VMess WebSocket', address: 'vmess.example.com', protocol: 'vmess', transport: 'ws', security: 'tls' }
    ].map(node => ({ ...node, port: 443, ping: 0, enabled: true, subscriptionId: 'demo', subscriptionGroup: 'Демонстрационная подписка' }));
    await host('smartDnsProviders', providers);
    await host('extendedSettings', { Theme: 'sunset', GlassTransparency: .48, GlassBlurEnabled: true, AiRoutingMode: 'vpn', AiDnsProvider: 'dns_ai', EnableAiBypass: true, EnableAiDns: false, EnableTunMode: false, GroupServers: true, EnableAdBlock: true });
    await host('settings', { theme: 'sunset', httpPort: 10809, socksPort: 10808, enableTunMode: false, isAdministrator: false, routingMode: 'smart_ru', dnsProvider: 'cloudflare', enableAiBypass: true, enableSniffing: true, blockQuic: true, blockIpv6: true, minimizeToTray: true });
    await host('subscriptions', [{ id: 'demo', name: 'Демонстрационная подписка', enabled: true, nodeCount: nodes.length }]);
    await host('servers', nodes);
    await host('state', { isConnected: false, isConnecting: false, statusText: 'ОТКЛЮЧЕНО', selectedServer: nodes[0], currentIp: '—', currentLocation: '' });
    await host('appUpdate', { currentVersion: version, checking: false, available: false, status: 'Демонстрация интерфейса. Проверка обновлений не запускалась.' });
    await page.waitForFunction(() => Object.values(SplateObjects.images).every(image => image.complete && image.naturalWidth));
    await page.evaluate(() => document.fonts.ready);
    fs.mkdirSync(output, { recursive: true });
    for (const [nav, name] of [['navHome', 'home'], ['navServers', 'servers'], ['navRouting', 'routing'], ['navSettings', 'settings']]) {
      await page.locator('#' + nav).click();
      await page.waitForTimeout(350);
      assert.equal(await page.locator('.version-label').textContent(), `SplateVPN Desktop v${version}`);
      assert.equal(await page.locator('.tab-pane.active').evaluate(element => element.scrollWidth > element.clientWidth + 2), false, name + ' horizontal overflow');
      await page.screenshot({ path: path.join(output, name + '.png') });
    }
    assert.deepEqual(errors, []);
    console.log(`Captured four ${version} UI screenshots with demo data; no native IPC or VPN connection.`);
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
