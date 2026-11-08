// Scripts
async function loadScripts() {
  state.scripts = await api(`/api/scripts?q=${encodeURIComponent($('#script-search').value.trim())}`);
  renderScriptList();
  if (!state.maps.length) state.maps = await api('/api/maps');
  fillScriptMapSelect();
  await loadScriptMapNpcs();
}
function renderScriptList() {
  const host = $('#script-list');
  host.innerHTML = state.scripts.map(s => `
    <button class="script-item ${state.selectedScript?.name === s.name ? 'active' : ''}" data-name="${encodeURIComponent(s.name)}">
      <b>${esc(s.name)}</b><span>${s.functions?.length || 0} functions · ${esc(s.encoding)}</span>
    </button>`).join('');
  $$('.script-item', host).forEach(x => x.onclick = () => openScript(decodeURIComponent(x.dataset.name)));
}
async function openScript(name) {
  try {
    state.selectedScript = await api(`/api/scripts/${encodeURIComponent(name)}`);
    renderScriptList();
    const s = state.selectedScript;
    $('#script-title').textContent = s.name;
    $('#script-encoding').textContent = s.encoding;
    $('#script-editor').disabled = false;
    $('#script-editor').value = s.content;
    $('#script-save').disabled = false;
    $('#script-validate').disabled = false;
    $('#script-delete').disabled = false;
    $('#script-snippet').disabled = false;
    renderValidation(s.validation);
    renderFunctions(s.functions);
    updateCursor();
  } catch (e) { toast(e.message, 'error'); }
}
function newScript() {
  const name = prompt('新 NPC 腳本檔名（例如 MyNpc.txt）');
  if (!name) return;
  state.selectedScript = {
    name:name.endsWith('.txt') ? name : `${name}.txt`,
    content:'function start()\n\tSay("n1Hello", "End")\nend\n',
    encoding:'utf-8', functions:['start'], validation:null, _new:true
  };
  $('#script-title').textContent = state.selectedScript.name;
  $('#script-encoding').textContent = 'new / utf-8';
  $('#script-editor').disabled = false;
  $('#script-editor').value = state.selectedScript.content;
  $('#script-save').disabled = false;
  $('#script-validate').disabled = false;
  $('#script-delete').disabled = true;
  $('#script-snippet').disabled = false;
  renderValidation(null);
  renderFunctions(['start']);
}
const SCRIPT_SNIPPETS = {
  say: '\tSay("n1對話內容", "End")',
  ask: '\tAsk("n1請選擇", "選項一", "選項二", "ChoiceNext")',
  function: '\nfunction ChoiceNext()\n\tSay("n1完成", "End")\nend\n',
};
function insertScriptSnippet(kind) {
  if (!kind || !state.selectedScript) return;
  const text = SCRIPT_SNIPPETS[kind];
  if (!text) return;
  const editor = $('#script-editor');
  const start = editor.selectionStart;
  const end = editor.selectionEnd;
  editor.value = editor.value.slice(0, start) + text + editor.value.slice(end);
  editor.focus();
  editor.setSelectionRange(start + text.length, start + text.length);
  state.selectedScript.content = editor.value;
  $('#script-snippet').value = '';
  validateScript();
  updateCursor();
}
async function validateScript() {
  if (!state.selectedScript) return;
  try {
    const v = await api('/api/scripts/validate', {
      method:'POST',
      body:{name:state.selectedScript.name, content:$('#script-editor').value}
    });
    renderValidation(v);
    renderFunctions(v.functions || []);
    return v;
  } catch (e) { toast(e.message, 'error'); }
}
function renderValidation(v) {
  const host = $('#script-validation');
  if (!v) {
    host.className = 'validation-box muted';
    host.textContent = '尚未驗證';
    return;
  }
  host.className = `validation-box ${v.ok ? 'good' : 'bad'}`;
  host.innerHTML = `
    <b>${v.ok ? '✓ 基本靜態檢查通過' : '✕ 發現錯誤'}</b>
    ${(v.errors || []).map(x => `<div class="validation-message">${esc(x)}</div>`).join('')}
    ${(v.warnings || []).map(x => `<div class="validation-message warn">⚠ ${esc(x)}</div>`).join('')}`;
}
function renderFunctions(funcs) {
  $('#script-functions').innerHTML = funcs?.length
    ? funcs.map(f => `<button class="function-chip" data-f="${esc(f)}">${esc(f)}()</button>`).join('')
    : '<span class="muted">—</span>';
  $$('.function-chip').forEach(b => b.onclick = () => jumpFunction(b.dataset.f));
}
function jumpFunction(name) {
  const ed = $('#script-editor');
  const re = new RegExp(`(^|\\n)\\s*function\\s+${name}\\s*\\(`);
  const m = re.exec(ed.value);
  if (!m) return;
  ed.focus();
  ed.setSelectionRange(m.index, m.index);
  updateCursor();
}
async function saveScript() {
  if (!state.selectedScript) return;
  try {
    const v = await validateScript();
    if (v && !v.ok) {
      toast('腳本有錯誤，已阻止儲存', 'error');
      return;
    }
    const name = state.selectedScript.name;
    state.selectedScript = await api(`/api/scripts/${encodeURIComponent(name)}`, {
      method:'PUT',
      body:{content:$('#script-editor').value}
    });
    toast('NPC 腳本已儲存');
    await loadScripts();
    await loadStatus();
  } catch (e) { toast(e.message, 'error'); }
}
async function deleteScript() {
  if (!state.selectedScript || state.selectedScript._new) return;
  const name = state.selectedScript.name;
  if (!confirm(`確定刪除 NPC 腳本 ${name}？原檔會先備份。`)) return;
  try {
    await api(`/api/scripts/${encodeURIComponent(name)}`, {method:'DELETE'});
    state.selectedScript = null;
    $('#script-title').textContent = '尚未選擇腳本';
    $('#script-encoding').textContent = '';
    $('#script-editor').value = '';
    $('#script-editor').disabled = true;
    $('#script-save').disabled = true;
    $('#script-validate').disabled = true;
    $('#script-delete').disabled = true;
    $('#script-snippet').disabled = true;
    renderValidation(null);
    renderFunctions([]);
    await loadScripts();
    await loadStatus();
    toast('NPC 腳本已刪除');
  } catch (e) { toast(e.message, 'error'); }
}
function updateCursor() {
  const ed = $('#script-editor');
  const p = ed.selectionStart;
  const before = ed.value.slice(0, p).split('\n');
  $('#cursor-status').textContent = `Ln ${before.length}, Col ${before.at(-1).length + 1}`;
}
function fillScriptMapSelect() {
  const sel = $('#script-map-select');
  const current = sel.value;
  sel.innerHTML = '<option value="">選擇地圖看 NPC</option>' + state.maps.map(m => `<option value="${m.id}">${esc(m.name)}</option>`).join('');
  if (current && state.maps.some(m => String(m.id) === current)) sel.value = current;
}
async function loadScriptMapNpcs() {
  const id = $('#script-map-select').value;
  if (!id) {
    $('#script-map-npcs').innerHTML = '<span class="muted">選地圖後可由 NPC 名稱直接跳到 Script。</span>';
    return;
  }
  try {
    const rows = await api(`/api/maps/${id}/npcs`);
    $('#script-map-npcs').innerHTML = rows.map(n => `
      <div class="npc-link" data-script="${encodeURIComponent(n.script)}">
        <b>${esc(n.name)}</b><span>${esc(n.script)} · (${n.x}, ${n.y})</span>
      </div>`).join('') || '<span class="muted">此地圖沒有 NPC。</span>';
    $$('.npc-link').forEach(x => x.onclick = () => openScript(decodeURIComponent(x.dataset.script)));
  } catch (e) { toast(e.message, 'error'); }
}

// Assets
async function loadAssets() {
  const folder = $('#asset-folder').value || '';
  const q = $('#asset-search').value.trim();
  state.assets = await api(`/api/assets?folder=${encodeURIComponent(folder)}&q=${encodeURIComponent(q)}&limit=500`);
  $('#asset-count').textContent = `${state.assets.length} files`;
  renderAssets();
}
function renderAssets() {
  const host = $('#asset-grid');
  host.innerHTML = state.assets.map((a,i) => `
    <article class="asset-card" data-asset-index="${i}">
      <div class="asset-preview">
        ${a.previewable
          ? `<img loading="lazy" src="/api/assets/file?path=${encodeURIComponent(a.path)}" alt="${esc(a.name)}">`
          : `<span class="file-ext">${esc(a.extension || 'FILE').replace('.','').toUpperCase()}</span>`}
        <div class="drop-overlay">放開即可替換</div>
      </div>
      <div class="asset-info">
        <b title="${esc(a.name)}">${esc(a.name)}</b>
        <span title="${esc(a.path)}">${esc(a.path)}</span>
        <span>${formatBytes(a.size)}</span>
      </div>
      <div class="asset-actions"><button class="button ghost replace-asset" data-i="${i}">選檔替換</button></div>
    </article>`).join('') || '<div class="empty-state"><h3>沒有符合的素材</h3></div>';

  $$('.replace-asset', host).forEach(b => b.onclick = () => chooseAsset(Number(b.dataset.i)));
  $$('.asset-card', host).forEach(card => {
    const index = Number(card.dataset.assetIndex);
    card.addEventListener('dragover', e => {
      e.preventDefault();
      card.classList.add('drag-over');
    });
    card.addEventListener('dragleave', e => {
      if (!card.contains(e.relatedTarget)) card.classList.remove('drag-over');
    });
    card.addEventListener('drop', e => {
      e.preventDefault();
      card.classList.remove('drag-over');
      const file = e.dataTransfer?.files?.[0];
      if (!file) return;
      state.assetTarget = state.assets[index];
      replaceAsset(file);
    });
  });
}
function chooseAsset(i) {
  state.assetTarget = state.assets[i];
  const input = $('#asset-file-input');
  input.accept = state.assetTarget.extension || '';
  input.value = '';
  input.click();
}
async function replaceAsset(file) {
  if (!state.assetTarget || !file) return;
  if (file.size > 25 * 1024 * 1024) return toast('素材不可超過 25 MB', 'error');
  const targetExt = String(state.assetTarget.extension || '').toLowerCase().replace('.jpeg', '.jpg');
  const sourceExt = (file.name.match(/\.[^.]+$/)?.[0] || '').toLowerCase().replace('.jpeg', '.jpg');
  if (targetExt !== sourceExt) {
    return toast(`素材格式不一致：目標 ${state.assetTarget.extension || '未知'}，來源 ${sourceExt || '無副檔名'}`, 'error');
  }
  if (!confirm(`用 ${file.name} 替換\n${state.assetTarget.path}\n\n原檔會先備份。`)) return;
  try {
    const data = await fileToBase64(file);
    await api('/api/assets', {method:'PUT', body:{path:state.assetTarget.path, contentBase64:data, sourceName:file.name}});
    toast('素材已替換');
    await loadAssets();
  } catch (e) { toast(e.message, 'error'); }
}
function fileToBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(',')[1] || '');
    reader.onerror = reject;
    reader.readAsDataURL(file);
  });
}
function formatBytes(n) {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  return `${(n / 1024 / 1024).toFixed(1)} MB`;
}

function emptyEditor(icon, title, desc) {
  return `<div class="empty-state"><div class="empty-icon">${icon}</div><h3>${title}</h3><p>${desc}</p></div>`;
}

// Events
$$('.nav-item').forEach(b => b.onclick = () => go(b.dataset.view));
$$('[data-go]').forEach(b => b.onclick = () => go(b.dataset.go));
$('#refresh-view').onclick = async () => {
  if (state.view === 'maps' && state.mapDirty) {
    if (!confirmDiscardMapChanges()) return;
    state.mapDirty = false;
  }
  try { await refreshCurrent(); } catch (e) { toast(e.message, 'error'); }
};

$('#skill-search').oninput = debounce(() => loadSkills().catch(e => toast(e.message, 'error')));
$('#skill-new').onclick = newSkill;

$('#enemy-search').oninput = debounce(() => loadEnemies().catch(e => toast(e.message, 'error')));
$('#enemy-new').onclick = newEnemy;

$('#map-select').onchange = e => switchMap(e.target.value).catch(err => toast(err.message, 'error'));
$('[data-map-tab]').forEach(b => b.onclick = () => switchMapTab(b.dataset.mapTab).catch(err => toast(err.message, 'error')));
$('#map-enemy-search').oninput = debounce(renderEnemyPalette, 100);
$('#map-show-blocking').onchange = e => { state.mapShowBlocking = e.target.checked; renderMapVisual(); };
$('#map-show-spawners').onchange = e => { state.mapShowSpawners = e.target.checked; renderMapVisual(); };
$('#map-show-labels').onchange = e => { state.mapShowLabels = e.target.checked; renderMapVisual(); };
$('#map-only-selected-type').onchange = e => { state.mapOnlySelectedType = e.target.checked; renderMapVisual(); };
$('#map-snap-walkable').onchange = e => { state.mapSnapWalkable = e.target.checked; };
$('#map-undo').onclick = undoMapEdit;
$('#map-redo').onclick = redoMapEdit;
$('#map-zoom-in').onclick = mapZoomIn;
$('#map-zoom-out').onclick = mapZoomOut;
$('#map-zoom-fit').onclick = mapFit;
$('#map-batch-arm').onclick = armBatchPlacement;
$('#map-add').onclick = addMapRow;
$('#map-save').onclick = saveMap;

$('#script-search').oninput = debounce(() => loadScripts().catch(e => toast(e.message, 'error')));
$('#script-new').onclick = newScript;
$('#script-validate').onclick = validateScript;
$('#script-save').onclick = saveScript;
$('#script-delete').onclick = deleteScript;
$('#script-snippet').onchange = e => insertScriptSnippet(e.target.value);
$('#script-editor').addEventListener('keyup', updateCursor);
$('#script-editor').addEventListener('click', updateCursor);
$('#script-editor').addEventListener('input', debounce(async () => {
  if (!state.selectedScript) return;
  await validateScript();
  state.selectedScript.content = $('#script-editor').value;
}, 450));
$('#script-editor').addEventListener('keydown', e => {
  if (e.key === 'Tab') {
    e.preventDefault();
    const t = e.target, start = t.selectionStart, end = t.selectionEnd;
    t.value = t.value.slice(0, start) + '\t' + t.value.slice(end);
    t.setSelectionRange(start + 1, start + 1);
  }
});
$('#script-map-select').onchange = loadScriptMapNpcs;

$('#asset-folder').onchange = () => loadAssets().catch(e => toast(e.message, 'error'));
$('#asset-search').oninput = debounce(() => loadAssets().catch(e => toast(e.message, 'error')));
$('#asset-file-input').onchange = e => replaceAsset(e.target.files?.[0]);

(async function boot() {
  try {
    await loadStatus();
    await go('dashboard');
  } catch (e) {
    toast(`Editor 啟動失敗：${e.message}`, 'error');
  }
})();
