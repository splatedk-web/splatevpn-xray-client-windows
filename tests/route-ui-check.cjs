const {chromium}=require('C:/Users/Daniel/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const path=require('node:path'),assert=require('node:assert/strict'),fs=require('node:fs');
(async()=>{
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try {
  const page=await browser.newPage({viewport:{width:1120,height:800}}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>{window.__sent=[];window.chrome={webview:{postMessage:m=>__sent.push(m),addEventListener:(_,fn)=>window.__host=fn}};});
  await page.goto('file:///'+path.resolve('wwwroot/index.html').replaceAll('\\','/'));
  const host=(type,payload)=>page.evaluate(({type,payload})=>__host({data:{type,payload}}),{type,payload});
  await host('extendedSettings',{Theme:'dark',AiRoutingMode:'vpn',EnableAiBypass:true,EnableAiDns:false,AiDnsProvider:'dns_ai'});
  await host('settings',{theme:'dark',httpPort:10809,socksPort:10808,enableAiBypass:true,enableAiXboxDns:false,dnsProvider:'cloudflare'});
  assert(await page.locator('#setAiMode').isVisible(),'one route selector on home');
  assert.equal(await page.locator('#setAiMode').inputValue(),'vpn');
  assert(await page.locator('#setAiProxy').isHidden());assert(await page.locator('#setAiDns').isHidden());assert(await page.locator('#setAiXboxDns').isHidden());
  assert.equal(await page.locator('#setDnsProvider option[value="xbox"]').count(),0);
  for(const mode of ['dns_ai','off','vpn','dns_ai']) {
   await page.locator('#setAiMode').selectOption(mode);
   const saved=await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings);
   assert.equal(saved.aiRoutingMode,mode);assert.equal(saved.enableAiDns,mode==='dns_ai');assert.equal(saved.enableAiBypass,mode!=='off');assert.equal(saved.enableAiXboxDns,false);
   assert.equal(await page.locator('#btnQuickAiDns').isVisible(),mode==='dns_ai');
  }
  await page.locator('#btnQuickAiDns').click();assert.equal((await page.evaluate(()=>__sent.at(-1))).action,'selectAiDns');assert(await page.locator('#btnSelectAiDns').isVisible());
  await host('aiDnsSelection',{Phase:'no_match',Completed:14,Total:14,Results:[{Choice:{ProviderKey:'dns_ai',TlsFragment:false},Targets:[{Service:'Gemini',Outcome:2,Detail:'JavaScript: региональный запрет на странице Gemini'}]}]});
  assert((await page.locator('#aiDnsSelectionResults').innerText()).includes('региональный запрет'));assert((await page.locator('#aiDnsSelectionStatus').innerText()).includes('Настройки не изменены'));
  await page.locator('#navHome').click();await page.locator('#btnCheckAi').click();assert.equal((await page.evaluate(()=>__sent.at(-1))).action,'checkAiConnectivity');
  await host('state',{isConnected:true,activeTunMode:true,statusText:'СВЯЗЬ НЕ ПОДТВЕРЖДЕНА',statusDetail:'VPN остаётся включённым',currentIp:'Не удалось определить'});
  assert((await page.locator('#headerStatusIndicator').getAttribute('class')).includes('connecting'));assert((await page.locator('#btnToggleConnection').innerText()).includes('Отключиться'));
  await page.setViewportSize({width:820,height:600});assert(!await page.locator('#tab-home').evaluate(e=>e.scrollWidth>e.clientWidth+2));
  fs.mkdirSync('tests/artifacts/2.12.2',{recursive:true});await page.screenshot({path:'tests/artifacts/2.12.2/ai-home.png'});
  assert.deepEqual(errors,[]);console.log('PASS one-click AI modes, consistent flags, no obsolete Xbox controls, direct Gemini selection, regional diagnostics and warning while VPN stays on');
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
