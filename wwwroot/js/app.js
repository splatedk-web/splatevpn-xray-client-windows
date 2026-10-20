/**
 * SplateVPN Tactical Client - Frontend Application Logic
 * High-performance 60FPS telemetry, Cyber-tactical aesthetics & C# WebView2 IPC Bridge
 */

(function () {
  'use strict';

  function showNotification({title = '', message = '', kind = 'info'} = {}) {
    const region = document.getElementById('notificationRegion');
    const item = document.createElement('div');
    item.className = 'app-notification ' + (['error','warning'].includes(kind) ? kind : 'info');
    const heading = document.createElement('strong'); heading.textContent = title;
    const text = document.createElement('p'); text.textContent = message;
    const close = document.createElement('button'); close.className = 'notification-close';
    close.type = 'button'; close.textContent = '×'; close.setAttribute('aria-label','Закрыть уведомление');
    close.onclick = () => item.remove();
    item.append(heading, text, close); region.append(item);
    while (region.children.length > 3) region.firstElementChild.remove();
    setTimeout(() => item.remove(), kind === 'error' ? 12000 : 5500);
  }

  let confirmationQueue = Promise.resolve();
  function showConfirmation({title = 'Подтверждение', message = '', acceptLabel = 'Подтвердить'} = {}) {
    const result = confirmationQueue.then(() => new Promise(resolve => {
      const dialog = document.getElementById('confirmationDialog');
      const priorFocus = document.activeElement;
      document.getElementById('confirmationTitle').textContent = title;
      document.getElementById('confirmationText').textContent = message;
      const accept = document.getElementById('confirmationAccept');
      const cancel = document.getElementById('confirmationCancel');
      accept.textContent = acceptLabel;
      const finish = value => {
        accept.onclick = cancel.onclick = dialog.oncancel = null;
        dialog.close();
        if (priorFocus && priorFocus.isConnected) priorFocus.focus();
        resolve(value);
      };
      accept.onclick = () => finish(true);
      cancel.onclick = () => finish(false);
      dialog.oncancel = event => { event.preventDefault(); finish(false); };
      dialog.showModal(); cancel.focus();
    }));
    confirmationQueue = result.then(() => undefined);
    return result;
  }

  // --- State Store ---
  const state = {
    isConnected: false,
    isConnecting: false,
    statusText: 'ОТКЛЮЧЕНО',
    statusDetail: 'Туннель не активен · Нажмите для защиты',
    selectedServer: null,
    servers: [],
    filterType: 'all',
    searchQuery: '',
    sortOrder: 'default',
    sessionDuration: '00:00:00',
    currentIp: 'Определение...',
    currentLocation: '',
    rtt: -1,
    downloadSpeedStr: '0 B/s',
    uploadSpeedStr: '0 B/s',
    sessionDataStr: '0 B',
    // Speeds for canvas graph (bytes per sec)
    downloadBpsHistory: new Array(30).fill(0),
    uploadBpsHistory: new Array(30).fill(0),
    settings: {
      enableTunMode: false,
      routingMode: 'smart_ru',
      xhttpMode: 'auto',
      bufferSize: '4096',
      enableFragment: false,
      dnsProvider: 'cloudflare',
      isFakeDns: false,
      blockQuic: true,
      blockIpv6: true
    }
  };

  // --- IPC Bridge Helper ---
  function sendToHost(payload) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(payload);
    } else {
      console.warn('[IPC] WebView2 host not detected, payload:', payload);
    }
  }

  // --- DOM Elements ---
  const els = {
    setTheme: document.getElementById("setTheme"),
    // Titlebar
    titlebar: document.getElementById('titlebar'),
    headerStatusDot: document.getElementById('headerStatusDot'),
    headerStatusLabel: document.getElementById('headerStatusLabel'),
    headerPingVal: document.getElementById('headerPingVal'),
    btnMinimize: document.getElementById('btnMinimize'),
    btnMaximize: document.getElementById('btnMaximize'),
    btnClose: document.getElementById('btnClose'),

    // Nav
    navItems: document.querySelectorAll('.nav-item'),
    tabPanes: document.querySelectorAll('.tab-pane'),
    navServersCount: document.getElementById('navServersCount'),
    sidebarAdapterPill: document.getElementById('sidebarAdapterPill'),
    sidebarAdapterText: document.getElementById('sidebarAdapterText'),

    // Home
    btnPowerCore: document.getElementById('btnPowerCore'),
    coreStatusText: document.getElementById('coreStatusText'),
    coreStatusDetail: document.getElementById('coreStatusDetail'),
    btnToggleConnection: document.getElementById('btnToggleConnection'),
    btnToggleConnectionText: document.getElementById('btnToggleConnectionText'),
    microProtocol: document.getElementById('microProtocol'),
    microAdapter: document.getElementById('microAdapter'),
    microDns: document.getElementById('microDns'),
    selectedServerCard: document.getElementById('selectedServerCard'),
    selectedServerFlag: document.getElementById('selectedServerFlag'),
    selectedServerName: document.getElementById('selectedServerName'),
    selectedServerProtocol: document.getElementById('selectedServerProtocol'),
    selectedServerTransport: document.getElementById('selectedServerTransport'),
    selectedServerGroup: document.getElementById('selectedServerGroup'),
    selectedServerPingText: document.getElementById('selectedServerPingText'),
    btnChangeServer: document.getElementById('btnChangeServer'),
    toggleTunMode: document.getElementById('toggleTunMode'),
    toggleSmartRu: document.getElementById('toggleSmartRu'),

    // Telemetry & Speed
    liveDownloadVal: document.getElementById('liveDownloadVal'),
    liveDownloadUnit: document.getElementById('liveDownloadUnit'),
    liveUploadVal: document.getElementById('liveUploadVal'),
    liveUploadUnit: document.getElementById('liveUploadUnit'),
    speedCanvas: document.getElementById('speedCanvas'),
    sessionDurationVal: document.getElementById('sessionDurationVal'),
    sessionDataVal: document.getElementById('sessionDataVal'),
    currentIpVal: document.getElementById('currentIpVal'),
    currentLocationVal: document.getElementById('currentLocationVal'),
    btnRefreshIp: document.getElementById('btnRefreshIp'),

    // Servers Tab
    serverSearchInput: document.getElementById('serverSearchInput'),
    btnSearchClear: document.getElementById('btnSearchClear'),
    btnPingAllServers: document.getElementById('btnPingAllServers'),
    btnAddServerDialog: document.getElementById('btnAddServerDialog'),
    btnUpdateSubs: document.getElementById('btnUpdateSubs'),
    serverTabs: document.querySelectorAll('.server-tab'),
    countAllNodes: document.getElementById('countAllNodes'),
    countPresetNodes: document.getElementById('countPresetNodes'),
    countSubNodes: document.getElementById('countSubNodes'),
    serverListContainer: document.getElementById('serverListContainer'),

    // Settings Tab
    setEnableTun: document.getElementById('setEnableTun'),
    setXhttpMode: document.getElementById('setXhttpMode'),
    setBufferSize: document.getElementById('setBufferSize'),
    setEnableFragment: document.getElementById('setEnableFragment'),
    setDnsProvider: document.getElementById('setDnsProvider'),
    setIsFakeDns: document.getElementById('setIsFakeDns'),
    setBlockQuic: document.getElementById('setBlockQuic'),
    setBlockIpv6: document.getElementById('setBlockIpv6'),
    setDnsLeakProtection: document.getElementById('setDnsLeakProtection'),
    setKillSwitch: document.getElementById('setKillSwitch'),
    setAutoStart: document.getElementById('setAutoStart'),
    setMinimizeToTray: document.getElementById('setMinimizeToTray'),
    setAutoFallback: document.getElementById('setAutoFallback'),
    setAutoUpdateSub: document.getElementById('setAutoUpdateSub'),
    btnSaveSettings: document.getElementById('btnSaveSettings'),
    autoSaveIndicator: document.getElementById('autoSaveIndicator'),
    autoSaveText: document.getElementById('autoSaveText'),
    settingsReconnectNotice: document.getElementById('settingsReconnectNotice'),

    // Home quick switches
    toggleKillSwitch: document.getElementById('toggleKillSwitch'),
    toggleAutoFallback: document.getElementById('toggleAutoFallback'),
    btnAutoSelect: document.getElementById('btnAutoSelect'),

    // Logs Tab
    logsTerminal: document.getElementById('logsTerminal'),
    btnCopyLogs: document.getElementById('btnCopyLogs'),
    btnClearLogs: document.getElementById('btnClearLogs'),

    // Modal
    addServerModal: document.getElementById('addServerModal'),
    addServerInput: document.getElementById('addServerInput'),
    btnAddModalClose: document.getElementById('btnAddModalClose'),
    btnAddModalCancel: document.getElementById('btnAddModalCancel'),
    btnAddModalConfirm: document.getElementById('btnAddModalConfirm')
  };

  // --- Initialize App ---
  function init() {
    setupWindowControls();
    setupNavigation();
    setupHomeEvents();
    setupServersEvents();
    setupSettingsEvents();
    setupLogsEvents();
    setupModalEvents();
    setupSpeedCanvas();
    setupIPCListener();
    document.querySelectorAll('.setting-item').forEach(row => {
      const label = row.querySelector('.item-text > label');
      const input = row.querySelector('input, select');
      if (label && input) label.htmlFor = input.id;
    });
    document.querySelectorAll('button[title]').forEach(button => button.setAttribute('aria-label', button.title));
    ['toggleTunMode','toggleSmartRu','toggleKillSwitch','toggleAutoFallback'].forEach((id, i) =>
      document.getElementById(id).setAttribute('aria-label', ['Системный туннель TUN','Российские сайты напрямую','Kill Switch','Автовыбор при обрыве'][i]));

    // Signal C# backend that frontend is ready to receive state
    sendToHost({ action: 'ready' });
  }

  // --- Window Controls & Dragging ---
  function setupWindowControls() {
    if (els.titlebar) {
      els.titlebar.addEventListener('mousedown', (e) => {
        // Only trigger native Win32 drag on left-click and outside interactive elements
        if (e.button !== 0) return;
        if (e.target.closest('button') || e.target.closest('input') || e.target.closest('select') || e.target.closest('.interactive')) {
          return;
        }
        sendToHost({ action: 'windowAction', command: 'drag' });
      });

      // Double-click on caption bar toggles maximize / restore
      els.titlebar.addEventListener('dblclick', (e) => {
        if (e.target.closest('button') || e.target.closest('input') || e.target.closest('select')) return;
        sendToHost({ action: 'windowAction', command: 'maximize' });
      });
    }

    if (els.btnMinimize) {
      els.btnMinimize.addEventListener('click', (e) => {
        e.stopPropagation();
        sendToHost({ action: 'windowAction', command: 'minimize' });
      });
    }

    if (els.btnMaximize) {
      els.btnMaximize.addEventListener('click', (e) => {
        e.stopPropagation();
        sendToHost({ action: 'windowAction', command: 'maximize' });
      });
    }

    if (els.btnClose) {
      els.btnClose.addEventListener('click', (e) => {
        e.stopPropagation();
        sendToHost({ action: 'windowAction', command: 'close' });
      });
    }
  }

  // --- Navigation ---
  function setupNavigation() {
    els.navItems.forEach((btn) => {
      btn.addEventListener('click', () => {
        const tabId = btn.getAttribute('data-tab');
        switchTab(tabId);
      });
    });

    if (els.btnChangeServer) {
      els.btnChangeServer.addEventListener('click', () => {
        switchTab('tab-servers');
      });
    }
  }

  function switchTab(tabId) {
    els.navItems.forEach((item) => {
      item.setAttribute('aria-current', item.dataset.tab === tabId ? 'page' : 'false');
      if (item.getAttribute('data-tab') === tabId) {
        item.classList.add('active');
      } else {
        item.classList.remove('active');
      }
    });

    els.tabPanes.forEach((pane) => {
      if (pane.id === tabId) {
        pane.classList.add('active');
      } else {
        pane.classList.remove('active');
      }
    });
    redrawTraffic();
    document.getElementById('mainViewport').scrollTop = 0;
  }

  // --- Home Tab Events ---
  function setupHomeEvents() {
    const handleToggle = () => {
      if (state.isConnecting) {
        if (els.btnToggleConnectionText) els.btnToggleConnectionText.textContent = 'Отмена...';
        if (els.coreStatusDetail) els.coreStatusDetail.textContent = 'Прерывание процесса подключения...';
      } else if (!state.isConnected) {
        if (els.btnToggleConnection) els.btnToggleConnection.className = 'btn-primary-action btn-connecting';
        if (els.btnToggleConnectionText) els.btnToggleConnectionText.textContent = 'Отменить подключение';
        if (els.coreStatusText) els.coreStatusText.textContent = 'ПОДКЛЮЧЕНИЕ...';
        if (els.coreStatusDetail) els.coreStatusDetail.textContent = 'Инициализация туннеля...';
      } else {
        if (els.btnToggleConnectionText) els.btnToggleConnectionText.textContent = 'Отключение...';
      }
      sendToHost({ action: 'toggleConnection' });
    };

    if (els.selectedServerCard) {
      els.selectedServerCard.addEventListener('click', () => switchTab('tab-servers'));
      els.selectedServerCard.addEventListener('keydown', e => { if (e.target === els.selectedServerCard && ['Enter',' '].includes(e.key)) { e.preventDefault(); switchTab('tab-servers'); } });
    }
    if (els.btnPowerCore) els.btnPowerCore.addEventListener('click', handleToggle);
    if (els.btnToggleConnection) els.btnToggleConnection.addEventListener('click', handleToggle);

    if (els.toggleTunMode) {
      els.toggleTunMode.addEventListener('change', (e) => {
        const checked = e.target.checked;

        if (els.setEnableTun) els.setEnableTun.checked = checked;
        saveCurrentSettings();
      });
    }

    if (els.toggleSmartRu) {
      els.toggleSmartRu.addEventListener('change', (e) => {
        const mode = e.target.checked ? 'smart_ru' : 'global';
        sendToHost({ action: 'setSetting', key: 'RoutingMode', value: mode });
      });
    }

    if (els.toggleKillSwitch) {
      els.toggleKillSwitch.addEventListener('change', (e) => {
        const checked = e.target.checked;
        sendToHost({ action: 'setSetting', key: 'EnableKillSwitch', value: checked });
        if (els.setKillSwitch) els.setKillSwitch.checked = checked;
      });
    }

    if (els.toggleAutoFallback) {
      els.toggleAutoFallback.addEventListener('change', (e) => {
        const checked = e.target.checked;
        sendToHost({ action: 'setSetting', key: 'AutoFallbackFastestServer', value: checked });
        if (els.setAutoFallback) els.setAutoFallback.checked = checked;
      });
    }

    if (els.btnAutoSelect) {
      els.btnAutoSelect.addEventListener('click', () => {
        els.btnAutoSelect.disabled = true;
        els.btnAutoSelect.textContent = 'Поиск...';
        sendToHost({ action: 'autoSelectServer' });

      });
    }

    if (els.btnRefreshIp) {
      els.btnRefreshIp.addEventListener('click', () => {
        if (els.currentIpVal) els.currentIpVal.textContent = 'Обновление...';
        sendToHost({ action: 'refreshIp' });
      });
    }
  }

  function updateAdapterUI(isTun) {
    if (state.isConnected) isTun = state.activeTunMode;
    const modeStr = isTun ? 'Wintun TUN' : 'System Proxy';
    if (els.sidebarAdapterText) els.sidebarAdapterText.textContent = modeStr;
    if (els.microAdapter) els.microAdapter.textContent = modeStr;
  }

  // --- Servers Tab Events & Filtering ---
  function setupServersEvents() {
    const remember=()=>sendToHost({action:'serverPreferences',sort:state.sortOrder,filter:state.filterType,source:document.getElementById('subscriptionSource').value,grouped:document.getElementById('groupServers').checked});
    const resetFilters=()=>{
      state.filterType='all';state.searchQuery='';els.serverSearchInput.value='';
      document.getElementById('subscriptionSource').value='Все';
      els.serverTabs.forEach(t=>t.classList.toggle('active',t.dataset.filter==='all'));
      remember();renderServerList();
    };
    document.getElementById('btnResetNodeFilters').onclick=resetFilters;
    document.getElementById('btnRevealSelected').onclick=()=>{
      resetFilters();
      const card=els.serverListContainer.querySelector(`[data-id="${CSS.escape(state.selectedServer?.id||'')}"]`);
      if(!card)return;
      if(card.hidden){let heading=card.previousElementSibling;while(heading&&!heading.classList.contains('server-group-heading'))heading=heading.previousElementSibling;if(heading)heading.click();}
      card.scrollIntoView({block:'center'});card.focus({preventScroll:true});
    };
    document.getElementById('subscriptionSource').addEventListener('change',()=>{remember();renderServerList();});
    document.getElementById('groupServers').addEventListener('change',e=>{remember();try{localStorage.setItem('splate-grouped',String(e.target.checked));}catch(_){}renderServerList();});
    try{document.getElementById('groupServers').checked=localStorage.getItem('splate-grouped')!=='false';}catch(_){}
    document.getElementById('serverSort').addEventListener('change',e=>{state.sortOrder=e.target.value;remember();});
    els.serverTabs.forEach(tab=>tab.addEventListener('click',()=>{state.filterType=tab.dataset.filter;document.getElementById('subscriptionSource').value='Все';remember();}));
    document.getElementById('serverSort').addEventListener('change', e => { state.sortOrder = e.target.value; renderServerList(); });
    if (els.serverSearchInput) {
      els.serverSearchInput.addEventListener('input', (e) => {
        state.searchQuery = e.target.value.toLowerCase().trim();
        renderServerList();
      });
    }

    if (els.btnSearchClear) {
      els.btnSearchClear.addEventListener('click', () => {
        if (els.serverSearchInput) {
          els.serverSearchInput.value = '';
          state.searchQuery = '';
          renderServerList();
        }
      });
    }

    els.serverTabs.forEach((tab) => {
      tab.addEventListener('click', () => {
        els.serverTabs.forEach((t) => t.classList.remove('active'));
        tab.classList.add('active');
        state.filterType = tab.getAttribute('data-filter') || 'all';
        renderServerList();
      });
    });

    if (els.btnPingAllServers) {
      els.btnPingAllServers.addEventListener('click', () => {
        if (state.pingBusy) { sendToHost({ action: 'cancelPing' }); return; }
        sendToHost({ action: 'pingAll' });
      });
    }

    if (els.btnUpdateSubs) {
      els.btnUpdateSubs.addEventListener('click', () => {
        sendToHost({ action: 'updateSubscription' });
      });
    }

    if (els.btnAddServerDialog) {
      els.btnAddServerDialog.addEventListener('click', () => {
        if (els.addServerModal) {
          els.addServerModal.showModal();
          document.getElementById('addSubscriptionName').value = '';
          document.getElementById('subscriptionNameField').hidden = true;
          if (els.addServerInput) {
            els.addServerInput.value = '';
            els.addServerInput.focus();
          }
        }
      });
    }
  }

  // --- Vector SVG Flag Renderer ---
  function getCountryFlagSvg(flag, name) {
    const text = ((flag || '') + ' ' + (name || '')).toUpperCase();

    if (/(?:^|[^A-Z])RU(?:$|[^A-Z])/.test(text) || text.includes('RUSSIA') || text.includes('РОССИЯ') || text.includes('🇷🇺')) {
      return `<svg class="flag-icon" viewBox="0 0 640 480" width="22" height="16"><g fill-rule="evenodd" stroke-width="1pt"><path fill="#fff" d="M0 0h640v160H0z"/><path fill="#0039a6" d="M0 160h640v160H0z"/><path fill="#d52b1e" d="M0 320h640v160H0z"/></g></svg>`;
    }
    if (/(?:^|[^A-Z])NL(?:$|[^A-Z])/.test(text) || text.includes('NETHERLANDS') || text.includes('НИДЕРЛАНДЫ') || text.includes('AMSTERDAM') || text.includes('🇳🇱')) {
      return `<svg class="flag-icon" viewBox="0 0 640 480" width="22" height="16"><g fill-rule="evenodd" stroke-width="1pt"><path fill="#21468b" d="M0 320h640v160H0z"/><path fill="#fff" d="M0 160h640v160H0z"/><path fill="#ae1c28" d="M0 0h640v160H0z"/></g></svg>`;
    }
    if (/(?:^|[^A-Z])DE(?:$|[^A-Z])/.test(text) || text.includes('GERMANY') || text.includes('ГЕРМАНИЯ') || text.includes('FRANKFURT') || text.includes('🇩🇪')) {
      return `<svg class="flag-icon" viewBox="0 0 640 480" width="22" height="16"><path fill="#000" d="M0 0h640v160H0z"/><path fill="#d00" d="M0 160h640v160H0z"/><path fill="#ffce00" d="M0 320h640v160H0z"/></svg>`;
    }
    if (/(?:^|[^A-Z])US(?:$|[^A-Z])/.test(text) || text.includes('USA') || text.includes('США') || text.includes('AMERICA') || text.includes('🇺🇸')) {
      return `<svg class="flag-icon" viewBox="0 0 640 480" width="22" height="16"><path fill="#bd3d44" d="M0 0h640v480H0z"/><path stroke="#fff" stroke-width="37" d="M0 55h640M0 129h640M0 203h640M0 277h640M0 351h640M0 425h640"/><path fill="#192f5d" d="M0 0h260v260H0z"/></svg>`;
    }
    if (/(?:^|[^A-Z])FI(?:$|[^A-Z])/.test(text) || text.includes('FINLAND') || text.includes('ФИНЛЯНДИЯ') || text.includes('🇫🇮')) {
      return `<svg class="flag-icon" viewBox="0 0 640 480" width="22" height="16"><path fill="#fff" d="M0 0h640v480H0z"/><path fill="#002f6c" d="M0 175h640v130H0z"/><path fill="#002f6c" d="M175 0h130v480H175z"/></svg>`;
    }
    if (/(?:^|[^A-Z])SE(?:$|[^A-Z])/.test(text) || text.includes('SWEDEN') || text.includes('ШВЕЦИЯ') || text.includes('🇸🇪')) {
      return `<svg class="flag-icon" viewBox="0 0 640 480" width="22" height="16"><path fill="#006aa7" d="M0 0h640v480H0z"/><path fill="#fecc00" d="M0 180h640v120H0z"/><path fill="#fecc00" d="M180 0h120v480H180z"/></svg>`;
    }
    if (/(?:^|[^A-Z])KZ(?:$|[^A-Z])/.test(text) || text.includes('KAZAKHSTAN') || text.includes('КАЗАХСТАН') || text.includes('🇰🇿')) {
      return `<svg class="flag-icon" viewBox="0 0 640 480" width="22" height="16"><path fill="#00afca" d="M0 0h640v480H0z"/><circle cx="320" cy="240" r="60" fill="#fec50c"/></svg>`;
    }
    if (/(?:^|[^A-Z])FR(?:$|[^A-Z])/.test(text) || text.includes('FRANCE') || text.includes('ФРАНЦИЯ') || text.includes('🇫🇷')) {
      return `<svg class="flag-icon" viewBox="0 0 640 480" width="22" height="16"><path fill="#002654" d="M0 0h213v480H0z"/><path fill="#fff" d="M213 0h214v480H213z"/><path fill="#ce1126" d="M427 0h213v480H427z"/></svg>`;
    }
    if (/(?:^|[^A-Z])GB(?:$|[^A-Z])/.test(text) || /(?:^|[^A-Z])UK(?:$|[^A-Z])/.test(text) || text.includes('LONDON') || text.includes('🇬🇧')) {
      return `<svg class="flag-icon" viewBox="0 0 640 480" width="22" height="16"><path fill="#012169" d="M0 0h640v480H0z"/><path stroke="#fff" stroke-width="60" d="m0 0 640 480m0-480L0 480M0 240h640M320 0v480"/><path stroke="#c8102e" stroke-width="36" d="M0 240h640M320 0v480m-320-240 640 480m0-480L0 480"/></svg>`;
    }
    if (/(?:^|[^A-Z])TR(?:$|[^A-Z])/.test(text) || text.includes('TURKEY') || text.includes('ТУРЦИЯ') || text.includes('🇹🇷')) {
      return `<svg class="flag-icon" viewBox="0 0 640 480" width="22" height="16"><path fill="#e30a17" d="M0 0h640v480H0z"/><circle cx="260" cy="240" r="100" fill="#fff"/><circle cx="285" cy="240" r="80" fill="#e30a17"/></svg>`;
    }

    return `<svg class="flag-icon flag-globe" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" width="18" height="18"><circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/></svg>`;
  }

  // --- Render Server List ---
  function renderServerList() {
    if (!els.serverListContainer) return;
    let filtered = SplateNodes.filter(state.servers,{...state,source:document.getElementById('subscriptionSource').value});
    document.getElementById('serverResultCount').textContent = `Показано ${filtered.length} из ${state.servers.length}`;
    document.getElementById('nodeSelectionContext').textContent=state.selectedServer ? `${state.isConnected?'Подключён':'Выбран'}: ${state.selectedServer.displayName||state.selectedServer.address}` : 'Сервер не выбран · выберите узел в списке';
    document.getElementById('btnRevealSelected').hidden=!state.selectedServer;
    els.btnSearchClear.hidden = !state.searchQuery;
    // Update counts
    const presetCount = state.servers.filter(SplateNodes.preset).length;
    const subCount = state.servers.filter(s => !SplateNodes.preset(s) && !SplateNodes.personal(s)).length;
    if (els.countAllNodes) els.countAllNodes.textContent = state.servers.filter(s=>s.enabled!==false).length;
    if (els.countPresetNodes) els.countPresetNodes.textContent = presetCount;
    if (els.countSubNodes) els.countSubNodes.textContent = subCount;
    if (els.navServersCount) els.navServersCount.textContent = state.servers.filter(s=>s.enabled!==false).length;

    const disabledFolders=!state.searchQuery&&document.getElementById('groupServers').checked&&['all','preset','subscriptions'].includes(state.filterType)&&(state.subscriptions||[]).some(sub=>sub.enabled===false);
    if (filtered.length === 0 && !disabledFolders) {
      els.serverListContainer.innerHTML = `
        <div class="empty-state">
          <div class="empty-icon">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" width="46" height="46">
              <circle cx="12" cy="12" r="10"/>
              <line x1="2" y1="12" x2="22" y2="12"/>
              <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>
            </svg>
          </div>
          <div class="empty-title">Узлы не найдены</div>
          <div class="empty-desc">${state.servers.length ? "Измените запрос или выберите вкладку «Все»." : "Добавьте ссылку на сервер или подписку, чтобы подключиться."}</div>
        </div>
      `;
      return;
    }

    // Build DOM
    els.serverListContainer.innerHTML = '';
    filtered.forEach((srv) => {
      const card = document.createElement('div');
      const isSelected = state.selectedServer && (state.selectedServer.id === srv.id);
      card.className = `server-card ${isSelected ? 'selected' : ''} ${srv.enabled === false ? 'disabled' : ''}`;
      card.setAttribute('data-id', srv.id);
      card.tabIndex = 0;
      card.setAttribute('role', 'option');
      card.setAttribute('aria-selected', String(Boolean(isSelected)));
      card.setAttribute('aria-label', srv.displayName || srv.address);
      card.addEventListener('keydown', e => {
        if (e.target === card && srv.enabled !== false && ['Enter',' '].includes(e.key)) { e.preventDefault(); sendToHost({action:'selectServer',id:srv.id}); }
      });

      let pingClass = 'ping-gray';
      let pingText = srv.ping < 0 ? 'Нет ответа' : '—';
      if (srv.ping > 0) {
        pingText = `${srv.ping} ms`;
        if (srv.ping < 120) pingClass = 'ping-green';
        else if (srv.ping < 280) pingClass = 'ping-yellow';
        else pingClass = 'ping-red';
      }

      card.innerHTML = `
        <div class="server-card-left">
          <div class="card-radio"></div>
          <span class="card-flag">${getCountryFlagSvg(srv.flag, srv.displayName || srv.address)}</span>
          <div class="card-titles">
            <div class="card-name">${escapeHtml(srv.displayName || srv.address)}${isSelected?`<span class="node-selected-label">${state.isConnected?'Подключён':'Выбран'}</span>`:''}</div>
            <div class="card-meta">
              <span class="badge protocol-badge">${escapeHtml(srv.protocol || 'VLESS')}</span>
              <span class="badge transport-badge">${escapeHtml(srv.transport || 'tcp')}</span>
              <span class="server-group">${escapeHtml(srv.subscriptionGroup || 'Основное')}</span>
              <span class="card-address">${escapeHtml(srv.address)}:${srv.port}</span>
            </div>
          </div>
        </div>
        <div class="server-card-right">
          <button class="btn-secondary favorite-button" data-action="favorite" title="Избранное" aria-pressed="${Boolean(srv.isFavorite)}">${srv.isFavorite?'★':'☆'}</button>
          <button class="btn-secondary node-select-button" data-action="select" ${srv.enabled===false?'disabled':''}>${srv.enabled===false?'Отключён':isSelected?(state.isConnected?'Подключён':'Выбран'):'Выбрать'}</button>
          <button class="ping-pill-btn ${pingClass}" data-action="ping" title="Проверить задержку">
            <span class="ping-dot"></span>
            <span>${pingText}</span>
          </button>
          <details class="node-row-actions"><summary class="btn-secondary" aria-label="Действия с сервером">⋯</summary><div class="node-action-items">
          <button class="btn-secondary" data-action="share" title="Ссылка и QR">Ссылка / QR</button>
          <button class="card-btn-edit btn-secondary" data-action="edit" title="Редактировать сервер">Изменить</button>
          <button class="card-btn-delete" data-action="delete" title="Удалить сервер">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" width="13" height="13">
              <polyline points="3 6 5 6 21 6"/>
              <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
            </svg><span>Удалить</span>
          </button>
          </div></details>
        </div>
      `;

      card.addEventListener('click', async (e) => {
        if (e.target.closest('[data-action="favorite"]')) { sendToHost({action:'favoriteServer',id:srv.id}); return; }
        if (e.target.closest('[data-action="share"]')) { sendToHost({action:'shareServer',id:srv.id}); return; }

        const editBtn = e.target.closest('[data-action="edit"]');
        if (editBtn) { e.stopPropagation(); sendToHost({action: 'editServer', id: srv.id}); return; }
        const pingBtn = e.target.closest('[data-action="ping"]');
        const deleteBtn = e.target.closest('[data-action="delete"]');

        if (pingBtn) {
          e.stopPropagation();
          pingBtn.innerHTML = '<span>...</span>';
          sendToHost({ action: 'pingServer', id: srv.id });
          return;
        }

        if (deleteBtn) {
          e.stopPropagation();
          if (await showConfirmation({title: "Удаление сервера", message: `Удалить сервер «${srv.displayName}»?`, acceptLabel: "Удалить"})) {
            sendToHost({ action: 'deleteServer', id: srv.id });
          }
          return;
        }

        if(e.target.closest('.node-row-actions')) return;
        // Selection
        if(srv.enabled !== false) sendToHost({ action: 'selectServer', id: srv.id });
      });

      els.serverListContainer.appendChild(card);
    });
    if(document.getElementById('groupServers').checked){
      const groups=new Map();
      filtered.forEach(s=>{const key=s.subscriptionId||'personal';if(!groups.has(key))groups.set(key,{name:s.subscriptionGroup||'Личные',cards:[]});groups.get(key).cards.push(els.serverListContainer.querySelector(`[data-id="${CSS.escape(s.id)}"]`));});
      const source=document.getElementById('subscriptionSource').value;
      (state.subscriptions||[]).filter(sub=>!state.searchQuery&&sub.enabled===false&&!groups.has(sub.id)&&(source==='Все'||source===sub.id)&&(state.filterType==='all'||(state.filterType==='preset'&&sub.isPreset)||(state.filterType==='subscriptions'&&!sub.isPreset))).forEach(sub=>groups.set(sub.id,{name:sub.name+' · Отключена',cards:[],count:sub.nodeCount,disabled:true}));
      groups.forEach((g,key)=>{const b=document.createElement('button');b.className='server-group-heading';b.textContent=g.name+' · '+(g.count??g.cards.length);if(g.disabled)b.classList.add('disabled-group');const storageKey='splate-group-'+key;let collapsed=false;try{collapsed=localStorage.getItem(storageKey)==='true';}catch(_){};const update=()=>{g.cards.forEach(c=>c.hidden=collapsed);b.setAttribute('aria-expanded',String(!collapsed));};b.onclick=()=>{collapsed=!collapsed;try{localStorage.setItem(storageKey,String(collapsed));}catch(_){}update();};els.serverListContainer.append(b,...g.cards);update();});
    }
  }

  function escapeHtml(str) {
    if (!str) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  // --- Settings Tab Events (Instant Auto-Save) ---
  let autoSaveTimer = null;

  function showAutoSaveStatus(isSaving) {
    if (!els.autoSaveIndicator) return;
    if (isSaving) {
      els.autoSaveIndicator.classList.add('saving');
      if (els.autoSaveText) els.autoSaveText.textContent = 'Сохранение...';
    } else {
      els.autoSaveIndicator.classList.remove('saving');
      if (els.autoSaveText) els.autoSaveText.textContent = 'Настройки синхронизированы';
    }
  }

  function saveCurrentSettings() {
    if (document.getElementById('setAiXboxDns').checked) document.getElementById('setSniffing').checked = true;
    showAutoSaveStatus(true);

    const newSettings = {
      ...window.SplateExtras.collectSettings(),
      theme: els.setTheme.value,
      httpPort: Number(document.getElementById('setHttpPort').value),
      socksPort: Number(document.getElementById('setSocksPort').value),
      customProxyRules: document.getElementById('setProxyRules').value,
      customDirectRules: document.getElementById('setDirectRules').value,
      enableSniffing: document.getElementById('setSniffing').checked,
      enableAiXboxDns: document.getElementById('setAiXboxDns').checked,
      enableAiBypass: document.getElementById('setAiProxy').checked,
      enableTunMode: els.setEnableTun ? els.setEnableTun.checked : false,
      xhttpModeOverride: els.setXhttpMode ? els.setXhttpMode.value : 'auto',
      bufferSize: els.setBufferSize ? els.setBufferSize.value : '4096',
      enableTlsFragment: els.setEnableFragment ? els.setEnableFragment.checked : false,
      dnsProvider: els.setDnsProvider ? els.setDnsProvider.value : 'cloudflare',
      isFakeDns: els.setIsFakeDns ? els.setIsFakeDns.checked : true,
      blockQuic: els.setBlockQuic ? els.setBlockQuic.checked : true,
      blockIpv6: els.setBlockIpv6 ? els.setBlockIpv6.checked : true,
      enableKillSwitch: els.setKillSwitch ? els.setKillSwitch.checked : false,
      enableDnsLeakProtection: els.setDnsLeakProtection ? els.setDnsLeakProtection.checked : false,
      autoStart: els.setAutoStart ? els.setAutoStart.checked : false,
      minimizeToTray: els.setMinimizeToTray ? els.setMinimizeToTray.checked : true,
      autoFallbackFastestServer: els.setAutoFallback ? els.setAutoFallback.checked : false,
      autoUpdateSubscription: els.setAutoUpdateSub ? els.setAutoUpdateSub.checked : false
    };

    if (!Number.isInteger(newSettings.httpPort) || !Number.isInteger(newSettings.socksPort) || newSettings.httpPort < 1 || newSettings.httpPort > 65535 || newSettings.socksPort < 1 || newSettings.socksPort > 65535 || newSettings.httpPort === newSettings.socksPort) {
      els.autoSaveText.textContent = 'Укажите разные порты от 1 до 65535';
      els.autoSaveIndicator.classList.remove('saving');
      return;
    }
    if (!newSettings.enableTunMode || !newSettings.enableSniffing) { newSettings.isFakeDns = false; els.setIsFakeDns.checked = false; }
    window.SplateTheme.apply(newSettings.theme,newSettings.glassTransparency,newSettings.glassBlurEnabled);
    // Update in-memory state
    state.settings = Object.assign(state.settings || {}, newSettings);

    // Sync Home tab toggles instantly
    if (els.toggleTunMode && els.setEnableTun) {
      els.toggleTunMode.checked = els.setEnableTun.checked;
      updateAdapterUI(els.setEnableTun.checked);
    }
    if (els.toggleKillSwitch && els.setKillSwitch) {
      els.toggleKillSwitch.checked = els.setKillSwitch.checked;
    }
    if (els.toggleAutoFallback && els.setAutoFallback) {
      els.toggleAutoFallback.checked = els.setAutoFallback.checked;
    }

    sendToHost({ action: 'saveSettings', settings: newSettings });


  }

  function setupSettingsEvents() {
    [['btnExportConfig', 'exportConfig'], ['btnImportConfig', 'importConfig'], ['btnScanQr', 'scanQr'], ['btnRestartElevated', 'restartElevated']].forEach(([id, action]) => document.getElementById(id).addEventListener('click', () => sendToHost({action})));
    const settingInputs = [
      els.setTheme,
      ...['setHttpPort', 'setSocksPort', 'setProxyRules', 'setDirectRules', 'setSniffing', 'setAiProxy', 'setAiXboxDns'].map(id => document.getElementById(id)),
      els.setEnableTun,
      els.setXhttpMode,
      els.setBufferSize,
      els.setEnableFragment,
      els.setDnsProvider,
      els.setIsFakeDns,
      els.setBlockQuic,
      els.setBlockIpv6,
      els.setKillSwitch,
      els.setDnsLeakProtection,
      els.setAutoStart,
      els.setMinimizeToTray,
      els.setAutoFallback,
      els.setAutoUpdateSub
      ,...document.querySelectorAll('.desktop-setting')
    ];

    settingInputs.forEach((input) => {
      if (!input) return;
      input.addEventListener('change', () => {
        saveCurrentSettings();
      });
    });

    if (els.btnSaveSettings) {
      els.btnSaveSettings.addEventListener('click', () => {
        saveCurrentSettings();
      });
    }
  }

  // --- Logs Tab Events ---
  function setupLogsEvents() {
    document.getElementById('logSearch').addEventListener('input', filterLogs);
    document.getElementById('logLevel').addEventListener('change', filterLogs);
    document.getElementById('logFollow').addEventListener('change', e => {
      if (e.target.checked) els.logsTerminal.scrollTop = els.logsTerminal.scrollHeight;
    });
    els.logsTerminal.addEventListener('scroll', () => {
      if (els.logsTerminal.clientHeight) document.getElementById('logFollow').checked =
        els.logsTerminal.scrollHeight - els.logsTerminal.scrollTop - els.logsTerminal.clientHeight < 32;
    });
    if (els.btnCopyLogs) {
      els.btnCopyLogs.addEventListener('click', () => {
        if (els.logsTerminal) {
          navigator.clipboard.writeText(els.logsTerminal.innerText).then(() => {
            showNotification({title:'Буфер обмена', message:'Журнал скопирован.'});
          }).catch(() => showNotification({title:'Буфер обмена', message:'Не удалось скопировать журнал. Выделите нужный текст и нажмите Ctrl+C.', kind:'warning'}));
        }
      });
    }

    if (els.btnClearLogs) {
      els.btnClearLogs.addEventListener('click', () => {
        if (els.logsTerminal) {
          els.logsTerminal.innerHTML = '<div class="log-entry info">[CLIENT] Журнал очищен.</div>';
        }
      });
    }
  }

  function appendLog(line) {
    if (!els.logsTerminal || !line) return;
    const follow = document.getElementById('logFollow').checked;
    const div = document.createElement('div');
    div.className = 'log-entry';
    if (/error|failed|ошибка/i.test(line)) {
      div.classList.add('error');
    } else if (/warn|предупреждение/i.test(line)) {
      div.classList.add('warn');
    } else if (line.includes('[TUN]') || line.includes('[XRAY]') || line.includes('[CLIENT]')) {
      div.classList.add('info');
    }
    div.textContent = line;
    filterLog(div);
    els.logsTerminal.appendChild(div);

    // Limit maximum lines in DOM
    while (els.logsTerminal.childNodes.length > 250) {
      els.logsTerminal.removeChild(els.logsTerminal.firstChild);
    }
    if (follow) els.logsTerminal.scrollTop = els.logsTerminal.scrollHeight;
  }

  function filterLog(row) {
    const query = document.getElementById('logSearch').value.toLowerCase();
    const level = document.getElementById('logLevel').value;
    row.hidden = !row.textContent.toLowerCase().includes(query) || (level === 'errors' && !row.matches('.error,.warn'));
  }
  function filterLogs() { els.logsTerminal.querySelectorAll('.log-entry').forEach(filterLog); }

  // --- Modal Events ---
  function setupModalEvents() {
    els.addServerInput.addEventListener('input', () => { document.getElementById('subscriptionNameField').hidden = !/^https?:\/\//i.test(els.addServerInput.value.trim()); });
    document.getElementById('btnAiPreset').addEventListener('click', () => {
      els.setEnableTun.checked = true;
      document.getElementById('setSniffing').checked = true;
      document.getElementById('setAiProxy').checked = true;
      els.setDnsProvider.value = 'cloudflare';
      document.getElementById('setAiDns').checked = false;
      document.getElementById('setAiXboxDns').checked = false;
      document.getElementById('setAiMode').value = 'vpn';
      saveCurrentSettings();
      document.getElementById('aiCheckResult').textContent = 'Параметры отправлены на сохранение. Запустите приложение от администратора и переподключитесь, чтобы включить TUN.';
    });
    document.getElementById('btnCheckAi').addEventListener('click', () => {
      document.getElementById('btnCheckAi').disabled = true;
      document.getElementById('aiCheckResult').textContent = 'Проверяем IP выбранного VPN и доступ к Gemini, ChatGPT, Claude по активным маршрутам…';
      sendToHost({action:'checkAiConnectivity'});
    });
    const closeModal = () => {
      if (els.addServerModal) els.addServerModal.close();
      els.btnAddServerDialog.focus();
    };

    els.addServerModal.addEventListener('keydown', e => { if (e.key === 'Enter' && e.ctrlKey) els.btnAddModalConfirm.click(); });
    if (els.btnAddModalClose) els.btnAddModalClose.addEventListener('click', closeModal);
    if (els.btnAddModalCancel) els.btnAddModalCancel.addEventListener('click', closeModal);

    if (els.btnAddModalConfirm) {
      els.btnAddModalConfirm.addEventListener('click', () => {
        const text = els.addServerInput ? els.addServerInput.value.trim() : '';
        if (text) {
          sendToHost({ action: 'addServer', text: text, subscriptionName: document.getElementById('addSubscriptionName').value.trim() });
          closeModal();
        } else {
          showNotification({title:'Добавление сервера', message:'Вставьте ссылку на узел или подписку.', kind:'warning'});
        }
      });
    }
  }

  // --- Speedometer & 60FPS Live Waveform Canvas ---
  let redrawTraffic = () => {};
  function setupSpeedCanvas() {
    const canvas = els.speedCanvas;
    const ctx = canvas?.getContext('2d');
    if (!ctx) return;
    redrawTraffic = () => {
      if (document.hidden || !canvas.getClientRects().length) return;
      const rect = canvas.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      canvas.width = Math.round(rect.width * dpr);
      canvas.height = Math.round(rect.height * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      const palette = getComputedStyle(document.documentElement);
      ctx.strokeStyle = palette.getPropertyValue('--border-subtle').trim();
      ctx.lineWidth = 1;
      for (let y = 15; y < rect.height; y += 22) {
        ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(rect.width, y); ctx.stroke();
      }
      const max = Math.max(...state.downloadBpsHistory, ...state.uploadBpsHistory, 1024);
      drawWave(ctx, state.downloadBpsHistory, max, rect.width, rect.height, palette.getPropertyValue('--accent-cyan').trim(), null, null);
      drawWave(ctx, state.uploadBpsHistory, max, rect.width, rect.height, palette.getPropertyValue('--accent-emerald').trim(), null, null);
    };
    new ResizeObserver(redrawTraffic).observe(canvas);
    new MutationObserver(redrawTraffic).observe(document.documentElement, {attributes:true, attributeFilter:['data-theme']});
    document.addEventListener('visibilitychange', redrawTraffic);
    redrawTraffic();
  }

  function drawWave(ctx, history, maxVal, width, height, strokeColor, fillStart, fillEnd) {
    if (!history || history.length === 0) return;
    const step = width / (history.length - 1);

    ctx.save();
    ctx.beginPath();

    const points = [];
    for (let i = 0; i < history.length; i++) {
      const val = history[i];
      const ratio = Math.min(1.0, Math.max(0.0, val / maxVal));
      // Invert Y coordinate so 0 is at bottom, maxVal near top with 10px margin
      const y = height - (ratio * (height - 18)) - 4;
      const x = i * step;
      points.push({ x, y });
    }

    // Bezier smoothing
    ctx.moveTo(points[0].x, points[0].y);
    for (let i = 0; i < points.length - 1; i++) {
      const xc = (points[i].x + points[i + 1].x) / 2;
      const yc = (points[i].y + points[i + 1].y) / 2;
      ctx.quadraticCurveTo(points[i].x, points[i].y, xc, yc);
    }
    ctx.lineTo(points[points.length - 1].x, points[points.length - 1].y);

    // If gradient fill requested
    if (fillStart && fillEnd) {
      ctx.save();
      const fillPath = new Path2D();
      fillPath.moveTo(points[0].x, points[0].y);
      for (let i = 0; i < points.length - 1; i++) {
        const xc = (points[i].x + points[i + 1].x) / 2;
        const yc = (points[i].y + points[i + 1].y) / 2;
        fillPath.quadraticCurveTo(points[i].x, points[i].y, xc, yc);
      }
      fillPath.lineTo(points[points.length - 1].x, points[points.length - 1].y);
      fillPath.lineTo(width, height);
      fillPath.lineTo(0, height);
      fillPath.closePath();

      const grad = ctx.createLinearGradient(0, 0, 0, height);
      grad.addColorStop(0, fillStart);
      grad.addColorStop(1, fillEnd);
      ctx.fillStyle = grad;
      ctx.fill(fillPath);
      ctx.restore();
    }

    // Stroke
    ctx.strokeStyle = strokeColor;
    ctx.lineWidth = 2.2;
    ctx.shadowColor = strokeColor;
    ctx.shadowBlur = 8;
    ctx.stroke();

    ctx.restore();
  }

  // --- IPC Reception from C# Backend ---
  function setupIPCListener() {
    if (!window.chrome || !window.chrome.webview) {
      console.warn('[IPC] window.chrome.webview not found. Running in standalone browser preview.');
      return;
    }

    window.chrome.webview.addEventListener('message', (event) => {
      let msg = event.data;
      if (typeof msg === 'string') {
        try {
          msg = JSON.parse(msg);
        } catch (e) {
          console.warn('[IPC] Parse error:', e);
          return;
        }
      }
      if (!msg || !msg.type) return;
      window.dispatchEvent(new CustomEvent('splateMessage',{detail:msg}));
      if(msg.type==='subscriptions'){state.subscriptions=msg.payload||[];renderServerList();}
      if(msg.type==='extendedSettings'){
        document.getElementById('groupServers').checked=msg.payload.GroupServers!==false;
        state.sortOrder=msg.payload.ServerSort||'default';state.filterType=msg.payload.ServerFilter||'all';
        document.getElementById('serverSort').value=state.sortOrder;
        els.serverTabs.forEach(t=>t.classList.toggle('active',t.dataset.filter===state.filterType));
        const source=document.getElementById('subscriptionSource');
        if([...source.options].some(o=>o.value===msg.payload.SelectedSubscriptionGroup))source.value=msg.payload.SelectedSubscriptionGroup;
        renderServerList();
      }

      switch (msg.type) {
        case 'state':
          handleStateUpdate(msg.payload);
          break;
        case 'servers':
          handleServersUpdate(msg.payload);
          break;
        case 'serverPing':
          handleServerPing(msg.payload);
          break;
        case 'pingProgress': {
          const {completed, total, busy} = msg.payload;
          state.pingBusy = busy;
          els.btnPingAllServers.disabled = false;
          els.btnPingAllServers.textContent = busy ? `${completed}/${total} · Стоп` : 'Пинг всех';
          els.btnPingAllServers.title = busy ? 'Остановить проверку, сохранив полученные результаты' : 'Проверить доступность узлов';
          break;
        }
        case 'settings':
          handleSettingsUpdate(msg.payload);
          break;
        case 'operation':
          handleOperation(msg.payload);
          break;
        case 'aiConnectivity':
          document.getElementById('btnCheckAi').disabled = false;
          document.getElementById('aiCheckResult').textContent = msg.payload.message;
          break;
        case 'notification':
          showNotification(msg.payload);
          break;
        case 'confirmation':
          showConfirmation(msg.payload).then(accepted => sendToHost({action:'confirmationResult', id:msg.payload.id, accepted}));
          break;
        case 'settingsError':
          showAutoSaveStatus(false);
          els.autoSaveText.textContent = msg.payload.message || 'Не удалось сохранить настройки';
          break;
        case 'traffic':
          handleTrafficUpdate(msg.payload);
          break;
        case 'log':
          appendLog(msg.payload);
          break;
        default:
          console.log('[IPC] Unknown message:', msg);
      }
    });
  }

  function handleOperation({operation, busy}) {
    if (operation === 'pingAll') {
      state.pingBusy = busy;
      els.btnPingAllServers.disabled = false;
      els.btnPingAllServers.setAttribute('aria-busy', String(busy));
      if (!busy) els.btnPingAllServers.textContent = 'Пинг всех';
      return;
    }
    const controls = {autoSelect:['btnAutoSelect','Найти лучший','Проверяем…'], pingAll:['btnPingAllServers','Пинг всех','Проверяем…'],
      updateSubscriptions:['btnUpdateSubs','Обновить','Обновляем…'], importConfig:['btnImportConfig','Импорт','Импорт…'],
      exportConfig:['btnExportConfig','Экспорт','Экспорт…'], scanQr:['btnScanQr','QR из файла','Чтение…'],addServer:['btnAddServerDialog','Добавить','Импорт…']};
    const control = controls[operation];
    if (!control) return;
    const button = document.getElementById(control[0]);
    button.disabled = busy;
    button.textContent = busy ? control[2] : control[1];
    button.setAttribute('aria-busy', String(busy));
  }

  // --- State Updates Handler ---
  function handleStateUpdate(data) {
    if (!data) return;

    state.isConnected = Boolean(data.isConnected);
    state.activeTunMode = Boolean(data.activeTunMode);
    updateAdapterUI(state.isConnected ? state.activeTunMode : state.settings.enableTunMode);
    state.isConnecting = Boolean(data.isConnecting);
    state.statusText = data.statusText || (state.isConnected ? 'ПОДКЛЮЧЕНО' : 'ОТКЛЮЧЕНО');
    state.statusDetail = data.statusDetail || (state.isConnected ? 'Защищённый туннель активен' : 'Туннель не активен · Нажмите для защиты');
    state.sessionDuration = data.sessionDuration || '00:00:00';
    state.currentIp = data.currentIp || 'Определение...';
    state.currentLocation = data.currentLocation || '';
    state.rtt = data.rtt !== undefined ? data.rtt : -1;

    const routeWarning = state.isConnected && /ОШИБКА|НЕ ПОДТВЕРЖДЕНА/.test(state.statusText);
    const visualConnected = state.isConnected && !routeWarning;
    // Header updates
    if (els.headerStatusLabel) els.headerStatusLabel.textContent = state.statusText;
    if (els.headerStatusDot) {
      els.headerStatusDot.className = 'pulse-dot ' + (visualConnected ? 'state-connected' : (state.isConnecting || routeWarning ? 'state-connecting' : 'state-disconnected'));
    }
    if (els.headerPingVal) {
      els.headerPingVal.textContent = state.rtt >= 0 ? `${state.rtt} ms` : (state.isConnected ? '...' : '—');
    }

    // Power Core state classes
    if (els.btnPowerCore) {
      els.btnPowerCore.className = 'core-button ' + (visualConnected ? 'state-connected' : (state.isConnecting || routeWarning ? 'state-connecting' : 'state-disconnected'));
      const coreWrapper = els.btnPowerCore.closest('.power-core-wrapper');
      if (coreWrapper) {
        coreWrapper.className = 'power-core-wrapper ' + (visualConnected ? 'connected' : (state.isConnecting || routeWarning ? 'connecting' : 'disconnected'));
      }
    }
    const headerIndicator = document.getElementById('headerStatusIndicator');
    if (headerIndicator) {
      headerIndicator.className = 'status-indicator ' + (visualConnected ? 'connected' : (state.isConnecting || routeWarning ? 'connecting' : 'disconnected'));
    }
    if (els.coreStatusText) {
      els.coreStatusText.textContent = state.statusText;
      els.coreStatusText.className = 'core-status-headline ' + (visualConnected ? 'color-emerald' : (state.isConnecting || routeWarning ? 'color-cyan' : ''));
    }
    if (els.coreStatusDetail) els.coreStatusDetail.textContent = state.statusDetail;

    // Connect Button
    if (els.btnToggleConnection) {
      if (state.isConnected) {
        els.btnToggleConnection.className = 'btn-primary-action btn-disconnect';
        if (els.btnToggleConnectionText) els.btnToggleConnectionText.textContent = 'Отключиться';
      } else if (state.isConnecting) {
        els.btnToggleConnection.className = 'btn-primary-action btn-connecting';
        if (els.btnToggleConnectionText) els.btnToggleConnectionText.textContent = 'Отменить подключение';
      } else {
        els.btnToggleConnection.className = 'btn-primary-action btn-connect';
        if (els.btnToggleConnectionText) els.btnToggleConnectionText.textContent = 'Подключиться';
      }
    }

    document.getElementById('homeAiRoute').textContent = !state.isConnected ? 'Применится при подключении' :
      (data.activeAiDns ? 'Нейросети · '+(data.activeAiDnsLabel||'SmartDNS') : data.activeAiMode==='vpn' ? 'Нейросети · VPN' : 'Нейросети · обычные правила');
    // Session stats
    if (els.sessionDurationVal) els.sessionDurationVal.textContent = state.sessionDuration;
    if (els.currentIpVal) els.currentIpVal.textContent = state.currentIp;
    if (els.currentLocationVal) els.currentLocationVal.textContent = state.currentLocation;

    // Selected Server Card
    if (data.selectedServer) {
      state.selectedServer = data.selectedServer;
      updateSelectedServerCard(data.selectedServer);
      updateSelectionMarker();
    } else { state.selectedServer = null; els.microProtocol.textContent = "—"; els.selectedServerFlag.innerHTML = getCountryFlagSvg("", ""); els.selectedServerPingText.textContent = "—"; els.selectedServerName.textContent = "Добавьте сервер"; els.selectedServerProtocol.textContent = "—"; els.selectedServerTransport.textContent = "—"; els.selectedServerGroup.textContent = ""; updateSelectionMarker(); }
  }

  function updateSelectionMarker() {
    if (!els.serverListContainer) return;
    document.getElementById('nodeSelectionContext').textContent=state.selectedServer ? `${state.isConnected?'Подключён':'Выбран'}: ${state.selectedServer.displayName||state.selectedServer.address}` : 'Сервер не выбран · выберите узел в списке';
    document.getElementById('btnRevealSelected').hidden=!state.selectedServer;
    els.serverListContainer.querySelectorAll('.server-card').forEach(card => {
      const selected = Boolean(state.selectedServer && card.dataset.id === state.selectedServer.id);
      card.classList.toggle('selected', selected);
      card.setAttribute('aria-selected', String(selected));
      const choose=card.querySelector('[data-action="select"]');
      if(choose&&!choose.disabled)choose.textContent=selected?(state.isConnected?'Подключён':'Выбран'):'Выбрать';
      const name=card.querySelector('.card-name');let marker=name.querySelector('.node-selected-label');
      if(selected){if(!marker){marker=document.createElement('span');marker.className='node-selected-label';name.append(marker);}marker.textContent=state.isConnected?'Подключён':'Выбран';}
      else marker?.remove();
    });
  }

  function updateSelectedServerCard(srv) {
    if (!srv) return;
    if (els.selectedServerFlag) els.selectedServerFlag.innerHTML = getCountryFlagSvg(srv.flag, srv.displayName || srv.address);
    if (els.selectedServerName) els.selectedServerName.textContent = srv.displayName || srv.address;
    if (els.selectedServerProtocol) els.selectedServerProtocol.textContent = srv.protocol || 'VLESS';
    if (els.selectedServerTransport) els.selectedServerTransport.textContent = (srv.transport || 'tcp') + (srv.security ? ` ${srv.security}` : '');
    if (els.selectedServerGroup) els.selectedServerGroup.textContent = srv.subscriptionGroup || 'Основное';
    if (els.microProtocol) els.microProtocol.textContent = srv.protocol || 'VLESS';

    if (els.selectedServerPingText) {
      els.selectedServerPingText.textContent = srv.ping > 0 ? `${srv.ping} ms` : '—';
    }
  }

  // --- Servers List Update ---
  function handleServersUpdate(servers) {
    if (!Array.isArray(servers)) return;
    state.servers = servers;
    renderServerList();
  }

  function handleServerPing(data) {
    if (!data || !data.id) return;
    const srv = state.servers.find((s) => s.id === data.id);
    if (srv) {
      srv.ping = data.ping;
    }
    if(state.sortOrder==='ping'){if(!state.renderQueued){state.renderQueued=true;requestAnimationFrame(()=>{state.renderQueued=false;renderServerList();});}return;}
    if (state.selectedServer && state.selectedServer.id === data.id) {
      state.selectedServer.ping = data.ping;
      if (els.selectedServerPingText) {
        els.selectedServerPingText.textContent = data.ping > 0 ? `${data.ping} ms` : '—';
      }
    }

    if (els.serverListContainer) {
      const card = els.serverListContainer.querySelector(`.server-card[data-id="${CSS.escape(data.id)}"]`);
      if (card) {
        const pingBtn = card.querySelector('[data-action="ping"]');
        if (pingBtn) {
          let pingClass = 'ping-gray';
          let pingText = data.ping < 0 ? 'Нет ответа' : '—';
          if (data.ping > 0) {
            pingText = `${data.ping} ms`;
            if (data.ping < 120) pingClass = 'ping-green';
            else if (data.ping < 280) pingClass = 'ping-yellow';
            else pingClass = 'ping-red';
          }
          pingBtn.className = `ping-pill-btn ${pingClass}`;
          pingBtn.innerHTML = `<span class="ping-dot"></span><span>${pingText}</span>`;
        }
      }
    }
  }

  // --- Settings Update ---
  function handleSettingsUpdate(settings) {
    if (!settings) return;
    state.settings = Object.assign(state.settings, settings);
    showAutoSaveStatus(false);
    els.setTheme.value = settings.theme || 'dark';
    window.SplateTheme.apply(settings.theme || 'dark');
    document.getElementById('setHttpPort').value = settings.httpPort || 10809;
    document.getElementById('setSocksPort').value = settings.socksPort || 10808;
    document.getElementById('setProxyRules').value = settings.customProxyRules || '';
    document.getElementById('setDirectRules').value = settings.customDirectRules || '';
    document.getElementById('setSniffing').checked = Boolean(settings.enableSniffing);
    document.getElementById('setAiProxy').checked = Boolean(settings.enableAiBypass);
    document.getElementById('setAiXboxDns').checked = Boolean(settings.enableAiXboxDns);
    document.getElementById('adminStatus').textContent = settings.isAdministrator ? 'Приложение запущено от администратора' : 'Обычный запуск · для TUN нужны права администратора';
    document.getElementById('btnRestartElevated').hidden = Boolean(settings.isAdministrator);
    els.setIsFakeDns.disabled = !settings.enableTunMode || !settings.enableSniffing;

    if (els.setEnableTun) els.setEnableTun.checked = Boolean(settings.enableTunMode);
    if (els.toggleTunMode) els.toggleTunMode.checked = Boolean(settings.enableTunMode);
    updateAdapterUI(Boolean(settings.enableTunMode));

    if (els.toggleSmartRu) els.toggleSmartRu.checked = (settings.routingMode === 'smart_ru');
    if (els.setXhttpMode && settings.xhttpModeOverride) els.setXhttpMode.value = settings.xhttpModeOverride;
    if (els.setBufferSize && settings.bufferSize) els.setBufferSize.value = String(settings.bufferSize);
    if (els.setEnableFragment) els.setEnableFragment.checked = Boolean(settings.enableTlsFragment);
    if (els.setDnsProvider && settings.dnsProvider) els.setDnsProvider.value = settings.dnsProvider;
    if (els.setIsFakeDns) els.setIsFakeDns.checked = Boolean(settings.isFakeDns);
    if (els.setBlockQuic) els.setBlockQuic.checked = Boolean(settings.blockQuic);
    if (els.setBlockIpv6) els.setBlockIpv6.checked = Boolean(settings.blockIpv6);

    // New settings
    if (els.setKillSwitch) els.setKillSwitch.checked = Boolean(settings.enableKillSwitch);
    if (els.toggleKillSwitch) els.toggleKillSwitch.checked = Boolean(settings.enableKillSwitch);
    if (els.setDnsLeakProtection) els.setDnsLeakProtection.checked = Boolean(settings.enableDnsLeakProtection);
    if (els.setAutoStart) els.setAutoStart.checked = Boolean(settings.autoStart);
    if (els.setMinimizeToTray) els.setMinimizeToTray.checked = Boolean(settings.minimizeToTray);
    if (els.setAutoFallback) els.setAutoFallback.checked = Boolean(settings.autoFallbackFastestServer);
    if (els.toggleAutoFallback) els.toggleAutoFallback.checked = Boolean(settings.autoFallbackFastestServer);
    if (els.setAutoUpdateSub) els.setAutoUpdateSub.checked = Boolean(settings.autoUpdateSubscription);

    // DNS label update
    if (els.microDns) {
      const dnsLabels = {
        cloudflare: 'Cloudflare', google: 'Google DNS', quad9: 'Quad9',
        adguard: 'AdGuard', xbox: 'Xbox DNS', yandex: 'Yandex'
      };
      const fakePart = Boolean(settings.isFakeDns) ? ' · FakeDNS' : '';
      els.microDns.textContent = (dnsLabels[settings.dnsProvider] || 'DNS') + fakePart;
    }
  }

  // --- Traffic Update ---
  function handleTrafficUpdate(data) {
    if (!data) return;

    // Formatted strings: "1.2 MB/s" -> split number and unit
    if (data.downloadSpeed) {
      const parts = data.downloadSpeed.split(' ');
      if (els.liveDownloadVal) els.liveDownloadVal.textContent = parts[0] || '0.0';
      if (els.liveDownloadUnit) els.liveDownloadUnit.textContent = parts[1] || 'Мбит/с';
    }

    if (data.uploadSpeed) {
      const parts = data.uploadSpeed.split(' ');
      if (els.liveUploadVal) els.liveUploadVal.textContent = parts[0] || '0.0';
      if (els.liveUploadUnit) els.liveUploadUnit.textContent = parts[1] || 'Кбит/с';
    }

    if (data.sessionDownloaded && els.sessionDataVal) {
      els.sessionDataVal.textContent = data.sessionDownloaded;
    }

    // Raw bytes/sec for canvas waveform
    const downBps = typeof data.downloadBps === 'number' ? data.downloadBps : 0;
    const upBps = typeof data.uploadBps === 'number' ? data.uploadBps : 0;

    state.downloadBpsHistory.shift();
    state.downloadBpsHistory.push(downBps);

    state.uploadBpsHistory.shift();
    state.uploadBpsHistory.push(upBps);
    redrawTraffic();
  }

  // Run on DOM loaded
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

})();
