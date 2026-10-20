(function () {
  'use strict';
  const $=id=>document.getElementById(id);
  const send=payload=>window.chrome?.webview?.postMessage(payload);
  const escape=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const routing=$('setProxyRules').closest('.settings-section');
  $('routingContent').append(routing);
  const network=$('setEnableFragment').closest('.settings-section');
  network.insertAdjacentHTML('beforeend', `
    <div class="setting-item"><label for="setFragmentPreset">Фрагментация TLS</label><select id="setFragmentPreset" class="tactical-select"><option value="balanced">Сбалансированная</option><option value="gentle">Мягкая</option><option value="aggressive">Агрессивная</option><option value="custom">Своя</option></select></div>
    <div class="compact-fields"><label>Длина<input id="setFragmentLength" class="desktop-setting port-input" value="100-200"></label><label>Интервал, мс<input id="setFragmentInterval" class="desktop-setting port-input" value="10-20"></label><label>Пакеты<input id="setFragmentPackets" class="desktop-setting port-input" value="tlshello"></label></div>
    <div class="setting-item"><div class="item-text"><label>Mux.Cool</label><p>Для VLESS/VMess без Vision, XHTTP и gRPC. Автовыбор транспорта сохраняется.</p></div><label class="toggle-switch"><input id="setMux" class="desktop-setting" type="checkbox"><span class="slider"></span></label></div>
    <div class="setting-item"><label for="setMuxConcurrency">Параллельность Mux</label><input id="setMuxConcurrency" class="desktop-setting port-input" type="number" min="1" max="128" value="8"></div>`);
  $('setDnsProvider').insertAdjacentHTML('beforeend','<option value="dns_ai">DNS-AI</option><option value="custom">Собственный DoH</option>');
  $('setDnsProvider').closest('.setting-item').insertAdjacentHTML('afterend','<div class="setting-item"><label for="setCustomDoh">Адрес DoH</label><input id="setCustomDoh" class="desktop-setting tactical-input" type="url" value="https://dns.google/dns-query"></div>');
  $('setAutoUpdateSub').closest('.settings-section').insertAdjacentHTML('beforeend',`
    <div class="setting-item"><label for="setSubInterval">Обновлять подписки</label><select id="setSubInterval" class="desktop-setting tactical-select"><option value="6h">Каждые 6 часов</option><option value="12h">Каждые 12 часов</option><option value="24h">Каждые 24 часа</option><option value="48h">Каждые 48 часов</option><option value="manual">Вручную</option></select></div>
    <div class="setting-item"><div class="item-text"><label>Запускать свёрнутым</label></div><label class="toggle-switch"><input id="setStartMinimized" class="desktop-setting" type="checkbox"><span class="slider"></span></label></div>
    <div class="setting-item"><div class="item-text"><label>Win + Alt + V</label><p>Подключить или отключить VPN глобальной клавишей.</p></div><label class="toggle-switch"><input id="setHotkey" class="desktop-setting" type="checkbox" checked><span class="slider"></span></label></div>`);
  const schema={enableAiDns:['setAiDns','checked'],aiRoutingMode:['setAiMode','value'],routingMode:['setRoutingMode','value'],customDohUrl:['setCustomDoh','value'],customBlockRules:['setBlockRules','value'],enableAdBlock:['setAdBlock','checked'],enableMux:['setMux','checked'],muxConcurrency:['setMuxConcurrency','value'],tlsFragmentLength:['setFragmentLength','value'],tlsFragmentInterval:['setFragmentInterval','value'],tlsFragmentPackets:['setFragmentPackets','value'],shareLan:['setShareLan','checked'],startMinimized:['setStartMinimized','checked'],enableGlobalHotkey:['setHotkey','checked'],subAutoUpdateInterval:['setSubInterval','value']};
  window.SplateExtras={collectSettings:()=>Object.fromEntries(Object.entries(schema).map(([key,[id,prop]])=>[key,$(id)[prop]]))};

  const appearance=$('setTheme').closest('.settings-section');
  $('setTheme').hidden=true;
  appearance.insertAdjacentHTML('beforeend',`<div id="themeChoices" class="theme-choices" role="radiogroup" aria-label="Тема оформления"></div><button id="showAllThemes" class="btn-secondary">Показать все темы</button><div class="glass-control"><label for="setGlass">Прозрачность стекла <output id="glassValue">0%</output></label><input id="setGlass" class="desktop-setting" type="range" min="0" max="1" step="0.01" value="0"><button id="resetGlass" class="link-btn">Сбросить</button></div>`);
  const names={dark:'Тёмная',amoled:'AMOLED',light:'Светлая',system:'Системная',cyberpunk:'Киберпанк',oneplus:'OnePlus',aurora:'Северное сияние',sunset:'Закат',ocean:'Океан',sakura:'Сакура',pony:'Пони',winx:'Винкс · Блум'};
  appearance.insertAdjacentHTML('beforeend',`<div class="setting-item"><div class="item-text"><label for="setGlassBlur">Размытие фона</label><p>Мягкий блюр под карточками и панелями. Текст и кнопки остаются чёткими.</p></div><label class="toggle-switch"><input id="setGlassBlur" class="desktop-setting" type="checkbox" checked><span class="slider"></span></label></div>`);
  $('setGlassBlur').addEventListener('change',()=>SplateTheme.apply($('setTheme').value,$('setGlass').value,$('setGlassBlur').checked));
  schema.glassBlurEnabled=['setGlassBlur','checked'];
  let allThemes=false;
  function renderThemes(){
    $('themeChoices').replaceChildren();
    Object.entries(names).slice(0,allThemes?12:4).forEach(([key,label])=>{
      const tile=document.createElement('button');tile.className='theme-choice';tile.dataset.themeKey=key;tile.type='button';tile.role='radio';tile.setAttribute('aria-checked',String($('setTheme').value===key));
      const preview=document.createElement('canvas');preview.width=240;preview.height=120;preview.setAttribute('aria-hidden','true');
      const text=document.createElement('span');text.textContent=label;tile.append(preview,text);
      tile.onclick=()=>{$('setTheme').value=key;$('setTheme').dispatchEvent(new Event('change'));renderThemes();};
      $('themeChoices').append(tile);SplateTheme.preview(preview,key);
    });
    $('showAllThemes').textContent=allThemes?'Скрыть темы':'Показать все темы';
    $('showAllThemes').setAttribute('aria-expanded',String(allThemes));
  }
  $('showAllThemes').onclick=()=>{allThemes=!allThemes;renderThemes();};
  $('setGlass').addEventListener('input',()=>{SplateTheme.apply($('setTheme').value,$('setGlass').value);$('glassValue').textContent=Math.round($('setGlass').value*100)+'%';});
  $('resetGlass').onclick=()=>{$('setGlass').value=0;$('setGlass').dispatchEvent(new Event('input'));$('setGlass').dispatchEvent(new Event('change'));};
  addEventListener('splateTheme',()=>{document.querySelectorAll('.theme-choice').forEach(t=>t.setAttribute('aria-checked',String(t.dataset.themeKey===$('setTheme').value)));});
  addEventListener('themeArtworkLoaded',renderThemes);renderThemes();
  schema.glassTransparency=['setGlass','value'];schema.groupServers=['groupServers','checked'];
  document.querySelector('.home-right-col').insertAdjacentHTML('beforeend',`<div class="card settings-section balancer-card"><div class="settings-sec-header"><h3>Балансировщик</h3></div><p class="settings-help">Проверка связи не отключает VPN. Резерв выбирается после подтверждённого сбоя; оптимизация ждёт завершения теста скорости.</p><div class="setting-item"><label for="setBalancerMode">Режим</label><select id="setBalancerMode" class="tactical-select desktop-setting"><option value="balanced">Автоматический</option><option value="failover">Только при сбое</option><option value="initial_only">Только при подключении</option></select></div><div class="balancer-fields"></div></div>`);
  const balanceFields={balancerHealthSeconds:['setBalancerHealth','Проверять каждые, сек',2,30,2],balancerFailureConfirmations:['setBalancerFailures','Подтверждений сбоя',2,5,2],balancerOptimizationMinutes:['setBalancerOptimization','Поиск лучшего, мин',5,60,5],balancerCooldownSeconds:['setBalancerCooldown','Пауза между оптимизациями, сек',60,1800,60],balancerImprovementPercent:['setBalancerImprovement','Минимальный выигрыш, %',30,80,30]};
  schema.balancerMode=['setBalancerMode','value'];
  Object.entries(balanceFields).forEach(([key,[id,label,min,max,value]])=>{document.querySelector('.balancer-fields').insertAdjacentHTML('beforeend',`<label for="${id}">${label}<input id="${id}" class="port-input desktop-setting" type="number" min="${min}" max="${max}" value="${value}"></label>`);schema[key]=[id,'value'];});
  // Keep connection settings in one place; the home card contains the enable switch.
  $('setAutoFallback').closest('.settings-section').after(document.querySelector('.balancer-card'));
  $('setKillSwitch').closest('.setting-item').hidden=true;$('setDnsLeakProtection').closest('.setting-item').hidden=true;
  const toolbar=$('serverSort').closest('.server-list-meta');
  toolbar.insertAdjacentHTML('beforeend',`<label for="setPingMode">Метод проверки</label><select id="setPingMode" class="tactical-select desktop-setting"><option value="tcp">TCP</option><option value="http">Отклик HTTPS</option></select><button id="btnServersAutoSelect" class="btn-secondary">Автовыбор</button>`);
  schema.pingMode=['setPingMode','value'];$('btnServersAutoSelect').onclick=()=>send({action:'autoSelectServer'});
  function draggableCapsule(selector){
    const items=[...document.querySelectorAll(selector)];let origin=null,moved=false;
    items.forEach(item=>{
      item.addEventListener('pointerdown',e=>{if(e.button!==0)return;origin={id:e.pointerId,x:e.clientX,y:e.clientY,item};moved=false;item.setPointerCapture(e.pointerId);});
      item.addEventListener('pointermove',e=>{if(!origin||e.pointerId!==origin.id)return;if(Math.hypot(e.clientX-origin.x,e.clientY-origin.y)>12)moved=true;if(moved){const target=document.elementFromPoint(e.clientX,e.clientY)?.closest(selector);items.forEach(i=>i.classList.toggle('drag-target',i===target));}});
      item.addEventListener('pointerup',e=>{if(!origin)return;const source=origin;origin=null;items.forEach(i=>i.classList.remove('drag-target'));if(moved){item.releasePointerCapture(e.pointerId);const target=document.elementFromPoint(e.clientX,e.clientY)?.closest(selector);if(target)target.click();}});
      item.addEventListener('click',e=>{if(e.isTrusted&&moved){e.preventDefault();e.stopImmediatePropagation();moved=false;}},true);
      item.addEventListener('pointercancel',()=>{origin=null;items.forEach(i=>i.classList.remove('drag-target'));});
    });
  }
  draggableCapsule('.server-tab');draggableCapsule('.nav-item');
  $('setFragmentPreset').onchange=()=>{
    const preset={balanced:['100-200','10-20','tlshello'],gentle:['200-400','1-5','tlshello'],aggressive:['50-100','20-40','1-3']}[$('setFragmentPreset').value];
    if (preset) { $('setFragmentLength').value=preset[0]; $('setFragmentInterval').value=preset[1]; $('setFragmentPackets').value=preset[2]; $('setFragmentLength').dispatchEvent(new Event('change')); }
  };


  $('btnRestorePresets').onclick=()=>send({action:'restorePresets'});
  document.querySelector('.home-right-col').insertAdjacentHTML('beforeend',`<div class="card connection-tools"><h3>Инструменты соединения</h3><div class="artifact-actions"><button id="btnSpeedTest" class="btn-secondary">Тест скорости</button><button id="btnCancelSpeedTest" class="btn-secondary" hidden>Остановить</button><button id="btnTrafficHistory" class="btn-secondary">Статистика трафика</button><button id="btnRecentConnections" class="btn-secondary">Соединения</button></div><p id="speedTestResult" class="ai-check-result" role="status"></p></div>`);
  document.querySelector('.toolbar-actions').insertAdjacentHTML('beforeend','<button id="btnImportLinks" class="btn-secondary">Из файла</button><button id="btnManualServer" class="btn-secondary">Вручную</button><button id="btnExportLinks" class="btn-secondary">Экспорт ссылок</button>');
  $('btnScanQr').insertAdjacentHTML('afterend','<button id="btnScanQrScreen" class="btn-secondary">QR с экрана</button>');
  document.querySelector('.settings-grid').insertAdjacentHTML('beforeend','<div class="card settings-section"><div class="settings-sec-header"><h3>Геоданные Xray</h3></div><p class="settings-help">Списки доменов и IP для Smart RU, рекламы и маршрутов.</p><button id="btnUpdateGeo" class="btn-secondary">Обновить geosite / geoip</button></div>');
  document.body.insertAdjacentHTML('beforeend',`<dialog id="toolsDialog" class="app-dialog tools-dialog"><div class="modal-header"><h3 id="toolsTitle"></h3><button id="btnCloseTools" class="btn-secondary" aria-label="Закрыть">×</button></div><div id="toolsContent"></div></dialog>`);
  $('btnCloseTools').onclick=()=>$('toolsDialog').close();
  document.querySelector('.connection-tools .artifact-actions').insertAdjacentHTML('beforeend','<button id="btnShowWidget" class="btn-secondary">Мини-виджет</button>');
  const actions={btnImportLinks:'importLinks',btnManualServer:'manualServer',btnExportLinks:'exportServers',btnScanQrScreen:'scanQrScreen',btnUpdateGeo:'updateGeoAssets',btnSpeedTest:'speedTest',btnCancelSpeedTest:'cancelSpeedTest',btnShowWidget:'showWidget'};
  Object.entries(actions).forEach(([id,action])=>$(id).onclick=()=>send({action}));
  const toolbarActions=document.querySelector('.toolbar-actions');
  toolbarActions.insertAdjacentHTML('beforeend','<details class="node-more-tools"><summary class="btn-secondary">Ещё</summary><div class="node-more-content"></div></details>');
  const more=toolbarActions.querySelector('.node-more-content');
  ['btnImportLinks','btnManualServer','btnExportLinks'].forEach(id=>more.append($(id)));
  const pingLabel=document.querySelector('label[for="setPingMode"]');more.append(pingLabel,$('setPingMode'));
  $('btnServersAutoSelect').textContent='Выбрать быстрый';
  const openTool=title=>{$('toolsTitle').textContent=title;$('toolsContent').textContent='Загрузка…';if(!$('toolsDialog').open)$('toolsDialog').showModal();};
  $('speedTestResult').insertAdjacentHTML('afterend','<canvas hidden id="speedTestCanvas" width="700" height="160" aria-label="Скорость скачивания, Мбит/с"></canvas>');
  const speedSamples=[];
  function drawSpeedTest(){const canvas=$('speedTestCanvas'),c=canvas.getContext('2d'),w=canvas.width,h=canvas.height;c.clearRect(0,0,w,h);const color=getComputedStyle(document.documentElement).getPropertyValue('--accent-cyan').trim();c.strokeStyle=color;c.lineWidth=3;const maximum=Math.max(1,...speedSamples);c.beginPath();speedSamples.forEach((v,i)=>{const x=i/Math.max(1,speedSamples.length-1)*w,y=h-10-v/maximum*(h-24);if(i)c.lineTo(x,y);else c.moveTo(x,y);});c.stroke();}
  addEventListener('splateTheme',drawSpeedTest);
  $('btnTrafficHistory').onclick=()=>{openTool('Статистика трафика');send({action:'trafficHistory'});};
  $('btnRecentConnections').onclick=()=>{openTool('Соединения за сессию');send({action:'recentConnections'});};
  let history=[];
  function renderHistory(period='30') {
    const cutoff=new Date();cutoff.setDate(cutoff.getDate()-Number(period)+1);
    const date=[cutoff.getFullYear(),String(cutoff.getMonth()+1).padStart(2,'0'),String(cutoff.getDate()).padStart(2,'0')].join('-');
    const rows=history.filter(r=>r.Date>=date), bytes=n=>(Number(n)/1048576).toFixed(1)+' МБ';
    $('toolsContent').innerHTML=`<div class="subscription-tools"><select id="historyPeriod" class="tactical-select"><option value="7">7 дней</option><option value="30">30 дней</option><option value="400">Всё время</option></select><button id="clearHistory" class="btn-secondary">Очистить</button></div><p class="settings-help">Скачано ${bytes(rows.reduce((a,r)=>a+r.Download,0))} · отправлено ${bytes(rows.reduce((a,r)=>a+r.Upload,0))}. Счётчики трафика VPN и Smart DNS.</p><table class="data-table"><thead><tr><th>Дата</th><th>Скачано</th><th>Отправлено</th><th>Время</th></tr></thead><tbody>${rows.map(r=>`<tr><td>${escape(r.Date)}</td><td>${bytes(r.Download)}</td><td>${bytes(r.Upload)}</td><td>${Math.round(r.ConnectedMs/60000)} мин</td></tr>`).join('')||'<tr><td colspan="4">История пока пуста</td></tr>'}</tbody></table>`;
    $('historyPeriod').value=period;$('historyPeriod').onchange=e=>renderHistory(e.target.value);
    $('clearHistory').onclick=()=>send({action:'clearTrafficHistory'});
  }
  let subscriptions=[];
  function renderSubscriptions(rows) {
    subscriptions=rows||[];
    $('subscriptionCount').textContent=subscriptions.length;
    const source=$('subscriptionSource'), selected=source.value;
    source.innerHTML='<option value="Все">Все источники</option>';
    subscriptions.forEach(s=>{const o=document.createElement('option');o.value=s.id;o.textContent=s.name;source.append(o);});
    source.value=[...source.options].some(o=>o.value===selected)?selected:'Все';
    $('subscriptionsList').innerHTML=subscriptions.map(s=>`<article class="subscription-card ${s.enabled?'':'disabled'}" data-sub-id="${escape(s.id)}"><div class="subscription-heading"><strong>${escape(s.name)}</strong><span class="badge">${s.isPreset?'Встроенная':'Личная'}</span></div><p>${s.nodeCount} узлов · ${escape(s.usage)} · ${escape(s.expiry)}</p><progress max="100" value="${Number(s.usagePercentage)||0}" aria-label="Использовано трафика"></progress><p>${s.isUpdating?'Обновление…':escape(s.lastUpdated)}</p><div class="subscription-actions"><label class="follow-label"><input data-sub-action="enabled" type="checkbox" ${s.enabled?'checked':''}>Включена</label><label class="follow-label"><input data-sub-action="auto" type="checkbox" ${s.autoUpdate?'checked':''}>Авто</label><button data-sub-action="refresh" class="btn-secondary" ${s.isUpdating?'disabled':''}>↻</button><button data-sub-action="delete" class="btn-secondary">Удалить</button></div></article>`).join('')||'<p class="settings-help">Добавьте подписку или восстановите встроенные списки.</p>';
  }
  $('subscriptionsList').addEventListener('change',e=>{
    const id=e.target.closest('[data-sub-id]')?.dataset.subId, a=e.target.dataset.subAction;
    if(a==='enabled')send({action:'subscriptionOptions',id,enabled:e.target.checked});
    if(a==='auto')send({action:'subscriptionOptions',id,autoUpdate:e.target.checked});
  });
  $('subscriptionsList').addEventListener('click',e=>{
    const b=e.target.closest('button'), id=b?.closest('[data-sub-id]')?.dataset.subId;
    if(b?.dataset.subAction==='refresh')send({action:'refreshOneSubscription',id});
    if(b?.dataset.subAction==='delete')send({action:'deleteSubscription',id});
  });
  window.addEventListener('splateMessage',event=>{
    const {type,payload}=event.detail;
    if(type==='subscriptions')renderSubscriptions(payload);
    if(type==='trafficHistory'){history=payload||[];renderHistory();}
    if(type==='recentConnections')$('toolsContent').innerHTML='<p class="settings-help">Последние 200 принятых соединений Xray. Запись не означает, что соединение ещё открыто.</p><table class="data-table"><thead><tr><th>Время</th><th>Назначение</th><th>Маршрут</th></tr></thead><tbody>'+payload.slice().reverse().map(r=>`<tr><td>${escape(r.time)}</td><td>${escape(r.target)}</td><td>${escape(r.route)}</td></tr>`).join('')+'</tbody></table>';
    if(type==='speedTest'){$('speedTestCanvas').hidden=!Number.isFinite(payload.mbps)&&!speedSamples.length;$('speedTestResult').textContent=payload.message;if(Number.isFinite(payload.mbps)){speedSamples.push(payload.mbps);if(speedSamples.length>80)speedSamples.shift();drawSpeedTest();}}
    if(type==='shareServer'){
      openTool(payload.name);
      $('toolsContent').innerHTML=`<img class="share-qr" alt="QR-код сервера" src="${escape(payload.qr)}"><textarea class="rules-input" rows="4" readonly>${escape(payload.link)}</textarea><button id="copySharedLink" class="btn-secondary">Копировать ссылку</button>`;
      $('copySharedLink').onclick=()=>send({action:'copyServer',id:payload.id});
    }
    if(type==='extendedSettings'){
      SplateTheme.apply(payload.Theme||'dark',payload.GlassTransparency??0,payload.GlassBlurEnabled??true);
      $('setGlassBlur').checked=payload.GlassBlurEnabled??true;
      $('glassValue').textContent=Math.round((payload.GlassTransparency||0)*100)+'%';
      Object.entries(schema).forEach(([key,[id,prop]])=>{const hostKey=key[0].toUpperCase()+key.slice(1);if(payload[hostKey]!==undefined)$(id)[prop]=payload[hostKey];});
      const length=$('setFragmentLength').value, interval=$('setFragmentInterval').value;
      $('setFragmentPreset').value=length==='100-200'&&interval==='10-20'?'balanced':length==='200-400'&&interval==='1-5'?'gentle':length==='50-100'&&interval==='20-40'?'aggressive':'custom';
    }
    if(type==='lanEndpoints')$('lanEndpoints').textContent=payload.join('\n');
    if(type==='operation'&&payload.operation==='restorePresets')$('btnRestorePresets').disabled=payload.busy;
    if(type==='operation'&&payload.operation==='speedTest'){if(payload.busy){speedSamples.length=0;$('speedTestCanvas').hidden=true;drawSpeedTest();}$('btnSpeedTest').disabled=payload.busy;$('btnCancelSpeedTest').hidden=!payload.busy;}
    if(type==='operation'&&payload.operation==='updateGeoAssets')$('btnUpdateGeo').disabled=payload.busy;
  });
})();
