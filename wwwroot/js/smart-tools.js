(function(){
 'use strict';
 const $=id=>document.getElementById(id),send=action=>window.chrome?.webview?.postMessage({action});
 const ai=$('setAiMode').closest('.settings-section');
 ai.insertAdjacentHTML('beforeend',`<div class="smart-dns-tools">
  <div class="setting-item"><label for="setAiDnsProvider">Провайдер SmartDNS</label><select id="setAiDnsProvider" class="tactical-select desktop-setting"><option value="dns_ai">DNS-AI</option></select></div>
  <div class="setting-item"><div class="item-text"><label for="setAiTlsFragment">TLS-фрагментация SmartDNS</label><p>Отдельно от фрагментации VPN. По умолчанию выключена.</p></div><label class="toggle-switch"><input id="setAiTlsFragment" class="desktop-setting" type="checkbox"><span class="slider"></span></label></div>
  <p class="settings-help">SmartDNS для нейросетей выбирается отдельно от общего DNS. Сначала проверяется ответ DNS, затем публичные страницы ChatGPT, Gemini и Claude; вход в аккаунт не выполняется. Победитель проходит дополнительную проверку Gemini с JavaScript; CAPTCHA и региональный запрет не считаются успехом.</p>
  <div class="artifact-actions"><button id="btnSelectAiDns" class="btn-secondary">Подобрать и применить</button><button id="btnCancelAiDns" class="btn-secondary" hidden>Остановить</button></div>
  <p id="aiDnsSelectionStatus" class="settings-help" role="status">Подбор для Gemini: 7 провайдеров × 2 режима TLS. VPN продолжает работать во время проверки.</p>
  <div class="ai-dns-results" id="aiDnsSelectionResults"></div>
 </div>`);
 const quick=document.querySelector('.ai-setup-card');
 quick.querySelector('h3').textContent='Нейросети';
 document.querySelector('label[for="setAiMode"]').textContent='Маршрут';
 quick.querySelector('.settings-sec-header').after($('setAiMode').closest('.setting-item'),$('aiModeHelp'));
 document.querySelector('.home-left-col').insertBefore(quick,document.querySelector('.winx-companion'));
 quick.querySelector('.ai-setup-actions').insertAdjacentHTML('beforeend','<button id="btnQuickAiDns" class="btn-secondary" type="button">Подобрать SmartDNS для Gemini</button>');
 $('btnQuickAiDns').onclick=()=>{$('navRouting').click();$('btnSelectAiDns').scrollIntoView({block:'center'});$('btnSelectAiDns').click();};
 const ad=$('setAdBlock').closest('.settings-section');
 ad.insertAdjacentHTML('beforeend',`<div class="ad-filter-tools"><p id="adFilterStatus" class="settings-help" role="status">Встроенная база AdGuard DNS доступна без скачивания.</p>
  <button id="btnUpdateAdFilters" class="btn-secondary">Обновить фильтры</button>
  <p class="settings-help">Обновляется раз в сутки, пока клиент запущен и блокировщик включён. Фоновая загрузка применяется при следующем подключении; ручное обновление переподключает активный VPN.</p>
  <label for="setAdBlockAllowlist">Исключения из блокировки рекламы</label><textarea id="setAdBlockAllowlist" class="rules-input desktop-setting" rows="4" placeholder="example.com&#10;разрешённый-домен.рф"></textarea>
  <p class="settings-help">До 100 доменов, по одному в строке. Исключение включает поддомены. Пользовательские правила блокировки сохраняют приоритет. Источник: AdGuard DNS filter · GPL-3.0; лицензия входит в сборку.</p>
 </div>`);
 let dnsBusy=false; const collect=window.SplateExtras.collectSettings;
 function syncAiMode(){
  const global=$('setRoutingMode').value==='global';
  if(global&&!['vpn','dns_vpn'].includes($('setAiMode').value))$('setAiMode').value='vpn';
  $('setAiMode').disabled=false; for(const option of $('setAiMode').options)option.disabled=global&&!['vpn','dns_vpn'].includes(option.value);
  $('setDirectRules').disabled=global;
  const mode=$('setAiMode').value;
  $('setAiDns').checked=['dns_ai','dns_vpn'].includes(mode);$('setAiProxy').checked=mode!=='off';$('setAiXboxDns').checked=false;
  document.querySelector('.smart-dns-tools').hidden=!['dns_ai','dns_vpn'].includes(mode);$('btnQuickAiDns').hidden=mode!=='dns_ai'; $('setAiTlsFragment').closest('.setting-item').hidden=mode!=='dns_ai'; for(const id of ['btnSelectAiDns','btnCancelAiDns','aiDnsSelectionStatus','aiDnsSelectionResults'])$(id).hidden=mode!=='dns_ai'||(id==='btnCancelAiDns'&&!dnsBusy); if($('setAiDnsAutoSwitch'))$('setAiDnsAutoSwitch').closest('.setting-item').hidden=mode!=='dns_ai';
  $('aiModeHelp').textContent={off:'Для нейросетей действуют общие правила маршрутизации.',vpn:'AI-сервисы идут через выбранный VPN-сервер. Нужен сервер в поддерживаемом регионе.',dns_vpn:'DNS и подключение к SmartDNS-реле идут через выбранный VPN. Конечный выход принадлежит SmartDNS. Провайдер выбирается вручную; прямой автоподбор и AI-фрагментация здесь не применяются.',dns_ai:'AI-домены идут напрямую через SmartDNS. Это отдельный маршрут: общий IP VPN не показывает адрес выхода Gemini. Доступ зависит от провайдера.'}[mode];
  if(global&&mode==='vpn')$('aiModeHelp').textContent='Глобальный маршрут: нейросети и остальной интернет идут через VPN. SmartDNS и исключения «Всегда напрямую» отключены. Для их использования выберите Smart RU.';
 }
 $('setAiMode').addEventListener('change',syncAiMode);
 $('setRoutingMode').addEventListener('change',syncAiMode);
 window.SplateExtras.collectSettings=()=>{syncAiMode();const mode=$('setAiMode').value;return {...collect(),aiRoutingMode:mode,enableAiDns:['dns_ai','dns_vpn'].includes(mode),enableAiBypass:mode!=='off',enableAiXboxDns:false,aiDnsProvider:$('setAiDnsProvider').value,aiTlsFragment:$('setAiTlsFragment').checked,adBlockAllowlist:$('setAdBlockAllowlist').value};};
 $('btnSelectAiDns').onclick=()=>send('selectAiDns');
 syncAiMode();
 $('btnCancelAiDns').onclick=()=>send('cancelAiDns');$('btnUpdateAdFilters').onclick=()=>send('updateAdFilters');
 let providers=new Map([['dns_ai','DNS-AI']]);
 addEventListener('splateMessage',event=>{
  const {type,payload:p}=event.detail;
  if(type==='smartDnsProviders'){
   const selected=$('setAiDnsProvider').value;providers=new Map(p.map(v=>[v.Key,v.Label]));
   $('setAiDnsProvider').replaceChildren(...p.map(v=>new Option(v.Label,v.Key)));$('setAiDnsProvider').value=selected;
   for(const v of p)if(!$('setDnsProvider').querySelector(`option[value="${v.Key}"]`))$('setDnsProvider').add(new Option(v.Label,v.Key));
  }
  if(type==='extendedSettings'){$('setAiMode').value=['off','vpn','dns_ai','dns_vpn'].includes(p.AiRoutingMode)?p.AiRoutingMode:p.EnableAiXboxDns?'dns_ai':!p.EnableAiBypass?'off':p.EnableAiDns?'dns_ai':'vpn';syncAiMode();$('setAiDnsProvider').value=p.AiDnsProvider||'dns_ai';$('setAiTlsFragment').checked=p.AiTlsFragment??false;$('setAdBlockAllowlist').value=p.AdBlockAllowlist||'';}
  if(type==='adFilters')$('adFilterStatus').textContent=(p.Count?`${p.Count.toLocaleString('ru-RU')} доменов. `:'')+(p.Status||'')+(p.Updated?` Обновлено: ${new Date(p.Updated).toLocaleString('ru-RU')}`:'');
  if(type==='operation'&&p.operation==='updateAdFilters'){$('btnUpdateAdFilters').disabled=p.busy;if(p.busy)$('adFilterStatus').textContent='Загрузка и проверка фильтров…';}
  if(type==='operation'&&p.operation==='selectAiDns'){dnsBusy=p.busy;$('btnSelectAiDns').disabled=p.busy;$('btnCancelAiDns').hidden=!p.busy;}
  if(type==='aiDnsSelection'){
   const phase={testing:'Проверка',verifying:'Повторная проверка',applied:'Сохранено и применяется к соединению',no_match:'Для Gemini нет подтверждённого варианта. Настройки не изменены; проверьте ограничения в результатах',cancelled:'Подбор остановлен. Настройки не изменены',error:'Ошибка проверки'}[p.Phase]||p.Phase;
   const current=p.Current?` · ${providers.get(p.Current.ProviderKey)||p.Current.ProviderKey}, фрагментация ${p.Current.TlsFragment?'вкл.':'выкл.'}`:'';
   const winner=p.Winner?` · ${providers.get(p.Winner.Choice.ProviderKey)||p.Winner.Choice.ProviderKey}, фрагментация ${p.Winner.Choice.TlsFragment?'вкл.':'выкл.'}`:'';
   $('aiDnsSelectionStatus').textContent=`${phase}: ${p.Completed}/${p.Total}${current}${winner}${p.Error?' · '+p.Error:''}`;
   $('aiDnsSelectionResults').replaceChildren(...(p.Results||[]).map(row=>{const item=document.createElement('div');item.className='ai-dns-result';const title=document.createElement('strong');title.textContent=`${providers.get(row.Choice.ProviderKey)||row.Choice.ProviderKey} · TLS ${row.Choice.TlsFragment?'вкл.':'выкл.'}`;const targets=document.createElement('span');targets.textContent=row.Targets.map(t=>`${t.Service}: ${['страница открывается','ограничение / проверка','региональный запрет','нет ответа'][t.Outcome]||t.Outcome}${t.ElapsedMs?' ('+t.ElapsedMs+' мс)':''}${t.Detail?' — '+t.Detail:''}`).join(' · ');item.append(title,targets);return item;}));
  }
 });
})();
