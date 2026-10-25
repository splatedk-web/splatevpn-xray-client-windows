(function () {
  'use strict';
  const $ = id => document.getElementById(id);
  const send = (action, data = {}) => window.chrome?.webview?.postMessage({ action, ...data });
  const settings = $('tab-settings').querySelector('.settings-container');
  for (const id of ['connectionProfileName', 'setUpstreamProxyHost']) settings.querySelector('.settings-grid').append($(id).closest('.settings-section'));
  settings.querySelector('.page-heading p').textContent = 'Быстрый поиск и настройки по категориям.';
  const toolbar = document.createElement('div');
  toolbar.className = 'settings-browser';
  toolbar.innerHTML = `<label class="settings-search" for="settingsSearch"><svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="2"><circle cx="10" cy="10" r="6"/><path d="m15 15 5 5"/></svg><input type="search" id="settingsSearch" placeholder="Найти настройку…" autocomplete="off"></label><div id="settingsCategories" class="settings-categories" role="group" aria-label="Категории настроек"></div><p id="settingsEmpty" class="settings-help" role="status" hidden>Ничего не найдено. Попробуйте другое слово или категорию.</p>`;
  settings.querySelector('.page-heading').after(toolbar);
  const region = document.createElement('div');
  region.className = 'setting-item';
  region.innerHTML = `<div class="item-text"><label for="setRoutingRegion">Регион маршрутизации</label><p>В умном маршруте сайты выбранного региона открываются напрямую. Международный профиль не добавляет региональные исключения.</p></div><select id="setRoutingRegion" class="tactical-select desktop-setting"><option value="ru">Россия</option><option value="cn">Китай</option><option value="ir">Иран</option><option value="global">Международный</option></select>`;
  $('setRoutingMode').closest('.setting-item').after(region);
  const lanControls = document.createElement('div'); lanControls.className = 'lan-controls';
  lanControls.innerHTML = `<div class="setting-item"><label for="setLanBindAddress">Интерфейс раздачи</label><select id="setLanBindAddress" class="tactical-select desktop-setting"><option value="">Все интерфейсы · без авторизации</option></select></div><p class="settings-help">При выборе конкретного адреса раздача не переходит на другую сеть, если адрес исчезнет.</p><div class="setting-item"><label for="setLanAuthEnabled">Защитить раздачу паролем</label><label class="toggle-switch"><input id="setLanAuthEnabled" class="desktop-setting" type="checkbox"><span class="slider"></span></label></div><div class="compact-fields"><label>Логин<input id="setLanUsername" class="tactical-input desktop-setting" value="splate" maxlength="64" autocomplete="off"></label><label>Пароль<input id="setLanPassword" class="tactical-input desktop-setting" type="password" maxlength="128" autocomplete="new-password"></label></div><div class="artifact-actions"><button type="button" id="showLanPassword" class="btn-secondary" aria-pressed="false">Показать пароль</button><button type="button" id="renewLanPassword" class="btn-secondary">Новый пароль</button><button type="button" id="copyLanAddress" class="btn-secondary">Копировать HTTP-адрес</button></div><p id="lanControlStatus" class="settings-help" role="status"></p>`;
  $('setShareLan').closest('.setting-item').after(lanControls);
  $('setShareLan').closest('.setting-item').querySelector('p').textContent = 'HTTP/SOCKS5 для устройств в одной локальной сети. Выберите интерфейс и при необходимости включите авторизацию. На другом устройстве задайте прокси вручную.';
  let configuredLanAddress = '', lanInterfaces = [];
  function renderLanInterfaces() {
    const select = $('setLanBindAddress'); select.replaceChildren(new Option('Все интерфейсы · без авторизации', ''));
    for (const nic of lanInterfaces) select.add(new Option(`${nic.name} · ${nic.address}`, nic.address));
    if (configuredLanAddress && !lanInterfaces.some(n => n.address === configuredLanAddress)) select.add(new Option(`${configuredLanAddress} · недоступен`, configuredLanAddress));
    select.value = configuredLanAddress;
  }
  function newLanPassword() {
    $('setLanPassword').value = [...crypto.getRandomValues(new Uint8Array(18))].map(b => b.toString(16).padStart(2, '0')).join('');
  }
  $('setLanBindAddress').addEventListener('change', () => { configuredLanAddress = $('setLanBindAddress').value; });
  $('setLanAuthEnabled').addEventListener('change', () => {
    if ($('setLanAuthEnabled').checked && !$('setLanBindAddress').value) { $('setLanAuthEnabled').checked = false; $('lanControlStatus').textContent = 'Сначала выберите конкретный сетевой интерфейс.'; return; }
    if ($('setLanAuthEnabled').checked && !$('setLanPassword').value) newLanPassword();
  });
  $('showLanPassword').onclick = () => { const show = $('setLanPassword').type === 'password'; $('setLanPassword').type = show ? 'text' : 'password'; $('showLanPassword').textContent = show ? 'Скрыть пароль' : 'Показать пароль'; $('showLanPassword').setAttribute('aria-pressed', String(show)); };
  $('renewLanPassword').onclick = () => { newLanPassword(); $('setLanPassword').dispatchEvent(new Event('change')); };
  $('copyLanAddress').onclick = async () => {
    if (!configuredLanAddress) { $('lanControlStatus').textContent = 'Выберите интерфейс для копирования адреса.'; return; }
    try { await navigator.clipboard.writeText(`${configuredLanAddress}:${$('setHttpPort').value}`); $('lanControlStatus').textContent = 'HTTP-адрес скопирован.'; }
    catch { $('lanControlStatus').textContent = `${configuredLanAddress}:${$('setHttpPort').value}`; }
  };
  const ruleSection = document.createElement('section'); ruleSection.className = 'card settings-section';
  ruleSection.innerHTML = `<div class="settings-sec-header"><h3>Правило для сайта</h3></div><p class="settings-help">Добавьте постоянное правило или временное исключение. Доменные правила охватывают поддомены. Блокировщик и служебные маршруты ядра сохраняют приоритет.</p><div class="site-rule-fields"><label for="siteRuleDomain">Домен или адрес сайта<input id="siteRuleDomain" class="tactical-input" placeholder="example.com" maxlength="2048"></label><label for="siteRuleRoute">Маршрут<select id="siteRuleRoute" class="tactical-select"><option value="proxy">Через VPN</option><option value="direct">Напрямую</option><option value="block">Блокировать</option></select></label><label for="siteRuleDuration">Срок<select id="siteRuleDuration" class="tactical-select"><option value="0">Постоянно</option><option value="15">15 минут</option><option value="60">1 час</option><option value="480">8 часов</option><option value="1440">24 часа</option></select></label></div><div class="artifact-actions"><button id="saveSiteRule" type="button" class="btn-secondary">Добавить правило</button><button id="previewSiteRule" type="button" class="btn-secondary">Посмотреть маршрут</button></div><pre id="siteRulePreview" class="settings-help" role="status"></pre><div id="temporarySiteRules"></div>`;
  $('routingContent').prepend(ruleSection);
  for (const action of ['saveSiteRule', 'previewSiteRule']) $(action).onclick = () => {
    if (!$('siteRuleDomain').value.trim()) { $('siteRulePreview').textContent = 'Введите домен сайта.'; $('siteRuleDomain').focus(); return; }
    if (window.SplateFlushSettings?.() === false) return;
    send(action, { domain: $('siteRuleDomain').value, route: $('siteRuleRoute').value, minutes: Number($('siteRuleDuration').value) });
  };
  const dns = document.createElement('div'); dns.className = 'dns-diagnostic';
  dns.innerHTML = `<label for="dnsTestDomain">Проверить ответ выбранного DNS</label><div class="artifact-actions"><input id="dnsTestDomain" class="tactical-input" value="example.com" maxlength="253" aria-label="Домен для проверки DNS"><button type="button" id="testDns" class="btn-secondary">Проверить DNS</button></div><pre id="dnsTestResult" class="settings-help" role="status"></pre>`;
  $('setDnsProvider').closest('.settings-section').append(dns);
  $('testDns').onclick = () => { if (window.SplateFlushSettings?.() === false) return; $('dnsTestResult').textContent = 'Проверяем DNS…'; send('testDns', { domain: $('dnsTestDomain').value }); };
  const collect = window.SplateExtras.collectSettings;
  const personal = document.createElement('label'); personal.className = 'follow-label';
  personal.innerHTML = '<input id="personalFolderEnabled" type="checkbox" checked> Личные конфигурации включены';
  document.querySelector('.toolbar-actions').append(personal);
  $('personalFolderEnabled').onchange = () => send('setPersonalFolder', { enabled: $('personalFolderEnabled').checked });
  const rollbackGeo = document.createElement('button'); rollbackGeo.type = 'button'; rollbackGeo.className = 'btn-secondary'; rollbackGeo.id = 'rollbackGeoAssets'; rollbackGeo.textContent = 'Откатить геобазы';
  rollbackGeo.onclick = () => send('rollbackGeoAssets'); $('btnUpdateGeo').after(rollbackGeo);
  $('subscriptionsList').addEventListener('click', event => {
    const button = event.target.closest('[data-refresh-action]'); if (!button) return;
    send(button.dataset.refreshAction, { id: button.closest('[data-sub-id]').dataset.subId });
  });
  let setupCompleted = false;
  let hasSettings = false, serverCount = null, offeredSetup = false;
  function offerSetup() {
    if (hasSettings && serverCount === 0 && !setupCompleted && !offeredSetup && !document.querySelector('dialog[open]')) {
      offeredSetup = true; help.click();
    }
  }
  window.SplateExtras.collectSettings = () => ({ ...collect(), routingRegion: $('setRoutingRegion').value, setupCompleted, lanBindAddress: $('setLanBindAddress').value, lanAuthEnabled: $('setLanAuthEnabled').checked, lanUsername: $('setLanUsername').value, lanPassword: $('setLanPassword').value });

  const categories = { all: 'Все', appearance: 'Оформление', network: 'Подключение', profiles: 'Профили', security: 'DNS и защита', system: 'Система', advanced: 'Дополнительно' };
  const classify = section => {
    if (section.querySelector('#setTheme')) return 'appearance';
    if (section.querySelector('#connectionProfileName')) return 'profiles';
    if (section.querySelector('#setDnsProvider')) return 'security';
    if (section.querySelector('#setAutoStart')) return 'system';
    if (section.querySelector('#setEnableTun, #setUpstreamProxyHost')) return 'advanced';
    return 'network';
  };
  const sections = [...settings.querySelectorAll('.settings-section')];
  sections.forEach(section => {
    section.dataset.category = classify(section);
    const heading = section.querySelector('h3');
    if (heading && !section.querySelector('.settings-sec-header svg')) {
      const icon = document.createElement('span'); icon.className = 'settings-category-icon'; icon.setAttribute('aria-hidden', 'true');
      icon.textContent = ({ appearance: '◐', profiles: '▤', security: '◇', system: '⌘', advanced: '⚙', network: '⇄' })[section.dataset.category];
      heading.before(icon);
    }
  });
  let category = 'all';
  function filter() {
    const query = $('settingsSearch').value.trim().toLocaleLowerCase('ru');
    let visible = 0;
    sections.forEach(section => {
      const text = section.textContent + ' ' + [...section.querySelectorAll('input,select,textarea')].map(e => e.placeholder || '').join(' ');
      const matches = (category === 'all' || section.dataset.category === category) && text.toLocaleLowerCase('ru').includes(query);
      section.hidden = !matches; if (matches) visible++;
    });
    $('settingsEmpty').hidden = visible > 0;
    toolbar.querySelectorAll('[data-category]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.category === category)));
  }
  Object.entries(categories).forEach(([key, name]) => {
    const button = document.createElement('button'); button.type = 'button'; button.className = 'btn-secondary'; button.textContent = name; button.dataset.category = key;
    button.onclick = () => { category = key; filter(); }; $('settingsCategories').append(button);
  });
  $('settingsSearch').addEventListener('input', filter); filter();
  document.addEventListener('keydown', event => {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f' && $('tab-settings').classList.contains('active') && !document.querySelector('dialog[open]')) {
      event.preventDefault(); $('settingsSearch').focus();
    }
  });
  // Compact route selector uses the same settings form and save path.
  const quick = document.createElement('div'); quick.className = 'quick-route';
  quick.innerHTML = `<div><span class="eyebrow">МАРШРУТ</span><p id="quickRouteDescription">Сайты своего региона напрямую, остальные через VPN</p></div><div class="route-segments" role="group" aria-label="Маршрут трафика"><button type="button" data-route="smart_ru">Умный</button><button type="button" data-route="global">Всё через VPN</button></div>`;
  document.querySelector('.home-left-col').prepend(quick);
  function syncRoute() {
    quick.querySelectorAll('[data-route]').forEach(b => { b.setAttribute('aria-pressed', String(b.dataset.route === $('setRoutingMode').value)); b.disabled = $('setRoutingMode').disabled; });
    const legacyDescription = $('toggleSmartRu').closest('.switch-row').querySelector('.switch-desc');
    legacyDescription.textContent = 'Сайты выбранного региона напрямую, остальные через VPN';
    const names = { ru: 'России', cn: 'Китая', ir: 'Ирана' };
    $('quickRouteDescription').textContent = $('setRoutingMode').value === 'global' ? 'Весь обрабатываемый трафик через выбранный сервер' : names[$('setRoutingRegion').value] ? `Сайты ${names[$('setRoutingRegion').value]} напрямую, остальные через VPN` : 'Международный профиль: без региональных исключений';
  }
  quick.addEventListener('click', event => { const button = event.target.closest('[data-route]'); if (!button || button.disabled) return; $('setRoutingMode').value = button.dataset.route; $('setRoutingMode').dispatchEvent(new Event('change')); syncRoute(); });
  $('setRoutingMode').addEventListener('change', syncRoute); $('setRoutingRegion').addEventListener('change', syncRoute);
  const help = document.createElement('button'); help.type = 'button'; help.className = 'btn-secondary'; help.id = 'btnSetupWizard'; help.textContent = 'Настроить пошагово'; toolbar.append(help);
  const wizard = document.createElement('dialog'); wizard.className = 'app-dialog setup-dialog'; wizard.id = 'setupWizard'; wizard.setAttribute('aria-labelledby', 'setupTitle');
  wizard.innerHTML = `<div class="modal-header"><h3 id="setupTitle">Настройка подключения</h3><button type="button" id="closeSetup" class="btn-secondary" aria-label="Закрыть">×</button></div><div class="modal-body"><p id="setupProgress" class="eyebrow"></p><div id="setupBody"></div></div><div class="modal-footer"><button type="button" id="setupBack" class="btn-secondary">Назад</button><button type="button" id="setupNext" class="btn-tactical btn-emerald">Далее</button></div>`;
  document.body.append(wizard);
  let step = 0, draft;
  const steps = [
    ['Регион маршрутизации', '<p>Регион определяет исключения для сайтов и DNS.</p><select id="setupRegion" class="tactical-select"><option value="ru">Россия</option><option value="cn">Китай</option><option value="ir">Иран</option><option value="global">Международный</option></select>'],
    ['Маршрут трафика', '<p>Умный маршрут открывает региональные сайты напрямую. Полный маршрут направляет обрабатываемый трафик через VPN.</p><select id="setupRoute" class="tactical-select"><option value="smart_ru">Умный маршрут</option><option value="global">Всё через VPN</option></select>'],
    ['Добавьте сервер', '<p>Для подключения понадобится сервер или подписка. Их можно добавить сейчас или после настройки.</p><button type="button" id="setupAddServer" class="btn-secondary">Добавить сервер или подписку</button>'],
    ['Как подключать приложения', '<p>Системный прокси подходит браузерам и приложениям с поддержкой прокси. TUN также перехватывает соединения игр и других программ; нужны права администратора.</p><select id="setupApps" class="tactical-select"><option value="proxy">Приложения с поддержкой прокси</option><option value="tun">Все приложения · TUN</option></select><p class="settings-help">Выбор отдельных программ в текущем ядре Windows пока не поддерживается.</p>']
  ];
  function remember() {
    if ($('setupRegion')) draft.region = $('setupRegion').value;
    if ($('setupRoute')) draft.route = $('setupRoute').value;
    if ($('setupApps')) draft.tun = $('setupApps').value === 'tun';
  }
  function renderStep() {
    $('setupTitle').textContent = steps[step][0]; $('setupProgress').textContent = `ШАГ ${step + 1} ИЗ ${steps.length}`; $('setupBody').innerHTML = steps[step][1];
    if ($('setupRegion')) $('setupRegion').value = draft.region;
    if ($('setupRoute')) $('setupRoute').value = draft.route;
    if ($('setupApps')) $('setupApps').value = draft.tun ? 'tun' : 'proxy';
    if ($('setupAddServer')) $('setupAddServer').onclick = () => { wizard.close(); $('btnAddServerDialog').click(); };
    $('setupBack').disabled = step === 0; $('setupNext').textContent = step === steps.length - 1 ? 'Применить настройки' : 'Далее';
  }
  help.onclick = () => { step = 0; draft = { region: $('setRoutingRegion').value, route: $('setRoutingMode').value, tun: $('setEnableTun').checked }; renderStep(); wizard.showModal(); };
  $('closeSetup').onclick = () => wizard.close();
  $('setupBack').onclick = () => { remember(); step--; renderStep(); };
  $('setupNext').onclick = () => {
    remember(); if (step < steps.length - 1) { step++; renderStep(); return; }
    $('setRoutingRegion').value = draft.region; $('setRoutingMode').value = draft.route; $('setEnableTun').checked = draft.tun; setupCompleted = true;
    if (window.SplateFlushSettings?.() === false) return;
    syncRoute(); wizard.close();
  };
  addEventListener('splateMessage', event => {
    const { type, payload } = event.detail;
    if (type === 'extendedSettings') {
      $('setRoutingRegion').value = payload.RoutingRegion || 'ru'; setupCompleted = !!payload.SetupCompleted; hasSettings = true; syncRoute();
      configuredLanAddress = payload.LanBindAddress || ''; $('setLanAuthEnabled').checked = !!payload.LanAuthEnabled; $('setLanUsername').value = payload.LanUsername || 'splate'; $('setLanPassword').value = payload.LanPassword || ''; renderLanInterfaces();
      $('personalFolderEnabled').checked = payload.PersonalServersEnabled !== false;
      $('temporarySiteRules').replaceChildren(...(payload.TemporaryRoutes || []).map(rule => {
        const row = document.createElement('div'); row.className = 'temporary-rule-row';
        const label = document.createElement('span'); label.textContent = `${rule.Domain} · ${ { proxy: 'через VPN', direct: 'напрямую', block: 'блокировать' }[rule.Route] } · до ${new Date(rule.ExpiresAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
        const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'btn-secondary'; remove.textContent = 'Удалить'; remove.setAttribute('aria-label', `Удалить правило ${rule.Domain}`); remove.onclick = () => send('removeSiteRule', { domain: rule.Domain }); row.append(label, remove); return row;
      }));
      offerSetup();
    }
    if (type === 'servers') { serverCount = payload.length; offerSetup(); }
    if (type === 'lanInterfaces') { lanInterfaces = payload; renderLanInterfaces(); }
    if (type === 'subscriptions') {
      document.querySelectorAll('#subscriptionsList [data-sub-id]').forEach(card => {
        const id = card.dataset.subId, subscription = payload.find(s => s.id === id);
        const history = document.createElement('button'); history.type = 'button'; history.className = 'btn-secondary'; history.dataset.refreshAction = 'subscriptionHistory'; history.textContent = 'История'; card.querySelector('.subscription-actions').append(history);
        if (subscription?.isUpdating) { const cancel = document.createElement('button'); cancel.type = 'button'; cancel.className = 'btn-secondary'; cancel.dataset.refreshAction = 'cancelSubscriptionUpdate'; cancel.textContent = 'Отменить загрузку'; card.querySelector('.subscription-actions').append(cancel); }
      });
    }
    if (type === 'subscriptionHistory') {
      $('toolsTitle').textContent = 'История обновлений подписки';
      const entries = payload.map(entry => { const row = document.createElement('p'); row.textContent = `${new Date(entry.Time).toLocaleString()} · ${ { success: 'обновлено', error: 'ошибка', cancelled: 'отменено' }[entry.Result] || entry.Result } · ${entry.Before} → ${entry.After} узлов${entry.Error ? '\n' + entry.Error : ''}`; return row; });
      if (!entries.length) { const empty = document.createElement('p'); empty.textContent = 'Обновлений пока нет.'; entries.push(empty); }
      $('toolsContent').replaceChildren(...entries); if (!$('toolsDialog').open) $('toolsDialog').showModal();
    }
    if (type === 'siteRulePreview') $('siteRulePreview').textContent = payload.message;
    if (type === 'dnsDiagnostic') $('dnsTestResult').textContent = payload.message;
    if (type === 'operation' && payload.operation === 'networkTools') ['testDns', 'saveSiteRule', 'previewSiteRule'].forEach(id => $(id).disabled = payload.busy);
    if (type === 'settings') syncRoute();
  });
})();
