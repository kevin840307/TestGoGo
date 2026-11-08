const $ = (s, root=document) => root.querySelector(s);
const $$ = (s, root=document) => [...root.querySelectorAll(s)];

const state = {
  view: 'dashboard',
  status: null, health: null, history: [], selectedHistory: null,
  skills: [], selectedSkill: null, skillDirty: false,
  enemies: [], selectedEnemy: null, enemyDirty: false,
  maps: [], mapId: null, mapTab: 'enemies', mapRows: [],
  mapMeta: null, mapSelectedIndex: null, mapSelectedIndexes: [], mapSelectedEnemyId: null,
  mapShowPreview: true, mapShowBlocking: true, mapShowSpawners: true, mapShowLabels: true,
  mapOnlySelectedType: false, mapSnapWalkable: true,
  mapZoom: 1, mapClipboard: [], mapUndo: [], mapRedo: [], mapDirty: false,
  mapBatchArmed: false, mapSuppressClick: false, mapSpaceDown: false,
  scripts: [], selectedScript: null, scriptDirty: false,
  assets: [], assetTarget: null,
};

const EDITOR_PREFS_KEY = 'sgo-editor-prefs-v1';
const EDITOR_PREF_FIELDS = [
  'mapId', 'mapTab', 'mapSelectedEnemyId',
  'mapShowPreview', 'mapShowBlocking', 'mapShowSpawners', 'mapShowLabels',
  'mapOnlySelectedType', 'mapSnapWalkable',
];

function loadEditorPreferences() {
  let raw = null;
  try { raw = JSON.parse(localStorage.getItem(EDITOR_PREFS_KEY) || 'null'); } catch {}
  if (!raw || typeof raw !== 'object' || Array.isArray(raw)) return;

  if (Number.isInteger(Number(raw.mapId)) && Number(raw.mapId) > 0) state.mapId = Number(raw.mapId);
  if (raw.mapTab === 'enemies' || raw.mapTab === 'npcs') state.mapTab = raw.mapTab;
  if (Number.isInteger(Number(raw.mapSelectedEnemyId)) && Number(raw.mapSelectedEnemyId) > 0) {
    state.mapSelectedEnemyId = Number(raw.mapSelectedEnemyId);
  }
  for (const key of [
    'mapShowPreview', 'mapShowBlocking', 'mapShowSpawners', 'mapShowLabels',
    'mapOnlySelectedType', 'mapSnapWalkable',
  ]) {
    if (typeof raw[key] === 'boolean') state[key] = raw[key];
  }
}

function saveEditorPreferences() {
  const payload = {};
  for (const key of EDITOR_PREF_FIELDS) payload[key] = state[key];
  try { localStorage.setItem(EDITOR_PREFS_KEY, JSON.stringify(payload)); } catch {}
}

function applyEditorPreferencesToControls() {
  const pairs = [
    ['map-show-preview', 'mapShowPreview'],
    ['map-show-blocking', 'mapShowBlocking'],
    ['map-show-spawners', 'mapShowSpawners'],
    ['map-show-labels', 'mapShowLabels'],
    ['map-only-selected-type', 'mapOnlySelectedType'],
    ['map-snap-walkable', 'mapSnapWalkable'],
  ];
  for (const [id, key] of pairs) {
    const control = document.getElementById(id);
    if (control) control.checked = !!state[key];
  }
  $('.segmented [data-map-tab]').forEach(button => {
    button.classList.toggle('active', button.dataset.mapTab === state.mapTab);
  });
}

loadEditorPreferences();

const VIEW_META = {
  dashboard: ['專案總覽', '集中管理遊戲資料、舊格式與素材。'],
  skills: ['技能編輯器', 'CRUD 技能與每級數值；保留未知的 TRES 欄位。'],
  enemies: ['怪物資料', '編輯 Enemy_Data.csv 常用欄位；其他 legacy 欄位保留。'],
  maps: ['視覺化地圖編輯器', '使用遊戲座標、Blocking.dat 與怪物區域直接放置怪物 / NPC。'],
  scripts: ['NPC 腳本編輯器', '編輯 Quest Lua、檢查 callback 與 #include，並從地圖 NPC 直接跳轉。'],
  assets: ['素材管理', '預覽並替換 client/Assets 素材；寫入前自動備份。'],
  history: ['變更歷史', '查看 Editor 自動備份、比較差異並安全還原。'],
};

async function api(path, options={}) {
  const opts = {...options, headers: {...(options.headers || {})}};
  if (opts.body && typeof opts.body !== 'string') {
    opts.headers['Content-Type'] = 'application/json';
    opts.body = JSON.stringify(opts.body);
  }
  const res = await fetch(path, opts);
  const contentType = res.headers.get('content-type') || '';
  const payload = contentType.includes('application/json') ? await res.json() : await res.text();
  if (!res.ok) throw new Error(payload?.error || payload || `HTTP ${res.status}`);
  return payload;
}

function toast(message, type='success') {
  const el = document.createElement('div');
  el.className = `toast ${type}`;
  el.textContent = message;
  $('#toast-stack').append(el);
  setTimeout(() => el.remove(), 3600);
}

function esc(value='') {
  return String(value).replace(/[&<>'"]/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[ch]));
}

function debounce(fn, delay=220) {
  let timer;
  return (...args) => { clearTimeout(timer); timer = setTimeout(() => fn(...args), delay); };
}

async function loadStatus() {
  state.status = await api('/api/status');
  $('#project-root').textContent = state.status.projectRoot;
  $('#project-root').title = state.status.projectRoot;
  $('#metric-skills').textContent = state.status.skills;
  $('#metric-enemies').textContent = state.status.enemies;
  $('#metric-maps').textContent = state.status.maps;
  $('#metric-scripts').textContent = state.status.scripts;
  const folders = ['', ...(state.status.assetFolders || [])];
  $('#asset-folder').innerHTML = folders.map(x => `<option value="${esc(x)}">${x ? esc(x) : '全部素材'}</option>`).join('');
}

function viewHasUnsavedChanges(view=state.view) {
  if (view === 'maps') return !!state.mapDirty;
  if (view === 'skills') return !!state.skillDirty;
  if (view === 'enemies') return !!state.enemyDirty;
  if (view === 'scripts') return !!state.scriptDirty;
  return false;
}

function clearViewDirty(view=state.view) {
  if (view === 'maps') state.mapDirty = false;
  if (view === 'skills') state.skillDirty = false;
  if (view === 'enemies') state.enemyDirty = false;
  if (view === 'scripts') state.scriptDirty = false;
}

function confirmDiscardCurrentChanges() {
  if (!viewHasUnsavedChanges()) return true;
  if (state.view === 'maps' && typeof confirmDiscardMapChanges === 'function') {
    return confirmDiscardMapChanges();
  }
  const label = {
    skills:'技能',
    enemies:'怪物',
    scripts:'NPC 腳本',
  }[state.view] || '目前';
  return confirm(`${label}有尚未儲存的修改，確定要放棄嗎？`);
}

async function go(view) {
  if (view === state.view) return true;
  if (!confirmDiscardCurrentChanges()) return false;
  clearViewDirty(state.view);
  state.view = view;
  $$('.nav-item').forEach(x => x.classList.toggle('active', x.dataset.view === view));
  $$('.view').forEach(x => x.classList.toggle('active', x.id === `view-${view}`));
  const meta = VIEW_META[view];
  $('#view-title').textContent = meta[0];
  $('#view-subtitle').textContent = meta[1];
  try { await refreshCurrent(); } catch (e) { toast(e.message, 'error'); }
  return true;
}

async function refreshCurrent() {
  switch (state.view) {
    case 'dashboard': return Promise.all([loadStatus(), loadHealth()]);
    case 'skills': return loadSkills();
    case 'enemies': return loadEnemies();
    case 'maps': return loadMaps();
    case 'scripts': return loadScripts();
    case 'assets': return loadAssets();
    case 'history': return loadHistory();
  }
}

