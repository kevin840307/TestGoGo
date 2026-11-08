// Enemies
async function loadEnemies() {
  state.enemies = await api(`/api/enemies?q=${encodeURIComponent($('#enemy-search').value.trim())}`);
  renderEnemyList();
}
function renderEnemyList() {
  const body = $('#enemy-list');
  body.innerHTML = state.enemies.map(e => `
    <tr data-id="${e.id}" class="${state.selectedEnemy?.id === e.id ? 'selected' : ''}">
      <td>${e.id}</td>
      <td><div class="row-title">${esc(e.name)}</div><div class="row-sub">${esc(e.sprite)}</div></td>
      <td>${e.level}</td><td>${e.maxHp}</td>
    </tr>`).join('');
  $$('tr[data-id]', body).forEach(row => row.onclick = () => openEnemy(Number(row.dataset.id)));
}
async function openEnemy(id) {
  state.selectedEnemy = await api(`/api/enemies/${id}`);
  renderEnemyList();
  renderEnemyEditor();
}
function newEnemy() {
  const max = Math.max(0, ...state.enemies.map(x => x.id));
  state.selectedEnemy = {
    id: max + 1, name: '新怪物', level: 1, sprite: '', maxHp: 100,
    cloneFromId: state.enemies[0]?.id || '', _new: true
  };
  renderEnemyList();
  renderEnemyEditor();
}
function enemyOptionList(selectedId) {
  return state.enemies.map(e => `<option value="${e.id}" ${Number(selectedId) === Number(e.id) ? 'selected' : ''}>${esc(e.name)} (#${e.id}) · Lv.${e.level}</option>`).join('');
}
function renderEnemyEditor() {
  const e = state.selectedEnemy;
  if (!e) return;
  $('#enemy-editor').innerHTML = `
    <div class="editor-inner">
      <div class="editor-heading">
        <div><h2>${esc(e.name)}</h2><p>${e._new ? '新增怪物：先選一隻相近怪物當範本最安全' : `Enemy #${e.id} · CSV ${e.columnCount || ''} columns`}</p></div>
        <div class="editor-actions"><button class="button danger" id="enemy-delete" ${e._new ? 'disabled' : ''}>刪除</button><button class="button primary" id="enemy-save">儲存</button></div>
      </div>

      <div class="friendly-editor-grid">
        <div>
          <div class="section-label">基本資料</div>
          <div class="form-grid">
            ${field('enemy-name','怪物名稱',e.name)}
            ${field('enemy-id','Enemy ID',e.id,'number')}
            ${field('enemy-level','等級',e.level,'number')}
            ${field('enemy-hp','最大 HP',e.maxHp,'number')}
            ${field('enemy-sprite','Sprite 素材代號',e.sprite)}
            ${e._new ? `<div class="field"><label>從哪隻怪物複製底層設定</label><select id="enemy-clone"><option value="">不複製（不建議）</option>${enemyOptionList(e.cloneFromId)}</select></div>` : ''}
          </div>
          <div class="validation-box muted" style="margin-top:16px">
            一般只需要改名稱、等級、HP 與 Sprite。其他 legacy 欄位會原樣保留；新增怪物建議選相近怪物複製。
          </div>
        </div>
        <div class="preview-panel">
          <div class="section-label">怪物預覽</div>
          <div id="enemy-preview" class="enemy-preview"><span class="muted">正在尋找對應素材…</span></div>
          <div class="helper">預覽依 Sprite 名稱到 Monsters 素材中搜尋，不影響實際資料。</div>
        </div>
      </div>
    </div>`;
  $('#enemy-save').onclick = saveEnemy;
  $('#enemy-delete').onclick = deleteEnemy;
  $('#enemy-sprite').addEventListener('change', () => loadEnemyPreview($('#enemy-sprite').value.trim()));
  loadEnemyPreview(e.sprite);
}
async function loadEnemyPreview(sprite) {
  const host = $('#enemy-preview');
  if (!host) return;
  if (!sprite) {
    host.innerHTML = '<span class="muted">填入 Sprite 後會自動顯示預覽。</span>';
    return;
  }
  try {
    const assets = await api(`/api/assets?folder=Monsters&q=${encodeURIComponent(sprite)}&limit=12`);
    const image = assets.find(a => a.previewable) || assets[0];
    if (!image || !image.previewable) {
      host.innerHTML = `<span class="muted">找不到 ${esc(sprite)} 的可預覽圖片。</span>`;
      return;
    }
    host.innerHTML = `<img src="/api/assets/file?path=${encodeURIComponent(image.path)}" alt="${esc(sprite)}"><span>${esc(image.path)}</span>`;
  } catch (e) {
    host.innerHTML = `<span class="muted">${esc(e.message)}</span>`;
  }
}
async function saveEnemy() {
  try {
    const e = state.selectedEnemy;
    const id = Number($('#enemy-id').value);
    if (id !== e.id && !e._new) throw new Error('既有怪物不允許直接改 ID');
    const doc = {
      name: $('#enemy-name').value.trim(),
      level: Number($('#enemy-level').value || 0),
      maxHp: Number($('#enemy-hp').value || 0),
      sprite: $('#enemy-sprite').value.trim(),
      cloneFromId: e._new ? ($('#enemy-clone')?.value || null) : null,
    };
    state.selectedEnemy = await api(`/api/enemies/${id}`, {method:'PUT', body:doc});
    toast('怪物已儲存');
    await loadEnemies();
    renderEnemyEditor();
    await loadStatus();
  } catch (e) { toast(e.message, 'error'); }
}
async function deleteEnemy() {
  if (!confirm(`確定刪除怪物 ${state.selectedEnemy.id}？地圖若仍引用它會失效。`)) return;
  try {
    await api(`/api/enemies/${state.selectedEnemy.id}`, {method:'DELETE'});
    state.selectedEnemy = null;
    $('#enemy-editor').innerHTML = emptyEditor('♜','選擇一隻怪物','名稱與預覽優先；Legacy 欄位仍會完整保留。');
    await loadEnemies();
    toast('怪物已刪除');
    await loadStatus();
  } catch (e) { toast(e.message, 'error'); }
}

// Maps
async function loadMaps() {
  const old = state.mapId;
  state.maps = await api('/api/maps');
  $('#map-select').innerHTML = state.maps.map(m => {
    const technical = m.technicalName && m.technicalName !== m.name ? ` · ${esc(m.technicalName)}` : '';
    return `<option value="${m.id}">${esc(m.name)}${technical} · ${m.enemyCount} 怪物 · ${m.npcCount} NPC</option>`;
  }).join('');

  if (old && state.maps.some(x => x.id === old)) state.mapId = old;
  else state.mapId = state.maps[0]?.id || null;
  if (state.mapId) $('#map-select').value = state.mapId;

  if (!state.enemies.length || $('#enemy-search').value.trim()) {
    state.enemies = await api('/api/enemies?q=');
  }
  if (!state.mapSelectedEnemyId && state.enemies.length) {
    state.mapSelectedEnemyId = state.enemies[0].id;
  }

  fillScriptMapSelect();
  await loadMapRows();
}

async function loadMapRows() {
  if (!state.mapId) return;
  const [rows, meta] = await Promise.all([
    api(`/api/maps/${state.mapId}/${state.mapTab}`),
    api(`/api/maps/${state.mapId}/editor-meta`),
  ]);
  state.mapRows = rows;
  state.mapMeta = meta;
  state.mapSelectedIndex = null;
  renderMapTable();
  renderMapVisual();
}

function getEnemyById(id) {
  return state.enemies.find(e => Number(e.id) === Number(id));
}

function mapBounds(rows = state.mapRows) {
  const metaBounds = state.mapMeta?.bounds;
  if (metaBounds &&
      Number(metaBounds.maxX) > Number(metaBounds.minX) &&
      Number(metaBounds.maxY) > Number(metaBounds.minY)) {
    return {
      minX:Number(metaBounds.minX), minY:Number(metaBounds.minY),
      maxX:Number(metaBounds.maxX), maxY:Number(metaBounds.maxY),
    };
  }

  const points = rows.filter(r => Number.isFinite(Number(r.x)) && Number.isFinite(Number(r.y)));
  if (!points.length) return {minX:0, maxX:4000, minY:0, maxY:4000};
  let minX = Math.min(...points.map(r => Number(r.x)));
  let maxX = Math.max(...points.map(r => Number(r.x)));
  let minY = Math.min(...points.map(r => Number(r.y)));
  let maxY = Math.max(...points.map(r => Number(r.y)));
  if (minX === maxX) { minX -= 500; maxX += 500; }
  if (minY === maxY) { minY -= 500; maxY += 500; }
  const padX = Math.max(160, (maxX - minX) * .12);
  const padY = Math.max(160, (maxY - minY) * .12);
  return {minX:minX-padX, maxX:maxX+padX, minY:minY-padY, maxY:maxY+padY};
}

function worldToPercent(x, y, bounds) {
  return {
    left: ((Number(x) - bounds.minX) / (bounds.maxX - bounds.minX)) * 100,
    top: ((Number(y) - bounds.minY) / (bounds.maxY - bounds.minY)) * 100,
  };
}

function clientToWorld(clientX, clientY, stage, bounds) {
  const rect = stage.getBoundingClientRect();
  const px = Math.max(0, Math.min(1, (clientX - rect.left) / Math.max(1, rect.width)));
  const py = Math.max(0, Math.min(1, (clientY - rect.top) / Math.max(1, rect.height)));
  return {
    x: Math.round(bounds.minX + px * (bounds.maxX - bounds.minX)),
    y: Math.round(bounds.minY + py * (bounds.maxY - bounds.minY)),
  };
}

function renderEnemyPalette() {
  const host = $('#map-enemy-list');
  if (!host) return;
  const q = ($('#map-enemy-search')?.value || '').trim().toLowerCase();
  const rows = state.enemies
    .filter(e => !q ||
      String(e.id).includes(q) ||
      String(e.name).toLowerCase().includes(q) ||
      String(e.sprite).toLowerCase().includes(q))
    .slice(0, 120);

  host.innerHTML = rows.map(e => `
    <button class="map-enemy-item ${Number(state.mapSelectedEnemyId) === Number(e.id) ? 'active' : ''}" data-enemy-id="${e.id}">
      <span class="monster-dot">♜</span>
      <span><b>${esc(e.name)}</b><small>#${e.id} · Lv.${e.level} · ${esc(e.sprite)}</small></span>
    </button>`).join('') || '<div class="helper">找不到怪物。</div>';

  $$('.map-enemy-item', host).forEach(btn => btn.onclick = () => {
    state.mapSelectedEnemyId = Number(btn.dataset.enemyId);
    renderEnemyPalette();
    const enemy = getEnemyById(state.mapSelectedEnemyId);
    $('#map-stage-hint').textContent = enemy
      ? `已選「${enemy.name}」：點可走區新增，拖曳既有標記可移動。`
      : '點地圖新增怪物。';
  });
}

function renderSpawnerZones(bounds) {
  if (!state.mapShowSpawners) return '';
  const zones = state.mapMeta?.spawners || [];
  return zones.map((zone, i) => {
    const a = worldToPercent(zone.x1, zone.y1, bounds);
    const b = worldToPercent(zone.x2, zone.y2, bounds);
    const left = Math.max(0, Math.min(100, a.left));
    const top = Math.max(0, Math.min(100, a.top));
    const right = Math.max(0, Math.min(100, b.left));
    const bottom = Math.max(0, Math.min(100, b.top));
    const width = Math.max(0, right - left);
    const height = Math.max(0, bottom - top);
    if (width <= 0 || height <= 0) return '';
    const label = zone.name || zone.monsterName || `Zone ${i + 1}`;
    return `<div class="map-spawn-zone" style="left:${left}%;top:${top}%;width:${width}%;height:${height}%;" title="${esc(label)} · ${esc(zone.monsterName || '')} · Max ${zone.maxCount || 0}">
      <span>${esc(label)}</span>
    </div>`;
  }).join('');
}

function renderContentBoundary(bounds) {
  const b = state.mapMeta?.bounds;
  if (!b || b.contentMinX == null) return '';
  const a = worldToPercent(b.contentMinX, b.contentMinY, bounds);
  const c = worldToPercent(b.contentMaxX, b.contentMaxY, bounds);
  return `<div class="map-content-boundary" style="left:${a.left}%;top:${a.top}%;width:${c.left-a.left}%;height:${c.top-a.top}%;" title="map_bounds.json 有效內容範圍"></div>`;
}

function renderMapVisual() {
  const enemyMode = state.mapTab === 'enemies';
  $('#map-enemy-palette').hidden = !enemyMode;
  $('#map-npc-palette').hidden = enemyMode;
  renderEnemyPalette();

  const stage = $('#map-stage');
  const bounds = mapBounds();
  state.mapViewBounds = bounds;

  const worldW = Math.max(1, bounds.maxX - bounds.minX);
  const worldH = Math.max(1, bounds.maxY - bounds.minY);
  stage.style.aspectRatio = `${worldW} / ${worldH}`;

  const blocking = state.mapMeta?.blocking;
  const blockingLayer = state.mapShowBlocking && blocking?.hasBlocking
    ? `<img class="map-blocking-layer" src="/api/maps/${state.mapId}/blocking.png?v=${state.mapId}" alt="Blocking.dat overlay" draggable="false">`
    : '';

  const markers = state.mapRows.map((row, i) => {
    const p = worldToPercent(row.x, row.y, bounds);
    const enemy = enemyMode ? getEnemyById(row.enemyId) : null;
    const label = enemyMode ? (enemy?.name || `Enemy #${row.enemyId}`) : (row.name || `NPC #${i + 1}`);
    return `<button class="map-marker ${enemyMode ? 'monster' : 'npc'} ${state.mapSelectedIndex === i ? 'selected' : ''}" data-map-index="${i}" style="left:${p.left}%;top:${p.top}%;" title="${esc(label)} · (${row.x}, ${row.y})">
      <span>${enemyMode ? '♜' : '●'}</span><b>${esc(label)}</b>
    </button>`;
  }).join('');

  stage.innerHTML = `
    ${blockingLayer}
    ${renderSpawnerZones(bounds)}
    ${renderContentBoundary(bounds)}
    <div class="map-grid-label tl">(${Math.round(bounds.minX)}, ${Math.round(bounds.minY)})</div>
    <div class="map-grid-label br">(${Math.round(bounds.maxX)}, ${Math.round(bounds.maxY)})</div>
    ${markers}
    ${state.mapRows.length ? '' : '<div class="map-stage-empty">目前沒有資料，直接點可走區新增。</div>'}`;

  $$('.map-marker', stage).forEach(marker => {
    const index = Number(marker.dataset.mapIndex);
    marker.onclick = event => {
      event.stopPropagation();
      state.mapSelectedIndex = index;
      renderMapVisual();
    };
    marker.onpointerdown = event => startMapMarkerDrag(event, index, marker, bounds);
  });

  stage.onmousemove = event => {
    const p = clientToWorld(event.clientX, event.clientY, stage, bounds);
    $('#map-stage-coord').textContent = `X ${p.x} / Y ${p.y}`;
  };
  stage.onmouseleave = () => $('#map-stage-coord').textContent = 'X — / Y —';
  stage.onclick = async event => {
    if (event.target.closest('.map-marker')) return;
    const p = clientToWorld(event.clientX, event.clientY, stage, bounds);
    await addMapRowAt(p.x, p.y);
  };

  const map = state.maps.find(x => x.id === state.mapId);
  const collisionText = blocking?.hasBlocking
    ? `Blocking ${blocking.width}×${blocking.height} / ${blocking.cellSize}px`
    : '無 Blocking.dat';
  $('#map-meta').textContent =
    `${map?.name || ''} · ${state.mapRows.length} ${enemyMode ? '個怪物出生點' : '個 NPC'} · ${collisionText} · ${state.mapMeta?.spawners?.length || 0} 個區域參考`;

  $('#map-stage-hint').textContent = enemyMode
    ? (getEnemyById(state.mapSelectedEnemyId)
      ? `已選「${getEnemyById(state.mapSelectedEnemyId).name}」：點可走區新增，拖曳標記移動。`
      : '先從左側選一隻怪物。')
    : '點可走區新增 NPC，拖曳標記改位置。';

  renderMapInspector();
}

async function normalizeMapPoint(x, y, notify=true) {
  if (!state.mapMeta?.blocking?.hasBlocking) return {x:Math.round(x), y:Math.round(y)};
  const terrain = await api(`/api/maps/${state.mapId}/terrain?x=${encodeURIComponent(x)}&y=${encodeURIComponent(y)}`);
  if (terrain.walkable) return {x:Math.round(x), y:Math.round(y)};

  if (!state.mapSnapWalkable) {
    if (notify) {
      toast(terrain.special ? '目前位置是特殊地形，仍保留此座標' : '目前位置是不可走區，仍保留此座標', 'error');
    }
    return {x:Math.round(x), y:Math.round(y)};
  }

  if (terrain.nearest) {
    if (notify) {
      toast(`位置不可走，已自動吸附到 (${terrain.nearest.x}, ${terrain.nearest.y})`);
    }
    return {x:Number(terrain.nearest.x), y:Number(terrain.nearest.y)};
  }

  if (notify) toast('附近找不到可走位置，未套用這次移動', 'error');
  return null;
}

function startMapMarkerDrag(event, index, marker, bounds) {
  if (event.button !== 0) return;
  event.preventDefault();
  event.stopPropagation();

  const stage = $('#map-stage');
  const original = {x:Number(state.mapRows[index].x), y:Number(state.mapRows[index].y)};
  let latest = {...original};
  marker.setPointerCapture?.(event.pointerId);
  marker.classList.add('dragging');

  const move = e => {
    latest = clientToWorld(e.clientX, e.clientY, stage, bounds);
    const pct = worldToPercent(latest.x, latest.y, bounds);
    marker.style.left = `${pct.left}%`;
    marker.style.top = `${pct.top}%`;
    marker.title = `(${latest.x}, ${latest.y})`;
    $('#map-stage-coord').textContent = `X ${latest.x} / Y ${latest.y}`;
  };

  const up = async () => {
    marker.classList.remove('dragging');
    window.removeEventListener('pointermove', move);
    window.removeEventListener('pointerup', up);

    try {
      const normalized = await normalizeMapPoint(latest.x, latest.y);
      state.mapRows[index].x = normalized?.x ?? original.x;
      state.mapRows[index].y = normalized?.y ?? original.y;
    } catch (e) {
      state.mapRows[index].x = original.x;
      state.mapRows[index].y = original.y;
      toast(e.message, 'error');
    }

    state.mapSelectedIndex = index;
    renderMapTable();
    renderMapVisual();
  };

  window.addEventListener('pointermove', move);
  window.addEventListener('pointerup', up, {once:true});
}

async function updateTerrainStatus(row) {
  const host = $('#map-terrain-status');
  if (!host || !state.mapMeta?.blocking?.hasBlocking) {
    if (host) {
      host.className = 'terrain-status neutral';
      host.textContent = '此地圖沒有 Blocking.dat，可自由放置。';
    }
    return;
  }
  try {
    const terrain = await api(`/api/maps/${state.mapId}/terrain?x=${encodeURIComponent(row.x)}&y=${encodeURIComponent(row.y)}`);
    if (terrain.walkable) {
      host.className = 'terrain-status good';
      host.textContent = `✓ 可走區 · cell (${terrain.cellX}, ${terrain.cellY})`;
    } else if (terrain.special) {
      host.className = 'terrain-status warn';
      host.textContent = `⚠ 特殊地形 · cell (${terrain.cellX}, ${terrain.cellY})`;
    } else {
      host.className = 'terrain-status bad';
      host.textContent = `✕ 不可走區 · cell (${terrain.cellX}, ${terrain.cellY})`;
    }
  } catch (e) {
    host.className = 'terrain-status neutral';
    host.textContent = e.message;
  }
}

function renderMapInspector() {
  const host = $('#map-inspector');
  const i = state.mapSelectedIndex;
  if (i == null || !state.mapRows[i]) {
    host.innerHTML = '<div class="empty-state compact-empty"><div class="empty-icon">⌖</div><h3>選擇地圖物件</h3><p>點一下標記即可編輯。</p></div>';
    return;
  }

  const row = state.mapRows[i];
  if (state.mapTab === 'enemies') {
    const enemy = getEnemyById(row.enemyId);
    host.innerHTML = `
      <div class="map-inspector-head"><div><p class="eyebrow">SELECTED MONSTER</p><h3>${esc(enemy?.name || `Enemy #${row.enemyId}`)}</h3></div><span class="badge neutral">#${i + 1}</span></div>
      <div class="field"><label>怪物</label><select id="map-inspector-enemy">${enemyOptionList(row.enemyId)}</select></div>
      <div class="coord-grid">
        <div class="field"><label>X</label><input id="map-inspector-x" type="number" value="${row.x}"></div>
        <div class="field"><label>Y</label><input id="map-inspector-y" type="number" value="${row.y}"></div>
      </div>
      <div id="map-terrain-status" class="terrain-status neutral">檢查地形中…</div>
      <details class="inline-advanced"><summary>進階資料</summary><div class="field"><label>Source Flags</label><input id="map-inspector-flags" type="number" value="${row.sourceFlags}"></div></details>
      <div class="map-inspector-actions"><button class="button ghost" id="map-duplicate">複製</button><button class="button danger" id="map-delete-selected">刪除</button></div>`;
    $('#map-inspector-enemy').onchange = e => {
      row.enemyId = Number(e.target.value);
      renderMapTable();
      renderMapVisual();
    };
    $('#map-inspector-flags').onchange = e => {
      row.sourceFlags = Number(e.target.value || 0);
      renderMapTable();
    };
  } else {
    host.innerHTML = `
      <div class="map-inspector-head"><div><p class="eyebrow">SELECTED NPC</p><h3>${esc(row.name || '未命名 NPC')}</h3></div><span class="badge neutral">#${i + 1}</span></div>
      <div class="field"><label>NPC 名稱</label><input id="map-inspector-name" value="${esc(row.name)}"></div>
      <div class="field"><label>互動腳本</label><div class="inline-field"><input id="map-inspector-script" value="${esc(row.script)}"><button class="button ghost" id="map-open-script">開啟</button></div></div>
      <div class="field"><label>NPC Type ID</label><input id="map-inspector-type" type="number" value="${row.npcTypeId}"></div>
      <div class="coord-grid">
        <div class="field"><label>X</label><input id="map-inspector-x" type="number" value="${row.x}"></div>
        <div class="field"><label>Y</label><input id="map-inspector-y" type="number" value="${row.y}"></div>
      </div>
      <div id="map-terrain-status" class="terrain-status neutral">檢查地形中…</div>
      <div class="map-inspector-actions"><button class="button ghost" id="map-duplicate">複製</button><button class="button danger" id="map-delete-selected">刪除</button></div>`;
    $('#map-inspector-name').onchange = e => {
      row.name = e.target.value;
      renderMapTable();
      renderMapVisual();
    };
    $('#map-inspector-script').onchange = e => {
      row.script = e.target.value;
      renderMapTable();
    };
    $('#map-inspector-type').onchange = e => {
      row.npcTypeId = Number(e.target.value || 0);
      renderMapTable();
    };
    $('#map-open-script').onclick = () => openScriptFromMap(row.script);
  }

  const updateCoords = async () => {
    try {
      const normalized = await normalizeMapPoint(
        Number($('#map-inspector-x').value || 0),
        Number($('#map-inspector-y').value || 0)
      );
      if (!normalized) {
        $('#map-inspector-x').value = row.x;
        $('#map-inspector-y').value = row.y;
        return;
      }
      row.x = normalized.x;
      row.y = normalized.y;
      renderMapTable();
      renderMapVisual();
    } catch (e) {
      toast(e.message, 'error');
    }
  };
  $('#map-inspector-x').onchange = updateCoords;
  $('#map-inspector-y').onchange = updateCoords;
  $('#map-duplicate').onclick = () => duplicateMapRow(i);
  $('#map-delete-selected').onclick = () => deleteMapRow(i);
  updateTerrainStatus(row);
}

function renderMapTable() {
  const enemy = state.mapTab === 'enemies';
  $('#map-head').innerHTML = enemy
    ? '<tr><th>#</th><th>怪物</th><th>Enemy ID</th><th>X</th><th>Y</th><th>Source Flags</th><th></th></tr>'
    : '<tr><th>#</th><th>NPC Type</th><th>Name</th><th>Script</th><th>X</th><th>Y</th><th></th></tr>';

  $('#map-body').innerHTML = state.mapRows.map((r,i) => enemy ? `
    <tr data-i="${i}">
      <td>${i + 1}</td><td>${esc(getEnemyById(r.enemyId)?.name || 'Unknown')}</td>
      <td><input data-k="enemyId" type="number" value="${r.enemyId}"></td>
      <td><input data-k="x" type="number" value="${r.x}"></td>
      <td><input data-k="y" type="number" value="${r.y}"></td>
      <td><input data-k="sourceFlags" type="number" value="${r.sourceFlags}"></td>
      <td><button class="icon-button row-delete">×</button></td>
    </tr>` : `
    <tr data-i="${i}">
      <td>${i + 1}</td><td><input data-k="npcTypeId" type="number" value="${r.npcTypeId}"></td>
      <td><input data-k="name" value="${esc(r.name)}"></td>
      <td><input data-k="script" value="${esc(r.script)}" title="點兩下開啟腳本"></td>
      <td><input data-k="x" type="number" value="${r.x}"></td>
      <td><input data-k="y" type="number" value="${r.y}"></td>
      <td><button class="icon-button row-delete">×</button></td>
    </tr>`).join('');

  $$('#map-body tr').forEach((tr, i) => {
    $$('input[data-k]', tr).forEach(inp => {
      inp.oninput = () => {
        state.mapRows[i][inp.dataset.k] = inp.type === 'number' ? Number(inp.value || 0) : inp.value;
      };
      inp.onchange = () => renderMapVisual();
      if (inp.dataset.k === 'script') {
        inp.ondblclick = () => openScriptFromMap(inp.value);
      }
    });
    $('.row-delete', tr).onclick = () => deleteMapRow(i);
    tr.onclick = e => {
      if (e.target.matches('input,button')) return;
      state.mapSelectedIndex = i;
      renderMapVisual();
    };
  });
}

async function addMapRowAt(x, y) {
  if (state.mapTab === 'enemies' && !state.mapSelectedEnemyId) {
    toast('請先從左側選一隻怪物', 'error');
    return;
  }

  let normalized;
  try {
    normalized = await normalizeMapPoint(x, y);
  } catch (e) {
    toast(e.message, 'error');
    return;
  }
  if (!normalized) return;

  if (state.mapTab === 'enemies') {
    state.mapRows.push({
      index:-1,
      enemyId:Number(state.mapSelectedEnemyId),
      x:normalized.x,
      y:normalized.y,
      sourceFlags:0,
    });
  } else {
    state.mapRows.push({
      index:-1,
      npcTypeId:1,
      name:'新 NPC',
      script:'NewNpc.txt',
      x:normalized.x,
      y:normalized.y,
    });
  }
  state.mapSelectedIndex = state.mapRows.length - 1;
  renderMapTable();
  renderMapVisual();
}

async function addMapRow() {
  const bounds = state.mapViewBounds || mapBounds();
  await addMapRowAt(
    Math.round((bounds.minX + bounds.maxX) / 2),
    Math.round((bounds.minY + bounds.maxY) / 2)
  );
}

async function duplicateMapRow(index) {
  const source = state.mapRows[index];
  if (!source) return;
  let point = {x:Number(source.x) + 32, y:Number(source.y) + 32};
  try {
    point = await normalizeMapPoint(point.x, point.y, false) || point;
  } catch (_) {}
  const clone = {...source, index:-1, x:point.x, y:point.y};
  state.mapRows.splice(index + 1, 0, clone);
  state.mapSelectedIndex = index + 1;
  renderMapTable();
  renderMapVisual();
  toast('已複製，拖曳到新位置即可');
}

function deleteMapRow(index) {
  state.mapRows.splice(index, 1);
  state.mapSelectedIndex = null;
  renderMapTable();
  renderMapVisual();
}

async function saveMap() {
  try {
    state.mapRows = await api(`/api/maps/${state.mapId}/${state.mapTab}`, {
      method:'PUT',
      body:state.mapRows,
    });
    toast('地圖資料已儲存');
    await loadMaps();
  } catch (e) {
    toast(e.message, 'error');
  }
}

async function openScriptFromMap(name) {
  if (!name) return;
  await go('scripts');
  await openScript(name);
}
