const {chromium}=require('C:/Users/Daniel/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const path=require('node:path'),fs=require('node:fs'),assert=require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try {
  const page=await browser.newPage({viewport:{width:1120,height:820},reducedMotion:'reduce'}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>{window.__sent=[];window.chrome={webview:{postMessage:m=>__sent.push(m),addEventListener:(_,fn)=>window.__host=fn}};});
  await page.goto('file:///'+path.resolve('wwwroot/index.html').replaceAll('\\','/'));
  const host=(type,payload)=>page.evaluate(({type,payload})=>__host({data:{type,payload,settingsRequestId:__sent.filter(m=>m.action==='saveSettings').at(-1)?.settingsRequestId}}),{type,payload});
  await host('extendedSettings',{Theme:'dark',RoutingMode:'smart_ru',RoutingRegion:'cn',SetupCompleted:true,AiRoutingMode:'vpn'});
  await host('settings',{theme:'dark',httpPort:10809,socksPort:10808,routingMode:'smart_ru'});
  await page.locator('#navSettings').click();
  assert(await page.locator('#autoSaveIndicator').isHidden(),'no permanent saved status');
  await page.locator('#settingsSearch').fill('внешний');
  assert.equal(await page.locator('#tab-settings .settings-section:visible').count(),1);
  assert(await page.locator('#setUpstreamProxyHost').isVisible());
  assert.equal(await page.locator('#setUpstreamProxyHost').getAttribute('placeholder'),'proxy.example.com');
  await page.locator('#settingsSearch').fill('zzzzzz'); assert(await page.locator('#settingsEmpty').isVisible());
  await page.locator('#settingsSearch').fill('');
  await page.locator('#settingsCategories [data-category="appearance"]').click();
  assert(await page.locator('#setGlass').isVisible());assert(await page.locator('#setHttpPort').isHidden());
  await page.locator('#settingsCategories [data-category="all"]').click();
  await page.locator('#navRouting').click();
  await page.locator('#setRoutingRegion').selectOption('ir');
  const saved=await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings);assert.equal(saved.routingRegion,'ir');
  await page.locator('#navHome').click();assert((await page.locator('#quickRouteDescription').innerText()).includes('Ирана'));
  await page.locator('[data-route="global"]').click();assert.equal((await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1))).settings.routingMode,'global');
  await page.locator('#navRouting').click();
  await page.locator('#siteRuleDomain').fill('<img src=x onerror=alert(1)>');await page.locator('#previewSiteRule').click();
  assert.equal((await page.evaluate(()=>__sent.at(-1))).action,'previewSiteRule');
  await host('siteRulePreview',{message:'<script>unsafe()</script>'});assert.equal(await page.locator('#siteRulePreview script').count(),0);
  await page.locator('#siteRuleDomain').fill('example.com');await page.locator('#siteRuleDuration').selectOption('60');await page.locator('#saveSiteRule').click();
  assert.deepEqual(await page.evaluate(()=>__sent.at(-1)),{action:'saveSiteRule',domain:'example.com',route:'proxy',minutes:60});
  await host('extendedSettings',{Theme:'dark',RoutingRegion:'ir',SetupCompleted:true,TemporaryRoutes:[{Domain:'example.com',Route:'direct',ExpiresAt:new Date(Date.now()+3600000).toISOString()}]});
  assert((await page.locator('#temporarySiteRules').innerText()).includes('напрямую'));
  await page.locator('#temporarySiteRules button').click();assert.equal((await page.evaluate(()=>__sent.at(-1))).action,'removeSiteRule');
  await page.locator('#navSettings').click(); await page.locator('#testDns').click();assert.equal((await page.evaluate(()=>__sent.at(-1))).action,'testDns');
  await host('dnsDiagnostic',{message:'example.com\nЗадержка: 24 мс\nОтвет A: 203.0.113.8'});assert((await page.locator('#dnsTestResult').innerText()).includes('24 мс'));
  await page.locator('#btnSetupWizard').click();assert(await page.locator('#setupWizard').isVisible());
  await page.locator('#setupRegion').selectOption('global');await page.locator('#setupNext').click();await page.locator('#setupRoute').selectOption('smart_ru');
  await page.locator('#setupNext').click();await page.locator('#setupNext').click();await page.locator('#setupApps').selectOption('proxy');await page.locator('#setupNext').click();
  const wizardSave=await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings);assert(wizardSave.setupCompleted);assert.equal(wizardSave.routingRegion,'global');
  fs.mkdirSync('tests/artifacts/desktop-refresh',{recursive:true});
  for(const width of [820,1120,1440]){
   await page.setViewportSize({width,height:820});
   for(const nav of ['Home','Settings','Routing']){
    await page.locator('#nav'+nav).click();
    assert(!await page.locator('.tab-pane.active').evaluate(e=>e.scrollWidth>e.clientWidth+2),nav+' horizontal overflow '+width);
    await page.screenshot({path:`tests/artifacts/desktop-refresh/${nav}-${width}.png`});
   }
  }
  assert.deepEqual(errors,[]);
  console.log('PASS settings search/categories, privacy, quick route/region IPC, temporary rules, safe preview, DNS UI, wizard, 820–1440 layouts');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
