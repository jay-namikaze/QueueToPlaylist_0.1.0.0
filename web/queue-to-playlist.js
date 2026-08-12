/*
 * Queue to Playlist companion for Jellyfin 10.11.x
 *
 * Install with the JS Injector plugin (or add to a custom jellyfin-web build). The server DLL
 * supplies the QueueToPlaylist endpoints; this file only adds the client-side controls and the
 * dice/wheel presentation.
 */
(() => {
  'use strict';

  const PLUGIN_ROOT = 'QueueToPlaylist';
  const PANEL_ID = 'qtp-controls';
  const OVERLAY_ID = 'qtp-picker-overlay';
  const api = () => window.ApiClient;

  function request(path, options = {}) {
    const client = api();
    const headers = Object.assign({
      'X-Emby-Token': client.accessToken(),
      'Content-Type': 'application/json'
    }, options.headers || {});
    return fetch(client.getUrl(`${PLUGIN_ROOT}/${path}`), Object.assign({}, options, { headers }))
      .then(response => {
        if (!response.ok) throw new Error(`Queue to Playlist request failed (${response.status})`);
        return response.status === 204 ? null : response.json();
      });
  }

  function play(items, startIndex = 0) {
    const playable = items.map(item => item.Dto || item.dto || item).filter(item => item && (item.Id || item.id));
    if (!playable.length) return;
    if (window.playbackManager && typeof window.playbackManager.play === 'function') {
      // Jellyfin Web 10.11's PlaybackManager accepts DTOs directly. Passing DTOs avoids the
      // serverId requirement used by the ids-only overload and preserves the complete queue.
      window.playbackManager.play({ items: playable, startIndex });
      return;
    }
    // A useful fallback for older web clients: open the selected item's detail page.
    const itemId = playable[startIndex]?.Id || playable[0].Id;
    window.location.hash = `#!/details?id=${encodeURIComponent(itemId)}`;
  }

  function imageUrl(id) {
    const client = api();
    return client.getUrl(`Items/${id}/Images/Primary?maxWidth=480&quality=85`);
  }

  function getSelectedPlaylistId(select) {
    return select && select.value ? select.value : null;
  }

  async function loadPlaylists(select) {
    const playlists = await request('playlists');
    select.innerHTML = playlists.length
      ? playlists.map(p => `<option value="${p.Id}">${escapeHtml(p.Name)} (${p.Count})</option>`).join('')
      : '<option value="">No video playlists</option>';
  }

  async function runPlaylist(mode, select) {
    const playlistId = getSelectedPlaylistId(select);
    if (!playlistId) return;
    const current = window.playbackManager?.currentItem?.();
    const exclude = mode === 'randomizer' && current?.Id ? `&excludeId=${encodeURIComponent(current.Id)}` : '';
    const plan = await request(`playlists/${playlistId}/queue?mode=${encodeURIComponent(mode)}${exclude}`);
    renderNext(plan);
    play(plan.Items, 0);
  }

  async function persistShuffle(select) {
    const playlistId = getSelectedPlaylistId(select);
    if (!playlistId) return;
    const plan = await request(`playlists/${playlistId}/shuffle`, { method: 'POST' });
    renderNext(plan);
    play(plan.Items, 0);
  }

  function renderNext(plan) {
    const list = document.querySelector(`#${PANEL_ID} .qtp-next-list`);
    if (!list) return;
    list.innerHTML = plan.Items.slice(0, 6).map((item, index) =>
      `<li><span>${index + 1}</span><img src="${imageUrl(item.Id)}" alt="" /><strong>${escapeHtml(item.Name)}</strong></li>`).join('');
  }

  async function showPicker(mode) {
    closePicker();
    const overlay = document.createElement('div');
    overlay.id = OVERLAY_ID;
    overlay.innerHTML = `
      <div class="qtp-picker-card" role="dialog" aria-label="What should I watch?">
        <button class="qtp-close" title="Close">×</button>
        <div class="qtp-eyebrow">QUEUE TO PLAYLIST</div>
        <h2>What should I watch?</h2>
        <div class="qtp-stage ${mode === 'wheel' ? 'qtp-wheel-stage' : 'qtp-dice-stage'}">
          <div class="qtp-spinner">${mode === 'wheel' ? '◌' : '⚄'}</div>
          <div class="qtp-result"></div>
        </div>
        <div class="qtp-picker-actions">
          <button class="qtp-roll">${mode === 'wheel' ? 'Spin again' : 'Roll again'}</button>
          <button class="qtp-play" disabled>Play this</button>
        </div>
      </div>`;
    document.body.appendChild(overlay);
    overlay.querySelector('.qtp-close').onclick = closePicker;
    overlay.addEventListener('click', event => { if (event.target === overlay) closePicker(); });
    const roll = () => rollPicker(overlay, mode);
    overlay.querySelector('.qtp-roll').onclick = roll;
    overlay.querySelector('.qtp-play').onclick = () => {
      const selected = overlay._selected;
      if (selected) play([selected]);
    };
    await rollPicker(overlay, mode);
  }

  async function rollPicker(overlay, mode) {
    const spinner = overlay.querySelector('.qtp-spinner');
    const result = overlay.querySelector('.qtp-result');
    const playButton = overlay.querySelector('.qtp-play');
    playButton.disabled = true;
    spinner.classList.remove('qtp-spin');
    void spinner.offsetWidth;
    spinner.classList.add('qtp-spin');
    try {
      const picker = await request(`picker?mode=${encodeURIComponent(mode)}&count=10`);
      const selected = picker.Selected;
      overlay._selected = selected;
      result.innerHTML = `
        <img src="${imageUrl(selected.Id)}" alt="" />
        <div class="qtp-result-copy"><strong>${escapeHtml(selected.Name)}</strong><span>${escapeHtml(selected.Kind)}</span></div>`;
      playButton.disabled = false;
    } catch (error) {
      result.textContent = error.message;
    }
  }

  function closePicker() {
    document.getElementById(OVERLAY_ID)?.remove();
  }

  function createPanel() {
    if (document.getElementById(PANEL_ID) || !api()) return;
    const panel = document.createElement('section');
    panel.id = PANEL_ID;
    panel.innerHTML = `
      <div class="qtp-title">Next up, your way</div>
      <div class="qtp-row"><select class="qtp-playlists" aria-label="Video playlist"></select></div>
      <div class="qtp-row qtp-buttons">
        <button class="qtp-action qtp-shuffle">Shuffle &amp; play</button>
        <button class="qtp-action qtp-randomize">🎲 Randomizer</button>
      </div>
      <div class="qtp-row qtp-buttons">
        <button class="qtp-action qtp-dice">🎲 What should I watch?</button>
        <button class="qtp-action qtp-wheel">◌ Spin the wheel</button>
      </div>`;
    panel.insertAdjacentHTML('beforeend', '<div class="qtp-next-heading">Next up</div><ol class="qtp-next-list"></ol>');
    document.body.appendChild(panel);
    const select = panel.querySelector('.qtp-playlists');
    loadPlaylists(select).then(() => {
      select.onchange = () => request(`playlists/${select.value}/queue?mode=ordered`).then(renderNext).catch(showError);
      if (select.value) return request(`playlists/${select.value}/queue?mode=ordered`).then(renderNext);
      return null;
    }).catch(() => { select.innerHTML = '<option>Plugin API unavailable</option>'; });
    panel.querySelector('.qtp-shuffle').onclick = () => persistShuffle(select).catch(showError);
    panel.querySelector('.qtp-randomize').onclick = () => runPlaylist('randomizer', select).catch(showError);
    panel.querySelector('.qtp-dice').onclick = () => showPicker('dice').catch(showError);
    panel.querySelector('.qtp-wheel').onclick = () => showPicker('wheel').catch(showError);
  }

  function showError(error) { console.error('[Queue to Playlist]', error); }
  function escapeHtml(value) {
    return String(value ?? '').replace(/[&<>'"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[character]));
  }

  // Jellyfin is a single-page app; a lightweight observer keeps the controls available after route changes.
  const start = () => { createPanel(); setInterval(createPanel, 1500); };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true }); else start();
})();
