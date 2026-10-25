const {chromium}=require('C:/Users/Daniel/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const path=require('node:path'),assert=require('node:assert/strict'),fs=require('node:fs');
(async()=>{
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{
  const page=await browser.newPage({viewport:{width:1120,height:780},reducedMotion:'reduce'}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>{window.__sent=[];window.chrome={webview:{postMessage:m=>__sent.push(m),addEventListener:(_,fn)=>window.__host=fn}};});
  await page.goto('file:///'+path.resolve('wwwroot/index.html').replaceAll('\\','/'));
  const host=(type,payload)=>page.evaluate(({type,payload})=>__host({data:{type,payload}}),{type,payload});
  const nodes=Array.from({length:15},(_,i)=>({id:'node-'+i,displayName:i===0?'daniel · Estonia':`Сервер ${i}`,address:'node.example.com',port:443,protocol:'vless',transport:'xhttp reality',flag:'🇪🇪',ping:70+i,enabled:true,subscriptionId:i<8?'preset_white_cidr':'user',subscriptionGroup:i<8?'Встроенные · CIDR':'Моя подписка'}));
  await host('subscriptions',[{id:'preset_white_cidr',name:'Встроенные · CIDR',enabled:true,isPreset:true,nodeCount:8},{id:'user',name:'Моя подписка',enabled:true,nodeCount:7}]);
  await host('servers',nodes);
  await host('extendedSettings',{Theme:'sunset',EnableAiBypass:true,EnableAiDns:true,AiRoutingMode:'dns_ai',AiDnsProvider:'geohide_eu',GroupServers:true});
  await host('settings',{theme:'sunset',httpPort:10809,socksPort:10808,enableAiBypass:true,enableAiXboxDns:false,enableTunMode:true,dnsProvider:'cloudflare'});
  await host('state',{isConnected:true,activeTunMode:true,activeAiDns:true,activeAiMode:'dns_ai',activeAiDnsLabel:'GeoHide EU',statusText:'ПОДКЛЮЧЕНО',statusDetail:'TUN активен · Все приложения',currentIp:'203.0.113.25',currentLocation:'Tallinn, Estonia',selectedServer:nodes[0]});
  assert(await page.locator('#setAiMode').evaluate(e=>!!e.closest('.home-left-col')));
  assert(await page.locator('#speedTestCanvas').isHidden());assert((await page.locator('.connection-tools').boundingBox()).height<180,'no empty speed graph area');
  fs.mkdirSync('tests/artifacts/2.12.2',{recursive:true});
  await page.screenshot({path:'tests/artifacts/2.12.2/home.png',fullPage:true});
  await page.locator('#navServers').click();assert(!await page.locator('#subscriptionsPanel').evaluate(e=>e.open));assert(await page.locator('#subscriptionSource').isVisible());
  assert((await page.locator('#nodeSelectionContext').innerText()).includes('Подключён: daniel'));
  const second=page.locator('[data-id="node-1"]');
  await page.evaluate(()=>__sent.length=0);await second.locator('summary').click();assert.equal(await page.evaluate(()=>__sent.filter(m=>m.action==='selectServer').length),0);
  await second.locator('[data-action="edit"]').click();assert.equal((await page.evaluate(()=>__sent.at(-1))).action,'editServer');
  await second.locator('summary').click();await second.locator('[data-action="select"]').click();assert.equal((await page.evaluate(()=>__sent.at(-1))).id,'node-1');
  await host('state',{isConnected:true,statusText:'ПОДКЛЮЧЕНО',selectedServer:nodes[1]});assert.equal(await second.locator('[data-action="select"]').innerText(),'Подключён');assert.equal(await page.locator('[data-id="node-0"] [data-action="select"]').innerText(),'Выбрать');
  await page.locator('#serverSearchInput').fill('ничего-не-найдено');assert.equal(await page.locator('.server-card').count(),0);await page.locator('#btnResetNodeFilters').click();assert.equal(await page.locator('.server-card').count(),15);
  await page.locator('#subscriptionSource').selectOption('user');assert.equal(await page.locator('.server-card').count(),7);await page.locator('#btnRevealSelected').click();assert.equal(await page.locator('#subscriptionSource').inputValue(),'Все');assert(await page.locator('[data-id="node-1"]').isVisible());
  await page.locator('#mainViewport').evaluate(e=>e.scrollTop=0);await page.screenshot({path:'tests/artifacts/2.12.2/nodes.png'});
  await page.setViewportSize({width:820,height:600});assert(!await page.locator('#tab-servers').evaluate(e=>e.scrollWidth>e.clientWidth+2),'nodes fit narrow window');
  await page.locator('#navHome').click();await host('speedTest',{message:'Измерение',mbps:10,done:false});assert(await page.locator('#speedTestCanvas').isVisible());
  assert.deepEqual(errors,[]);console.log('PASS balanced home layout, empty graph hidden, separate subscription management, explicit node selection, safe action menu, current marker, reset/reveal and responsive nodes');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
