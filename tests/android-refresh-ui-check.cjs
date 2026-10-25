const {chromium}=require('C:/Users/Daniel/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),crypto=require('node:crypto');
(async()=>{
 const root=path.resolve('wwwroot'),android=path.resolve('../SplateClient/app/src/main');
 const data=JSON.parse(fs.readFileSync('wwwroot/js/objects-data.json','utf8'));
 assert.equal(Object.keys(data).length,6);
 const hash=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
 for(const art of Object.values(data)){assert(art.layers.length>=2);const original='theme_'+art.file.slice(7,-4)+'_object.png';assert.equal(hash(path.join(root,'assets/themes',art.file)),hash(path.join(android,'res/drawable-nodpi',original)));}
 const source=fs.readFileSync('Services/SmartDnsProviders.cs','utf8');
 const providers=[...source.matchAll(/new\("([a-z_]+)", "([^"]+)", "([^"]+)",/g)].map(m=>({Key:m[1],Label:m[2],Host:m[3]}));assert.equal(providers.length,7);
 const androidProviders=fs.readFileSync(path.join(android,'java/com/danieldk/splatevpn/data/SmartDnsProviders.kt'),'utf8');
 providers.forEach(p=>assert(androidProviders.includes(`"${p.Key}", "${p.Label}", "${p.Host}"`),'provider matches Android'));
 const server=require('node:http').createServer((req,res)=>{const file=path.resolve(root,'.'+new URL(req.url,'http://localhost').pathname);if(!file.startsWith(root+path.sep)){res.writeHead(403).end();return;}fs.readFile(file,(error,bytes)=>{if(error){res.writeHead(404).end();return;}res.setHeader('Content-Type',({'.js':'text/javascript','.css':'text/css','.png':'image/png','.html':'text/html'})[path.extname(file)]||'application/octet-stream');res.end(bytes);});});
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const browser=await chromium.launch({headless:true,channel:'msedge'});
 try{
  const page=await browser.newPage({viewport:{width:1280,height:900},reducedMotion:'no-preference'}),errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>{window.__sent=[];window.chrome={webview:{postMessage:m=>__sent.push(m),addEventListener:(_,fn)=>window.__host=fn}};let next=0;const frames=new Map();window.requestAnimationFrame=fn=>{frames.set(++next,fn);return next;};window.cancelAnimationFrame=id=>frames.delete(id);window.__frame=t=>{const callbacks=[...frames.values()];frames.clear();callbacks.forEach(fn=>fn(t));};});
  await page.goto('http://127.0.0.1:'+server.address().port+'/index.html');
  await page.waitForFunction(()=>window.SplateObjects&&Object.values(SplateObjects.images).every(i=>i.complete&&i.naturalWidth));
  const host=(type,payload)=>page.evaluate(({type,payload})=>__host({data:{type,payload}}),{type,payload});
  await host('smartDnsProviders',providers);
  await host('extendedSettings',{Theme:'sakura',GlassTransparency:.65,GlassBlurEnabled:true,AiDnsProvider:'geohide_eu',AiTlsFragment:false,EnableAiDns:true,EnableAiBypass:true,AiRoutingMode:'dns_ai',AdBlockAllowlist:'example.com',EnableAdBlock:true});
  await host('settings',{theme:'sakura',httpPort:10809,socksPort:10808,enableAiBypass:true,dnsProvider:'cloudflare'});
  await page.locator('#navRouting').click();
  assert.equal(await page.locator('#setAiDnsProvider option').count(),7);assert.equal(await page.locator('#setAiDnsProvider').inputValue(),'geohide_eu');assert.equal(await page.locator('#setDnsProvider').inputValue(),'cloudflare');
  await page.locator('#setAiDnsProvider').selectOption('malw');await page.locator('#setAiTlsFragment').evaluate(e=>{e.checked=true;e.dispatchEvent(new Event('change'));});
  const saved=await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings);assert.equal(saved.aiDnsProvider,'malw');assert(saved.aiTlsFragment);assert.equal(saved.dnsProvider,'cloudflare');assert.equal(saved.adBlockAllowlist,'example.com');
  await page.locator('#btnSelectAiDns').click();assert.equal((await page.evaluate(()=>__sent.at(-1))).action,'selectAiDns');
  await host('operation',{operation:'selectAiDns',busy:true});assert(await page.locator('#btnSelectAiDns').isDisabled());await page.locator('#btnCancelAiDns').click();assert.equal((await page.evaluate(()=>__sent.at(-1))).action,'cancelAiDns');
  const result={Choice:{ProviderKey:'malw',TlsFragment:false},Targets:[{Service:'ChatGPT',Outcome:0,ElapsedMs:100},{Service:'Gemini',Outcome:1,ElapsedMs:120},{Service:'Claude',Outcome:2,ElapsedMs:200}]};
  await host('aiDnsSelection',{Phase:'verifying',Completed:14,Total:14,Current:result.Choice,Results:[result]});assert((await page.locator('#aiDnsSelectionResults').innerText()).includes('региональный запрет'));
  await host('aiDnsSelection',{Phase:'cancelled',Completed:14,Total:14,Results:[result]});assert((await page.locator('#aiDnsSelectionStatus').innerText()).includes('не изменены'));
  await host('operation',{operation:'selectAiDns',busy:false});assert(await page.locator('#btnCancelAiDns').isHidden());
  await page.locator('#navRouting').click();await page.locator('#btnUpdateAdFilters').click();assert.equal((await page.evaluate(()=>__sent.at(-1))).action,'updateAdFilters');
  await host('adFilters',{Count:184692,Status:'База готова.'});assert((await page.locator('#adFilterStatus').innerText()).includes('184'));
  fs.mkdirSync('tests/artifacts/2.12.1',{recursive:true});let time=1000;
  await page.locator('#navHome').click();
  for(const theme of ['cyberpunk','sakura','ocean','sunset','aurora','oneplus']){
   await host('extendedSettings',{Theme:theme,GlassTransparency:.6,GlassBlurEnabled:true});
   await page.evaluate(t=>__frame(t),time);const first=await page.evaluate(()=>document.querySelector('#themeBackdrop').toDataURL());
   await page.evaluate(t=>__frame(t),time+7000);const second=await page.evaluate(()=>document.querySelector('#themeBackdrop').toDataURL());assert(first!==second,theme+' layers move');time+=10000;
   await page.screenshot({path:`tests/artifacts/2.12.1/${theme}.png`});
  }
  await page.locator('#navSettings').click();await page.locator('#showAllThemes').click();
  const preview=page.locator('[data-theme-key="ocean"] canvas');const before=await preview.evaluate(e=>e.toDataURL());await page.evaluate(t=>__frame(t),time+15000);assert.equal(await preview.evaluate(e=>e.toDataURL()),before,'previews static');
  await page.setViewportSize({width:820,height:600});for(const nav of ['Settings','Routing','Home']){await page.locator('#nav'+nav).click();assert(!await page.locator('.tab-pane.active').evaluate(e=>e.scrollWidth>e.clientWidth+2),nav+' no overflow');}
  assert.deepEqual(errors,[]);console.log('PASS Android atlas hashes/layers, provider parity, separate DNS settings, selector/cancel/results, ad filters, animation, static previews and responsive UI');
 }finally{await browser.close();await new Promise(resolve=>server.close(resolve));}
})().catch(e=>{console.error(e);process.exitCode=1;});
