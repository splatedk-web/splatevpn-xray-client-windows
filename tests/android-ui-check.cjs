const {chromium}=require('C:/Users/Daniel/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const path=require('node:path');
const assert=require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{
 const page=await browser.newPage({viewport:{width:1120,height:780},reducedMotion:'reduce'});
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.addInitScript(()=>{window.__sent=[];window.chrome={webview:{postMessage:m=>__sent.push(m),addEventListener:(_,fn)=>window.__host=fn}};});
 await page.goto('file:///'+path.resolve('wwwroot/index.html').replaceAll('\\','/'));
 const host=async(type,payload)=>page.evaluate(({type,payload})=>__host({data:{type,payload}}),{type,payload});
 const has=async(action,id)=>page.evaluate(({action,id})=>__sent.some(m=>m.action===action&&(!id||m.id===id)),{action,id});
 const saved=async()=>page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings);
 await host('settings',{theme:'dark',dnsProvider:'cloudflare',enableSniffing:true,enableAiBypass:true,httpPort:10809,socksPort:10808,blockIpv6:true,blockQuic:true});
 await host('subscriptions',[
  {id:'preset_white_cidr',name:'Белый список (CIDR RU)',enabled:true,autoUpdate:true,isPreset:true,nodeCount:1,usage:'Без ограничений',expiry:'Бессрочно',lastUpdated:'Локальная копия'},
  {id:'private',name:'Моя <подписка>',enabled:true,autoUpdate:false,nodeCount:1,usage:'1 / 10 ГБ',usagePercentage:10,expiry:'До 30.12.2026',lastUpdated:'Обновлено сегодня'},
  {id:'off',name:'Отключённая',enabled:false,autoUpdate:true,nodeCount:1,usage:'Без ограничений'}]);
 await host('servers',[
  {id:'preset-node',displayName:'Встроенный',address:'preset.example',port:443,protocol:'vless',ping:40,subscriptionId:'preset_white_cidr',subscriptionGroup:'Белый список (CIDR RU)',enabled:true},
  {id:'private-node',displayName:'Личный VPN',address:'private.example',port:443,protocol:'trojan',ping:50,subscriptionId:'private',subscriptionGroup:'Моя <подписка>',isFavorite:true,enabled:true},
  {id:'off-node',displayName:'Отключённый узел',address:'off.example',port:443,protocol:'vless',ping:1,subscriptionId:'off',subscriptionGroup:'Отключённая',enabled:false}]);
 await page.locator('#navServers').click();
 assert.equal(await page.locator('.server-card').count(),2,'disabled nodes hidden in All');
 await page.locator('[data-filter="preset"]').click();assert.equal(await page.locator('.server-card').count(),1);
 await page.locator('[data-filter="subscriptions"]').click();assert.equal(await page.locator('.server-card').count(),2);
 await page.locator('.server-card[data-id="off-node"] .card-name').click();assert(!await has('selectServer'));
 await page.locator('[data-filter="favorites"]').click();assert.equal(await page.locator('.server-card').count(),1);
 await page.locator('.server-card [data-action="favorite"]').click();assert(await has('favoriteServer','private-node'));
 await page.locator('[data-filter="all"]').click();
 await page.locator('#subscriptionSource').selectOption('private');assert.equal(await page.locator('.server-card').count(),1);
 await page.locator('#subscriptionSource').selectOption('Все');await page.locator('#groupServers').check();
 assert.equal(await page.locator('.server-group-heading').count(),3);
 await page.locator('.server-group-heading').first().click();assert.equal(await page.locator('.server-card:visible').count(),1);
 await page.locator('.server-group-heading').first().click();
 await page.locator('#subscriptionsPanel').evaluate(e=>e.open=true);
 await page.locator('[data-sub-id="private"] [data-sub-action="auto"]').check();assert(await has('subscriptionOptions','private'));
 await page.locator('[data-sub-id="private"] [data-sub-action="refresh"]').click();assert(await has('refreshOneSubscription','private'));
 await page.locator('#btnRestorePresets').click();assert(await has('restorePresets'));
 assert.equal(await page.locator('#subscriptionsList img').count(),0);
 await page.screenshot({path:'tests/artifacts/android-servers.png'});
 await page.locator('.server-card[data-id="private-node"] summary').click();
 await page.locator('.server-card[data-id="private-node"] [data-action="share"]').click();assert(await has('shareServer','private-node'));
 await page.locator('#navHome').click();await page.locator('#setAiMode').selectOption('dns_ai');
 let setting=await saved();assert(setting.enableAiDns&&setting.enableAiBypass&&!setting.enableAiXboxDns);
 await page.locator('#setAiMode').selectOption('vpn');assert.equal((await saved()).aiRoutingMode,'vpn');await page.locator('#navRouting').click();
 await page.locator('#setBlockRules').fill('ads.example.com');await page.locator('#setBlockRules').dispatchEvent('change');assert.equal((await saved()).customBlockRules,'ads.example.com');
 await page.screenshot({path:'tests/artifacts/android-routing.png'});
 await page.locator('#navSettings').click();await page.locator('#setFragmentPreset').selectOption('aggressive');
 setting=await saved();assert.equal(setting.tlsFragmentLength,'50-100');assert.equal(setting.tlsFragmentInterval,'20-40');assert.equal(setting.tlsFragmentPackets,'1-3');
 await page.locator('#setDnsProvider').selectOption('custom');await page.locator('#setCustomDoh').fill('https://dns.google/dns-query');await page.locator('#setCustomDoh').dispatchEvent('change');
 await page.locator('#setSubInterval').selectOption('manual');assert.equal((await saved()).subAutoUpdateInterval,'manual');
 await page.locator('#navHome').click();await page.locator('#btnTrafficHistory').click();
 const date=new Date().toLocaleDateString('en-CA');
 await host('trafficHistory',[{Date:date,Download:1048576,Upload:524288,ConnectedMs:60000}]);
 assert((await page.locator('#toolsContent').textContent()).includes('1.0 МБ'));
 await page.screenshot({path:'tests/artifacts/android-traffic.png'});
 await page.keyboard.press('Escape');assert(!await page.locator('#toolsDialog').isVisible());
 await page.locator('#btnSpeedTest').click();assert(await has('speedTest'));
 await host('operation',{operation:'speedTest',busy:true});await page.locator('#btnCancelSpeedTest').click();assert(await has('cancelSpeedTest'));
 for(const size of [{width:820,height:600},{width:1440,height:900}]){
  await page.setViewportSize(size);
  for(const tab of ['Servers','Routing','Settings','Home','Logs']){
   await page.locator('#nav'+tab).click();assert(!await page.locator('.tab-pane.active').evaluate(e=>e.scrollWidth>e.clientWidth+2),tab+' overflow');
  }
 }
 await page.setViewportSize({width:820,height:600});await page.locator('#navServers').click();await page.screenshot({path:'tests/artifacts/android-compact.png'});
 assert.deepEqual(errors,[]);
 console.log('PASS Android UI: subscriptions, source IDs, favorites, grouping, disabled nodes, DNS-AI/VPN, fragment presets, DoH, manual updates, traffic, speed cancellation, responsive tabs');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
