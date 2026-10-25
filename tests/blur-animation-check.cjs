const {chromium}=require('C:/Users/Daniel/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const path=require('node:path'),assert=require('node:assert/strict'),fs=require('node:fs');
(async()=>{
 const root=path.resolve('wwwroot');
 const server=require('node:http').createServer((req,res)=>{
  const file=path.resolve(root,'.'+new URL(req.url,'http://localhost').pathname);
  if(!file.startsWith(root+path.sep)){res.writeHead(403).end();return;}
  fs.readFile(file,(error,data)=>{if(error){res.writeHead(404).end();return;}res.setHeader('Content-Type',({'.js':'text/javascript','.css':'text/css','.png':'image/png','.html':'text/html'})[path.extname(file)]||'application/octet-stream');res.end(data);});
 });
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
 const browser=await chromium.launch({headless:true,channel:'msedge'});
 try {
  const page=await browser.newPage({viewport:{width:1280,height:900},reducedMotion:'no-preference'}),errors=[];
  page.on('pageerror',e=>errors.push(e.message));
  await page.addInitScript(()=>{
   window.__sent=[];window.chrome={webview:{postMessage:m=>__sent.push(m),addEventListener:(_,fn)=>window.__host=fn}};
   let next=0;const callbacks=new Map();
   window.requestAnimationFrame=fn=>{callbacks.set(++next,fn);return next;};
   window.cancelAnimationFrame=id=>callbacks.delete(id);
   window.__frame=time=>{const queued=[...callbacks.values()];callbacks.clear();queued.forEach(fn=>fn(time));};
   window.__pendingFrames=()=>callbacks.size;
  });
  await page.goto('http://127.0.0.1:'+server.address().port+'/index.html');
  await page.waitForFunction(()=>document.querySelector('#themeBackdrop')&&SplateTheme);
  await page.waitForTimeout(400);
  const host=payload=>page.evaluate(payload=>__host({data:{type:'extendedSettings',payload,settingsRequestId:__sent.filter(m=>m.action==='saveSettings').at(-1)?.settingsRequestId}}),payload);
  await host({Theme:'sakura',GlassTransparency:.75,GlassBlurEnabled:true});
  await page.locator('#navSettings').click();
  assert(await page.locator('#setGlassBlur').isChecked());
  await page.locator('#setGlassBlur').evaluate(e=>e.scrollIntoView({block:'center'}));
  await page.locator('.toggle-switch:has(#setGlassBlur)').click();
  assert.equal(await page.evaluate(()=>getComputedStyle(document.documentElement).getPropertyValue('--glass-blur')),'none');
  assert.equal(await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings.glassBlurEnabled),false);
  await page.locator('.toggle-switch:has(#setGlassBlur)').click();
  assert.equal(await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings.glassBlurEnabled),true);
  await page.locator('#navHome').click();await page.locator('#mainViewport').evaluate(e=>e.scrollTop=0);
  await page.waitForTimeout(350);
  fs.mkdirSync('tests/artifacts/2.11.6',{recursive:true});
  // A sharp test pattern proves that the card samples the canvas behind the app,
  // rather than merely declaring a backdrop-filter inside an isolated tab.
  const paint=blur=>page.evaluate(blur=>{
   SplateTheme.apply('sakura',.75,blur);
   const canvas=document.querySelector('#themeBackdrop'),c=canvas.getContext('2d');
   c.setTransform(1,0,0,1,0,0);
   for(let x=0;x<canvas.width;x+=8){c.fillStyle=x%16===0?'#ffffff':'#000000';c.fillRect(x,0,8,canvas.height);}
  },blur);
  const ip=page.locator('#currentIpVal').locator('xpath=ancestor::div[contains(@class,"card")][1]');
  await paint(false);const sharp=await ip.screenshot({path:'tests/artifacts/2.11.6/blur-off.png'});
  await paint(true);const blurred=await ip.screenshot({path:'tests/artifacts/2.11.6/blur-on.png'});
  assert(!sharp.equals(blurred),'blur changes actual pixels under the IP card');
  let time=0;
  for(const theme of Object.keys(await page.evaluate(()=>SplateTheme.palettes))){
   const frames=await page.evaluate(({theme,time})=>{SplateTheme.apply(theme,.55,true);__frame(time+1000);const first=document.querySelector('#themeBackdrop').toDataURL();__frame(time+9000);return [first,document.querySelector('#themeBackdrop').toDataURL()];},{theme,time});
   assert(frames[0]!==frames[1],theme+' background moves');time+=10000;
   fs.writeFileSync('tests/artifacts/2.11.6/animation-'+theme+'.png',Buffer.from(frames[1].split(',')[1],'base64'));
  }
  await page.evaluate(time=>{SplateTheme.apply('winx',.6,true);__frame(time+12000);},time);
  await page.screenshot({path:'tests/artifacts/2.11.6/winx-magic.png'});
  await page.emulateMedia({reducedMotion:'reduce'});
  await page.waitForFunction(()=>__pendingFrames()===0,null,{polling:20});
  assert.equal(await page.evaluate(()=>__pendingFrames()),0,'reduced motion stops the loop');
  await page.setViewportSize({width:820,height:600});await page.locator('#navSettings').click();
  assert(!await page.locator('#tab-settings').evaluate(e=>e.scrollWidth>e.clientWidth+2));
  await host({Theme:'ocean',GlassTransparency:.45,GlassBlurEnabled:false});
  assert(!(await page.locator('#setGlassBlur').isChecked()));
  assert.equal(await page.evaluate(()=>getComputedStyle(document.querySelector('.card')).backdropFilter),'none');
  assert.deepEqual(errors,[]);
  console.log('PASS persisted blur IPC, real IP card blur pixels, all 11 backgrounds animate, reduced motion and responsive settings');
 } finally {await browser.close();await new Promise(resolve=>server.close(resolve));}
})().catch(e=>{console.error(e);process.exitCode=1;});
