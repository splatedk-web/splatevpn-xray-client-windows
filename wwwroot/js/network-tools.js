(function(){
 'use strict';
 const $=id=>document.getElementById(id),send=(action,data={})=>{if(['saveConnectionProfile','previewConnectionProfile','undoConnectionProfile'].includes(action)&&window.SplateFlushSettings?.()===false)return;window.chrome?.webview?.postMessage({action,...data});};
 const open=title=>{$('toolsTitle').textContent=title;$('toolsContent').replaceChildren();if(!$('toolsDialog').open)$('toolsDialog').showModal();};
 const button=(label,fn)=>{const b=document.createElement('button');b.type='button';b.className='btn-secondary';b.textContent=label;b.onclick=fn;return b;};
 document.querySelector('.smart-dns-tools').insertAdjacentHTML('afterbegin',`<div class="setting-item"><div class="item-text"><label for="setAiDnsAutoSwitch">Автопереключение SmartDNS</label><p>Каждые 5 минут при подключённом VPN. Повторно проверяет сбой и замену; CAPTCHA и вход не считаются отказом. Смена кратко переподключает VPN. Проверяются публичные страницы, доступ аккаунта не гарантируется.</p></div><label class="toggle-switch"><input id="setAiDnsAutoSwitch" class="desktop-setting" type="checkbox"><span class="slider"></span></label></div>`);
 const collect=window.SplateExtras.collectSettings;
 window.SplateExtras.collectSettings=()=>({...collect(),aiDnsAutoSwitch:$('setAiDnsAutoSwitch').checked});
 const section=document.createElement('section');section.className='card settings-section';
 section.innerHTML=`<div class="settings-sec-header"><h3>Профили подключения</h3></div><p class="settings-help">Сохраняют сетевые настройки и правила. До восьми профилей. Перед применением показывается предпросмотр; последнее применение можно отменить. Профиль можно привязать к текущему Wi-Fi или сетевому интерфейсу: он применится при смене сети. Доступ к имени Wi-Fi зависит от разрешений Windows.</p><label for="connectionProfileName">Название профиля</label><input id="connectionProfileName" maxlength="48" class="tactical-select" placeholder="Мои настройки"><div id="connectionProfileActions" class="artifact-actions"></div><div id="connectionProfileList" class="artifact-actions"></div><p id="settingsRecoveryNotice" class="settings-help" role="status"></p>`;
 $('setRoutingMode').closest('.settings-section').after(section);
 $('connectionProfileActions').append(button('Сохранить текущие',()=>send('saveConnectionProfile',{name:$('connectionProfileName').value})),button('Откатить применение',()=>send('undoConnectionProfile')));
 $('connectionProfileActions').lastChild.id='undoConnectionProfile';
 for(const [id,label] of [['smart','Умный маршрут'],['all','Все приложения через VPN'],['ai','Нейросети через VPN']])$('connectionProfileActions').append(button(label,()=>send('previewConnectionProfile',{id})));
 document.querySelector('.connection-tools .artifact-actions').append(button('Расширенная диагностика',()=>{open('Предпросмотр диагностического отчёта');send('extendedDiagnostic');}),button('Как подключить устройство по LAN',()=>{open('Подключение по локальной сети');renderLan();}));
 let lan=[],canUndo=false;
 function renderLan(){
  const p=document.createElement('p');p.textContent='Включите «Доступ из локальной сети» в настройках и подключите VPN. Второе устройство должно быть в той же локальной сети. Выберите адрес физического адаптера ПК; не используйте адрес TUN. Прокси обслуживает только настроенные приложения и не заменяет системный VPN второго устройства.';
  const addresses=document.createElement('pre');addresses.style.whiteSpace='pre-wrap';addresses.textContent=lan.length?lan.join('\n'):'Подходящих IPv4-адресов не найдено. Проверьте подключение к локальной сети.';
  const help=document.createElement('p');help.style.whiteSpace='pre-line';help.textContent='Android / iPhone: Wi-Fi → текущая сеть → прокси вручную → адрес ПК и HTTP-порт.\nWindows: Параметры → Сеть и Интернет → Прокси → ручная настройка.\nmacOS: Системные настройки → Сеть → активная сеть → Прокси → HTTP и HTTPS.\nLinux: GNOME/KDE → Сеть → прокси вручную, HTTP и HTTPS с одним адресом и HTTP-портом.\nОтдельное приложение: SOCKS5, адрес ПК и SOCKS-порт; включите удалённое разрешение DNS, если доступно.\nПосле использования отключите прокси на втором устройстве. Если включена авторизация раздачи, укажите логин и пароль из настроек SplateVPN. Используйте доверенную сеть. Если соединение не проходит, проверьте доступ к порту в брандмауэре и изоляцию клиентов Wi-Fi.';
  $('toolsContent').replaceChildren(p,addresses,help);
 }
 addEventListener('splateMessage',e=>{
  const {type,payload:p}=e.detail;
  if(type==='extendedSettings'){$('setAiDnsAutoSwitch').checked=p.AiDnsAutoSwitch??true;$('setAiDnsAutoSwitch').closest('.setting-item').hidden=p.AiRoutingMode!=='dns_ai';}
  if(type==='lanEndpoints')lan=p;
  if(type==='connectionProfiles'){
   canUndo=p.canUndo;$('undoConnectionProfile').disabled=!canUndo;$('settingsRecoveryNotice').textContent=p.recoveryNotice||'';
   $('connectionProfileList').replaceChildren(...p.profiles.map(profile=>{const row=document.createElement('div');row.className='artifact-actions';row.append(button(profile.Name,()=>send('previewConnectionProfile',{id:profile.Id})),button(profile.NetworkLabel?'Отвязать: '+profile.NetworkLabel:'Привязать к текущей сети',()=>send(profile.NetworkLabel?'unbindConnectionProfile':'bindConnectionProfile',{id:profile.Id})),button('Удалить',()=>send('deleteConnectionProfile',{id:profile.Id})));return row;}));
  }
  if(type==='connectionProfilePreview'){
   open('Применение профиля');const text=document.createElement('pre');text.style.whiteSpace='pre-wrap';text.textContent=p.message;
   $('toolsContent').append(text,button('Применить',()=>{send('applyConnectionProfile',{id:p.id,snapshot:p.snapshot});$('toolsDialog').close();}));
  }
  if(type==='extendedDiagnostic'){
   open('Предпросмотр диагностического отчёта');const report=document.createElement('pre');report.style.cssText='white-space:pre-wrap;overflow-wrap:anywhere';report.textContent=p.message;
   $('toolsContent').append(report,button('Копировать этот отчёт',()=>send('copyExtendedDiagnostic')));
  }
  if(type==='operation'&&p.operation==='networkTools')section.querySelectorAll('button').forEach(b=>b.disabled=p.busy||(b.id==='undoConnectionProfile'&&!canUndo));
 });
})();
