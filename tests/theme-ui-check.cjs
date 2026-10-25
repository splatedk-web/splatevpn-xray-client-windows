const {chromium}=require('C:/Users/Daniel/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const path=require('node:path');const assert=require('node:assert/strict');const fs=require('node:fs');
(async()=>{
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{
  const page=await browser.newPage({viewport:{width:1280,height:860},reducedMotion:'reduce'}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>{window.__sent=[];window.chrome={webview:{postMessage:m=>__sent.push(m),addEventListener:(_,fn)=>window.__host=fn}};});
  await page.goto('file:///'+path.resolve('wwwroot/index.html').replaceAll('\\','/'));
  const host=(type,payload)=>page.evaluate(({type,payload})=>__host({data:{type,payload,settingsRequestId:__sent.filter(m=>m.action==='saveSettings').at(-1)?.settingsRequestId}}),{type,payload});
  await host('settings',{theme:'dark',httpPort:10809,socksPort:10808,enableSniffing:true,enableAiBypass:true,dnsProvider:'cloudflare'});
  await page.locator('#navSettings').click();
  assert.equal(await page.locator('.theme-choice').count(),4,'first four themes');
  await page.locator('#showAllThemes').click();assert.equal(await page.locator('.theme-choice').count(),12);
  fs.mkdirSync('tests/artifacts/2.11.0',{recursive:true});
  await page.waitForFunction(()=>document.querySelector('#themeBackdrop')&&SplateTheme.palettes.winx);
  for(const key of Object.keys(await page.evaluate(()=>SplateTheme.palettes))){
   await page.locator('#navSettings').click();await page.locator(`[data-theme-key="${key}"]`).click();
   assert.equal(await page.locator('html').getAttribute('data-theme'),key);
   assert.equal(await page.locator(`[data-theme-key="${key}"]`).getAttribute('aria-checked'),'true');
   const check=await page.evaluate(key=>{
    const p=SplateTheme.palettes[key],css=getComputedStyle(document.documentElement);
    return Object.keys(p).filter(k=>k.includes('-')).every(k=>css.getPropertyValue('--'+k).trim().toLowerCase()===p[k].toLowerCase());
   },key);assert(check,key+' exact palette');
   assert.equal(await page.locator('html').getAttribute('data-light'),String(['light','pony','winx'].includes(key)));
   assert(await page.evaluate(()=>getComputedStyle(document.querySelector('.speed-num')).color===getComputedStyle(document.querySelector('.core-status-headline')).color),key+' readable speed numerals');
   await page.locator('#setGlass').evaluate(e=>e.value='0.30');await page.locator('#setGlass').dispatchEvent('input');await page.locator('#setGlass').dispatchEvent('change');
   await page.locator('#navHome').click();await page.locator('#mainViewport').evaluate(e=>e.scrollTop=0);
   await page.screenshot({path:`tests/artifacts/2.11.0/theme-${key}.png`});
  }
  await page.locator('#navSettings').click();await page.locator('[data-theme-key="pony"]').click();
  const opaque=await page.evaluate(()=>getComputedStyle(document.documentElement).getPropertyValue('--surface-card'));
  await page.locator('#setGlass').evaluate(e=>e.value='1');await page.locator('#setGlass').dispatchEvent('input');
  assert.notEqual(await page.evaluate(()=>getComputedStyle(document.documentElement).getPropertyValue('--surface-card')),opaque,'live glass drag');
  await page.locator('#setGlass').dispatchEvent('change');
  let saved=await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings);assert.equal(Number(saved.glassTransparency),1);
  await page.locator('#resetGlass').click();assert.equal(await page.locator('#setGlass').inputValue(),'0');
  await host('extendedSettings',{Theme:'ocean',GlassTransparency:.52,GroupServers:true,BalancerMode:'failover',BalancerHealthSeconds:10,BalancerFailureConfirmations:4,BalancerOptimizationMinutes:15,BalancerCooldownSeconds:120,BalancerImprovementPercent:50,ServerFilter:'all',ServerSort:'default'});
  assert.equal(await page.locator('#setGlass').inputValue(),'0.52');assert.equal(await page.locator('#setBalancerMode').inputValue(),'failover');
  await page.locator('#setBalancerMode').selectOption('initial_only');await page.locator('#setBalancerFailures').fill('5');await page.locator('#setBalancerFailures').dispatchEvent('change');
  saved=await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings);assert.equal(saved.balancerMode,'initial_only');assert.equal(Number(saved.balancerFailureConfirmations),5);
  for(const nav of ['Home','Servers','Routing','Settings','Logs']){
   await page.locator('#nav'+nav).click();assert(!/android|андроид|VpnService|Jetpack|Kotlin/i.test(await page.locator('.tab-pane.active').innerText()),'platform labels in '+nav);
  }
  await page.locator('#navServers').click();
  const from=await page.locator('[data-filter="all"]').boundingBox(),to=await page.locator('[data-filter="personal"]').boundingBox();
  await page.mouse.move(from.x+from.width/2,from.y+from.height/2);await page.mouse.down();await page.mouse.move(to.x+to.width/2,to.y+to.height/2,{steps:12});await page.mouse.up();
  assert(await page.locator('[data-filter="personal"]').evaluate(e=>e.classList.contains('active')),'draggable categories');
  await page.locator('.node-more-tools').evaluate(e=>e.open=true);
  await page.locator('#setPingMode').selectOption('http');assert.equal((await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings)).pingMode,'http');
  await page.locator('#navHome').click();
  await host('speedTest',{message:'Скачивание',mbps:12.3,bytes:1000000,seconds:1,httpsPing:null,done:false});
  await host('speedTest',{message:'20.1 Мбит/с · Отклик HTTPS: недоступен',mbps:20.1,bytes:2000000,seconds:2,httpsPing:null,done:true});
  assert((await page.locator('#speedTestResult').innerText()).includes('недоступен'));
  for(const size of [{width:820,height:600},{width:1440,height:900}]){
   await page.setViewportSize(size);
   for(const nav of ['Home','Servers','Routing','Settings','Logs']){
    await page.locator('#nav'+nav).click();assert(!await page.locator('.tab-pane.active').evaluate(e=>e.scrollWidth>e.clientWidth+2),nav+' horizontal overflow');
   }
  }
  assert.deepEqual(errors,[]);
  console.log('PASS 12 themes, exact palettes, live/persisted glass, balancer fields, folders, drag navigation, speed graph, Windows wording and responsive screens');
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
