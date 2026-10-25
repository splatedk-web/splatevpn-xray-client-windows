const { chromium } = require('C:/Users/Daniel/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const path = require('node:path');
const fs = require('node:fs');
(async () => {
  const browser = await chromium.launch({headless:true,channel:"msedge"});
  const page = await browser.newPage({viewport:{width:1120,height:780}, reducedMotion:'reduce'});
  const errors=[]; page.on('pageerror', e => errors.push(e.message));
  page.on('dialog', async dialog => { errors.push('Unexpected native dialog: '+dialog.type()); await dialog.dismiss(); });
  await page.addInitScript(() => {
    window.chrome = window.chrome || {};
    window.__sent=[];
    window.chrome.webview={postMessage:msg=>window.__sent.push(msg), addEventListener:(name,fn)=>{window.__host=fn;}};
  });
  await page.goto('file:///' + path.resolve('wwwroot/index.html').replaceAll('\\','/'));
  const settings={theme:'dark',enableTunMode:false,routingMode:'smart_ru',xhttpModeOverride:'auto',bufferSize:4096,enableTlsFragment:false,dnsProvider:'cloudflare',isFakeDns:false,blockQuic:true,blockIpv6:true,enableSniffing:true,enableAiBypass:true,httpPort:10809,socksPort:10808,minimizeToTray:true,isAdministrator:false};
  await page.evaluate(settings => {
    __host({data:{type:'settings',payload:settings}});
    const server={id:'test-node',displayName:'Тестовый сервер',address:'vpn.example.com',port:443,protocol:'vless',transport:'xhttp',flag:'🇩🇪',ping:42,subscriptionGroup:'Проверка'};
    __host({data:{type:'state',payload:{isConnected:false,isConnecting:false,statusText:'ОТКЛЮЧЕНО',selectedServer:server}}});
    __host({data:{type:'servers',payload:[server,{...server,id:'other-node',displayName:'Другой сервер'}]}});
  }, settings);
  fs.mkdirSync('tests/artifacts',{recursive:true});
  await page.screenshot({path:'tests/artifacts/home-dark.png'});
  await page.locator('#navServers').click();
  await page.locator('.server-card[data-id="test-node"] summary').click();
  await page.locator('.server-card[data-id="test-node"] [data-action="edit"]').click();
  if (!await page.evaluate(()=>__sent.some(m=>m.action==='editServer'&&m.id==='test-node'))) throw new Error('Edit action missing');
  for (const connection of [{isConnected:false,isConnecting:false},{isConnected:false,isConnecting:true},{isConnected:true,isConnecting:false}]) {
    await page.evaluate(connection => {
      __host({data:{type:'state',payload:{...connection,selectedServer:{id:'test-node',displayName:'Тестовый сервер'}}}});
      __sent.length=0;
    }, connection);
    await page.locator('.server-card[data-id="other-node"] .card-name').click();
    if (!await page.evaluate(()=>__sent.some(m=>m.action==='selectServer'&&m.id==='other-node'))) throw new Error('Server selection blocked: '+JSON.stringify(connection));
    await page.evaluate(connection=>__host({data:{type:'state',payload:{...connection,selectedServer:{id:'other-node',displayName:'Другой сервер'}}}}),connection);
    if (!await page.locator('.server-card[data-id="other-node"]').evaluate(el=>el.classList.contains('selected'))) throw new Error('New selection marker absent');
    if (await page.locator('.server-card[data-id="test-node"]').evaluate(el=>el.classList.contains('selected'))) throw new Error('Old selection marker remains');
  }
  await page.locator('#navHome').click();
  await page.locator('#selectedServerCard').click();
  if (!await page.locator('#tab-servers').evaluate(el=>el.classList.contains('active'))) throw new Error('Summary card does not open server list');
  await page.locator('#navServers').click();
  await page.evaluate(()=>__sent.length=0);
  await page.locator('.server-card[data-id="other-node"] details').evaluate(e=>e.open=true);
  await page.locator('.server-card[data-id="other-node"] [data-action="delete"]').click();
  await page.locator('#confirmationDialog').waitFor({state:'visible'});
  if (await page.evaluate(()=>__sent.some(m=>m.action==='deleteServer'))) throw new Error('Deletion sent before confirmation');
  await page.keyboard.press('Escape');
  if (await page.evaluate(()=>__sent.some(m=>m.action==='deleteServer'))) throw new Error('Cancelled deletion sent');
  await page.locator('.server-card[data-id="other-node"] details').evaluate(e=>e.open=true);
  await page.locator('.server-card[data-id="other-node"] [data-action="delete"]').click();
  await page.locator('#confirmationAccept').click();
  if (!await page.evaluate(()=>__sent.some(m=>m.action==='deleteServer'&&m.id==='other-node'))) throw new Error('Confirmed deletion missing');
  await page.evaluate(()=>__host({data:{type:'confirmation',payload:{id:'confirm-test',title:'Подписка найдена',message:'Обновить подписку?'}}}));
  await page.locator('#confirmationCancel').click();
  await page.waitForFunction(()=>__sent.some(m=>m.action==='confirmationResult'&&m.id==='confirm-test'&&m.accepted===false));
  await page.locator('#navSettings').click();
  await page.locator('#setTheme').selectOption('light',{force:true});
  if (await page.locator('html').getAttribute('data-theme') !== 'light') throw new Error('Theme not applied');
  await page.evaluate(settings=>__host({data:{type:'settings',payload:{...settings,theme:'light'}}}),settings);
  await page.screenshot({path:'tests/artifacts/settings-light.png'});
  await page.locator('#navHome').click();
  await page.locator('#btnAiPreset').click();
  const preset=await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings);
  if (!preset.enableTunMode || !preset.enableSniffing || !preset.enableAiBypass || preset.dnsProvider!=='cloudflare') throw new Error('Codex preset incomplete');
  await page.locator('#btnCheckAi').click();
  if (!await page.locator('#btnCheckAi').isDisabled()) throw new Error('Diagnostic button not disabled');
  if (!await page.evaluate(()=>__sent.some(m=>m.action==='checkAiConnectivity'))) throw new Error('Diagnostics not sent');
  await page.evaluate(()=>__host({data:{type:'aiConnectivity',payload:{message:'ChatGPT: HTTP 403; тестовый отказ'}}}));
  if (await page.locator('#btnCheckAi').isDisabled()) throw new Error('Diagnostic button stuck');
  if (!await page.locator('#aiCheckResult').textContent().then(t=>t.includes('403'))) throw new Error('Diagnostic result absent');
  await page.evaluate(()=>__host({data:{type:'notification',payload:{title:'Импорт завершён',message:'Сервер добавлен. <b>Текст</b>',kind:'info'}}}));
  if (await page.locator('.app-notification b').count()) throw new Error('Notification HTML executed');
  await page.screenshot({path:'tests/artifacts/notification-light.png'});
  await page.evaluate(()=>__host({data:{type:'confirmation',payload:{id:'screenshot-dialog',title:'Удаление подписки',message:'Удалить подписку «Моя подписка» и её серверы?'}}}));
  await page.locator('#confirmationDialog').waitFor({state:'visible'});
  await page.screenshot({path:'tests/artifacts/confirmation-light.png'});
  await page.locator('#confirmationCancel').click();
  await page.locator('#navHome').click();
  await page.locator('#setAiMode').selectOption('dns_ai');
  const aiDnsSave=await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1).settings);
  if (aiDnsSave.enableAiXboxDns || !aiDnsSave.enableAiDns || !aiDnsSave.enableAiBypass || aiDnsSave.dnsProvider!=='cloudflare') throw new Error('Unified SmartDNS mode inconsistent');
  await page.locator('#setAiMode').selectOption('vpn');
  await page.locator('#navRouting').click();
  await page.locator('#setProxyRules').fill('example.com\n10.1.0.0/16');
  await page.locator('#setProxyRules').dispatchEvent('change');
  const save=await page.evaluate(()=>__sent.filter(m=>m.action==='saveSettings').at(-1));
  if(save.settings.customProxyRules!=='example.com\n10.1.0.0/16')throw new Error('Routing rules not sent');
  await page.locator('#navSettings').click();
  await page.locator('#setHttpPort').fill('10808');
  await page.locator('#setHttpPort').dispatchEvent('change');
  if(!await page.locator('#autoSaveText').textContent().then(t=>t.includes('разные порты')))throw new Error('Port validation absent');
  await page.locator('#setHttpPort').fill('10809');
  await page.locator('#setHttpPort').dispatchEvent('change');
  for (const theme of ['dark','amoled','light','system','cyberpunk','oneplus','aurora','sunset']) {
    await page.locator('#setTheme').selectOption(theme,{force:true});
    const bg=await page.evaluate(()=>getComputedStyle(document.documentElement).getPropertyValue('--bg-dark'));
    if (!bg.trim()) throw new Error('Palette empty: '+theme);
  }
  await page.setViewportSize({width:900,height:640});
  await page.screenshot({path:'tests/artifacts/settings-compact.png'});
  const overflow = await page.locator('#tab-settings .settings-grid').evaluate(el => el.scrollWidth > el.clientWidth + 1);
  if (overflow) throw new Error('Compact settings overflow horizontally');
  if(!await page.locator('#setKillSwitch').isDisabled())throw new Error('Broken kill switch exposed');
  await page.locator('#navHome').click();
  await page.evaluate(()=>__host({data:{type:'state',payload:{isConnecting:true,isConnected:false}}}));
  if(!await page.locator('#btnToggleConnectionText').textContent().then(t=>t.includes('Отменить')))throw new Error('Cancellation missing');

  await page.setViewportSize({width:1120,height:780});
  await page.locator('#navServers').click();
  await page.evaluate(() => {
    __host({data:{type:'servers',payload:[
      {id:'a',displayName:'Alpha',address:'a.example',port:443,protocol:'vless',ping:120,subscriptionId:'sub1',subscriptionGroup:'Рабочая подписка'},
      {id:'b',displayName:'Beta',address:'b.example',port:443,protocol:'trojan',ping:20,subscriptionGroup:'Личные'},
      {id:'c',displayName:'Gamma',address:'c.example',port:443,protocol:'vless',ping:0,subscriptionGroup:'Личные'}]}});
  });
  await page.locator('#serverSearchInput').fill('рабочая');
  if (await page.locator('.server-card').count() !== 1) throw new Error('Subscription search failed');
  await page.locator('#btnSearchClear').click();
  await page.locator('[data-filter="personal"]').click();
  if (await page.locator('.server-card').count() !== 2) throw new Error('Personal filter disagrees with count');
  await page.locator('[data-filter="all"]').click();
  await page.locator('#groupServers').uncheck();
  await page.locator('#serverSort').selectOption('ping');
  if ((await page.locator('.server-card').first().getAttribute('data-id')) !== 'b') throw new Error('Ping sorting failed');
  if ((await page.locator('.server-card').last().getAttribute('data-id')) !== 'c') throw new Error('Unreachable server sorted first');
  await page.locator('.server-card[data-id="b"]').focus();
  await page.keyboard.press('Enter');
  if (!await page.evaluate(()=>__sent.some(m=>m.action==='selectServer' && m.id==='b'))) throw new Error('Keyboard selection failed');
  await page.locator('#serverSearchInput').fill('no-such-server');
  if (!await page.locator('.empty-desc').textContent().then(t=>t.includes('запрос'))) throw new Error('Wrong search empty state');
  await page.locator('#btnAddServerDialog').click();
  await page.locator('#addServerInput').fill('https://example.com/sub');
  await page.locator('#addSubscriptionName').fill('Old name');
  await page.keyboard.press('Escape');
  if (await page.locator('#addServerModal').isVisible()) throw new Error('Escape failed to close add dialog');
  await page.locator('#btnAddServerDialog').click();
  if (await page.locator('#addSubscriptionName').inputValue()) throw new Error('Stale subscription name remains');
  if (await page.locator('#subscriptionNameField').isVisible()) throw new Error('Stale subscription name field remains');
  await page.locator('#addServerInput').fill('vless://test');
  await page.keyboard.press('Control+Enter');
  if (!await page.evaluate(()=>__sent.some(m=>m.action==='addServer'&&m.text==='vless://test'))) throw new Error('Keyboard import failed');
  await page.evaluate(()=>__host({data:{type:'operation',payload:{operation:'autoSelect',busy:true}}}));
  await page.locator('#navHome').click();
  if (!await page.locator('#btnAutoSelect').isDisabled()) throw new Error('Busy action can be repeated');
  await page.evaluate(()=>__host({data:{type:'operation',payload:{operation:'autoSelect',busy:false}}}));
  if (await page.locator('#btnAutoSelect').isDisabled()) throw new Error('Completed action stays disabled');
  await page.evaluate(()=>__host({data:{type:'state',payload:{isConnected:true,activeTunMode:true,activeAiDns:true,activeAiMode:'dns_ai',activeAiDnsLabel:'GeoHide EU',selectedServer:{id:'b',displayName:'Beta'}}}}));
  if (!await page.locator('#homeAiRoute').textContent().then(t=>t.includes('GeoHide EU'))) throw new Error('Actual AI route missing');
  await page.screenshot({path:'tests/artifacts/home-refined.png'});
  await page.locator('#navLogs').click();
  await page.evaluate(()=>{ for(let i=0;i<200;i++) __host({data:{type:'log',payload:'[INFO] Line '+i}}); __host({data:{type:'log',payload:'[CONNECT] Ошибка: test failure'}}); });
  await page.locator('#logLevel').selectOption('errors');
  if(await page.locator('.log-entry:visible').count()!==1)throw new Error('Russian error filter failed');
  await page.locator('#logLevel').selectOption('all');
  await page.locator('#logSearch').fill('Line 199');
  if(await page.locator('.log-entry:visible').count()!==1)throw new Error('Log search failed');
  await page.locator('#logSearch').fill('');
  await page.locator('#logFollow').uncheck();
  await page.locator('#logsTerminal').evaluate(el=>el.scrollTop=0);
  await page.evaluate(()=>__host({data:{type:'log',payload:'[INFO] New event'}}));
  if(await page.locator('#logsTerminal').evaluate(el=>el.scrollTop)>0)throw new Error('New log stole scroll position');
  await page.screenshot({path:'tests/artifacts/logs-refined.png'});
  await page.locator('#navSettings').click();
  await page.locator('#setBlockQuic').focus();
  const before=await page.locator('#setBlockQuic').isChecked();
  await page.keyboard.press('Space');
  if(await page.locator('#setBlockQuic').isChecked()===before)throw new Error('Keyboard toggle failed');
  for(const size of [{width:820,height:600},{width:1440,height:900}]) {
    await page.setViewportSize(size);
    for(const tab of ['Home','Servers','Routing','Settings','Logs']) {
      await page.locator('#nav'+tab).click();
      if(await page.locator('.tab-pane.active').evaluate(el=>el.scrollWidth>el.clientWidth+2))throw new Error('Horizontal overflow '+tab+' '+size.width);
    }
  }
  if(errors.length)throw new Error(errors.join('\n'));
  console.log('PASS browser UI: themes, IPC actions, routing, ports, cancellation, compact layout; no JS errors');
  await browser.close();
})().catch(e=>{console.error(e);process.exit(1);});
