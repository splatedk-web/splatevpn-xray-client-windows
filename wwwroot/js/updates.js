(function () {
  'use strict';
  const $ = id => document.getElementById(id);
  const send = action => window.chrome?.webview?.postMessage({action});
  document.querySelector('#tab-home .page-heading').insertAdjacentHTML('afterend', `
    <div class="card app-update-banner" id="appUpdateBanner" role="status" hidden>
      <div><strong id="appUpdateHeadline"></strong><p>Обновите SplateVPN, чтобы получить исправления и новые возможности.</p></div>
      <button id="btnAppUpdateRelease" class="btn-secondary">Скачать обновление</button>
    </div>`);
  document.querySelector('#tab-settings .settings-grid').insertAdjacentHTML('beforeend', `
    <div class="card settings-section" id="appUpdateSettings">
      <div class="settings-sec-header"><h3>Обновления приложения</h3></div>
      <p class="settings-help">Установленная версия: <strong id="appCurrentVersion">—</strong></p>
      <p class="settings-help">Проверка при запуске. Кнопка скачивания открывает страницу релиза на GitHub.</p>
      <p class="settings-help" id="appUpdateStatus" role="status">Проверка ещё не выполнялась.</p>
      <div class="artifact-actions"><button id="btnCheckAppUpdates" class="btn-secondary">Проверить обновления</button><button id="btnSettingsAppRelease" class="btn-secondary" hidden>Скачать обновление</button></div>
    </div>`);
  $('btnCheckAppUpdates').onclick = () => send('checkAppUpdates');
  $('btnAppUpdateRelease').onclick = $('btnSettingsAppRelease').onclick = () => send('openAppRelease');
  window.addEventListener('splateMessage', event => {
    if (event.detail.type !== 'appUpdate') return;
    const state = event.detail.payload;
    $('appCurrentVersion').textContent = state.currentVersion || '—';
    $('appUpdateStatus').textContent = state.status || 'Проверка ещё не выполнялась.';
    $('btnCheckAppUpdates').disabled = !!state.checking;
    $('btnCheckAppUpdates').textContent = state.checking ? 'Проверка…' : 'Проверить обновления';
    const available = !!state.available && !!state.latestVersion;
    $('appUpdateBanner').hidden = !available;
    $('btnSettingsAppRelease').hidden = !available;
    $('appUpdateHeadline').textContent = available ? `Доступна версия ${state.latestVersion}` : '';
    document.querySelector('.version-label').textContent = `SplateVPN Desktop v${state.currentVersion || '—'}`;
  });
})();
