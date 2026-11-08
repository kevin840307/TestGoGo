// Visual map editor
function cloneMapRows(rows=state.mapRows) {
  return rows.map(row => ({...row}));
}

function mapSnapshot() {
  return JSON.stringify(state.mapRows);
}

function resetMapHistory() {
  state.mapUndo = [mapSnapshot()];
  state.mapRedo = [];
  state.mapDirty = false;
  state.mapSelectedIndexes = [];
  state.mapSelectedIndex = null;
  updateMapCommandState();
}

function commitMapEdit(label='已修改地圖') {
  const snapshot = mapSnapshot();
  if (state.mapUndo.at(-1) !== snapshot) {
    state.mapUndo.push(snapshot);
    if (state.mapUndo.length > 60) state.mapUndo.splice(1, 1);
    state.mapRedo = [];
  }
  state.mapDirty = snapshot !== state.mapUndo[0];
  updateMapCommandState();
  if (label) $('#map-stage-hint').textContent = label;
}

function restoreMapSnapshot(snapshot) {
  try {
    state.mapRows = JSON.parse(snapshot);
  } catch {
    return;
  }
  state.mapSelectedIndexes = [];
  state.mapSelectedIndex = null;
  renderMapTable();
  renderMapVisual();
}

function undoMapEdit() {
  if (state.mapUndo.length <= 1) return;
  const current = state.mapUndo.pop();
  state.mapRedo.push(current);
  restoreMapSnapshot(state.mapUndo.at(-1));
  state.mapDirty = state.mapUndo.at(-1) !== state.mapUndo[0];
  updateMapCommandState();
  toast('已復原');
}

function redoMapEdit() {
  const snapshot = state.mapRedo.pop();
  if (!snapshot) return;
  state.mapUndo.push(snapshot);
  restoreMapSnapshot(snapshot);
  state.mapDirty = state.mapUndo.at(-1) !== state.mapUndo[0];
  updateMapCommandState();
  toast('已重做');
}

function updateMapCommandState() {
  const undo = $('#map-undo');
  const redo = $('#map-redo');
  const save = $('#map-save');
  const selectionCount = state.mapSelectedIndexes.length;
  if (undo) undo.disabled = state.mapUndo.length <= 1;
  if (redo) redo.disabled = !state.mapRedo.length;
  if (save) {
    save.classList.toggle('dirty', !!state.mapDirty);
    save.textContent = state.mapDirty ? '儲存地圖 •' : '儲存地圖';
  }
  const counter = $('#map-selection-count');
  if (counter) counter.textContent = selectionCount ? `已選 ${selectionCount}` : '';
}

function confirmDiscardMapChanges() {
  return !state.mapDirty || confirm('目前地圖有尚未儲存的修改，確定要放棄嗎？');
}

async function switchMap(nextId) {
  const id = Number(nextId);
  if (id === state.mapId) return;
  if (!confirmDiscardMapChanges()) {
    $('#map-select').value = state.mapId;
    return;
  }
  state.mapId = id;
  await loadMapRows();
}

async function switchMapTab(tab) {
  if (tab === state.mapTab) return;
  if (!confirmDiscardMapChanges()) {
    $$('[data-map-tab]').forEach(x => x.classList.toggle('active', x.dataset.mapTab === state.mapTab));
    return;
  }
  state.mapTab = tab;
  $$('[data-map-tab]').forEach(x => x.classList.toggle('active', x.dataset.mapTab === tab));
  await loadMapRows();
}

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
  state.mapBatchArmed = false;
  state.mapClipboard = [];
  state.mapZoom = 1;
  resetMapHistory();
  renderMapTable();
  renderMapVisual();
  requestAnimationFrame(mapFit);
}

function getEnemyById(id) {
  return state.enemies.find(e => Number(e.id) === Number(id));
}

function mapBounds(rows=state.mapRows) {
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

function setMapSelection(indexes, primary=null) {
  const unique = [...new Set(indexes.filter(i => i >= 0 && i < state.mapRows.length))].sort((a,b) => a-b);
  state.mapSelectedIndexes = unique;
  state.mapSelectedIndex = primary != null && unique.includes(primary) ? primary : (unique.at(-1) ?? null);
  updateMapCommandState();
}

function toggleMapSelection(index) {
  const selected = new Set(state.mapSelectedIndexes);
  if (selected.has(index)) selected.delete(index);
  else selected.add(index);
  setMapSelection([...selected], index);
}

function selectAllMapRows() {
  setMapSelection(state.mapRows.map((_,i) => i), state.mapRows.length ? 0 : null);
  renderMapVisual();
}

function clearMapSelection() {
  setMapSelection([]);
  renderMapVisual();
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
    .slice(0, 150);

  host.innerHTML = rows.map(e => `
    <button class="map-enemy-item ${Number(state.mapSelectedEnemyId) === Number(e.id) ? 'active' : ''}" data-enemy-id="${e.id}">
      <span class="monster-dot">♜</span>
      <span><b>${esc(e.name)}</b><small>#${e.id} · Lv.${e.level} · ${esc(e.sprite)}</small></span>
    </button>`).join('') || '<div class="helper">找不到怪物。</div>';

  $$('.map-enemy-item', host).forEach(btn => btn.onclick = () => {
    state.mapSelectedEnemyId = Number(btn.dataset.enemyId);
    renderEnemyPalette();
    if (state.mapOnlySelectedType) renderMapVisual();
    const enemy = getEnemyById(state.mapSelectedEnemyId);
    $('#map-stage-hint').textContent = enemy
      ? `已選「${enemy.name}」：點可走區新增；Shift 拖曳框選，多選後可一起移動。`
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

function markerVisible(row) {
  if (state.mapTab !== 'enemies' || !state.mapOnlySelectedType) return true;
  return Number(row.enemyId) === Number(state.mapSelectedEnemyId);
}

function renderMapVisual() {
  const enemyMode = state.mapTab === 'enemies';
  $('#map-enemy-palette').hidden = !enemyMode;
  $('#map-npc-palette').hidden = enemyMode;
  const onlyTypeToggle = $('#map-only-selected-type')?.closest('label');
  if (onlyTypeToggle) onlyTypeToggle.hidden = !enemyMode;
  renderEnemyPalette();

  const stage = $('#map-stage');
  const bounds = mapBounds();
  state.mapViewBounds = bounds;
  const selected = new Set(state.mapSelectedIndexes);

  const worldW = Math.max(1, bounds.maxX - bounds.minX);
  const worldH = Math.max(1, bounds.maxY - bounds.minY);
  stage.style.aspectRatio = `${worldW} / ${worldH}`;
  stage.style.width = `${Math.round(state.mapZoom * 100)}%`;

  const blocking = state.mapMeta?.blocking;
  const blockingLayer = state.mapShowBlocking && blocking?.hasBlocking
    ? `<img class="map-blocking-layer" src="/api/maps/${state.mapId}/blocking.png?v=${state.mapId}" alt="Blocking.dat overlay" draggable="false">`
    : '';

  const markers = state.mapRows.map((row, i) => {
    if (!markerVisible(row)) return '';
    const p = worldToPercent(row.x, row.y, bounds);
    const enemy = enemyMode ? getEnemyById(row.enemyId) : null;
    const label = enemyMode ? (enemy?.name || `Enemy #${row.enemyId}`) : (row.name || `NPC #${i + 1}`);
    return `<button class="map-marker ${enemyMode ? 'monster' : 'npc'} ${selected.has(i) ? 'selected' : ''} ${state.mapShowLabels ? '' : 'labels-hidden'}" data-map-index="${i}" style="left:${p.left}%;top:${p.top}%;" title="${esc(label)} · (${row.x}, ${row.y})">
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
      if (state.mapSuppressClick) {
        state.mapSuppressClick = false;
        return;
      }
      if (event.shiftKey || event.ctrlKey || event.metaKey) toggleMapSelection(index);
      else setMapSelection([index], index);
      renderMapVisual();
    };
    marker.onpointerdown = event => {
      if (event.shiftKey || event.ctrlKey || event.metaKey) return;
      startMapMarkerDrag(event, index, marker, bounds);
    };
  });

  stage.onmousemove = event => {
    const p = clientToWorld(event.clientX, event.clientY, stage, bounds);
    $('#map-stage-coord').textContent = `X ${p.x} / Y ${p.y}`;
  };
  stage.onmouseleave = () => $('#map-stage-coord').textContent = 'X — / Y —';

  stage.onpointerdown = event => {
    if (event.target.closest('.map-marker')) return;
    if (event.shiftKey && event.button === 0) startMarqueeSelection(event, bounds);
  };

  stage.onclick = async event => {
    if (state.mapSuppressClick) {
      state.mapSuppressClick = false;
      return;
    }
    if (event.target.closest('.map-marker') || event.shiftKey) return;
    const p = clientToWorld(event.clientX, event.clientY, stage, bounds);
    if (state.mapBatchArmed && enemyMode) await addMapBatchAt(p.x, p.y);
    else await addMapRowAt(p.x, p.y);
  };

  wireMapPanAndZoom();

  const map = state.maps.find(x => x.id === state.mapId);
  const collisionText = blocking?.hasBlocking
    ? `Blocking ${blocking.width}×${blocking.height} / ${blocking.cellSize}px`
    : '無 Blocking.dat';
  $('#map-meta').textContent =
    `${map?.name || ''} · ${state.mapRows.length} ${enemyMode ? '個怪物出生點' : '個 NPC'} · ${collisionText} · ${state.mapMeta?.spawners?.length || 0} 個區域參考`;

  $('#map-stage-hint').textContent = enemyMode
    ? (state.mapBatchArmed
      ? '批量放置模式：下一次點地圖會以該位置為中心散佈怪物。'
      : (getEnemyById(state.mapSelectedEnemyId)
        ? `已選「${getEnemyById(state.mapSelectedEnemyId).name}」：點地圖新增；Shift 拖曳框選；Space/中鍵拖曳平移。`
        : '先從左側選一隻怪物。'))
    : '點可走區新增 NPC；Shift 拖曳框選；Space/中鍵拖曳平移。';

  renderMapInspector();
  updateMapCommandState();
  updateMapZoomLabel();
}

async function normalizeMapPoints(points, notify=true) {
  const rounded = points.map(p => ({x:Math.round(p.x), y:Math.round(p.y)}));
  if (!state.mapMeta?.blocking?.hasBlocking) return rounded.map(p => ({...p, snapped:false, walkable:true}));

  const result = await api(`/api/maps/${state.mapId}/snap-points`, {
    method:'POST',
    body:{points:rounded, snap:state.mapSnapWalkable},
  });

  if (notify) {
    const snapped = result.filter(x => x.snapped).length;
    const unresolved = result.filter(x => x.unresolved).length;
    if (snapped) toast(`有 ${snapped} 個位置落在阻擋區，已自動吸附到最近可走格`);
    if (unresolved) toast(`有 ${unresolved} 個位置附近找不到可走格`, 'error');
  }
  return result;
}

async function normalizeMapPoint(x, y, notify=true) {
  const result = await normalizeMapPoints([{x,y}], notify);
  const point = result[0];
  if (!point || point.unresolved) return null;
  return {x:Number(point.x), y:Number(point.y)};
}

function startMapMarkerDrag(event, index, marker, bounds) {
  if (event.button !== 0) return;
  event.preventDefault();
  event.stopPropagation();

  if (!state.mapSelectedIndexes.includes(index)) setMapSelection([index], index);
  const movingIndexes = [...state.mapSelectedIndexes];
  const originals = new Map(movingIndexes.map(i => [i, {x:Number(state.mapRows[i].x), y:Number(state.mapRows[i].y)}]));
  const stage = $('#map-stage');
  const start = clientToWorld(event.clientX, event.clientY, stage, bounds);
  let latest = start;
  marker.setPointerCapture?.(event.pointerId);
  marker.classList.add('dragging');

  const move = e => {
    latest = clientToWorld(e.clientX, e.clientY, stage, bounds);
    const dx = latest.x - start.x;
    const dy = latest.y - start.y;
    movingIndexes.forEach(i => {
      const original = originals.get(i);
      const el = stage.querySelector(`[data-map-index="${i}"]`);
      if (!el || !original) return;
      const p = worldToPercent(original.x + dx, original.y + dy, bounds);
      el.style.left = `${p.left}%`;
      el.style.top = `${p.top}%`;
    });
    $('#map-stage-coord').textContent = `ΔX ${dx} / ΔY ${dy}`;
  };

  const up = async () => {
    marker.classList.remove('dragging');
    window.removeEventListener('pointermove', move);
    window.removeEventListener('pointerup', up);

    const dx = latest.x - start.x;
    const dy = latest.y - start.y;
    const moved = Math.abs(dx) > 2 || Math.abs(dy) > 2;
    if (!moved) return;
    state.mapSuppressClick = true;
    try {
      const targets = movingIndexes.map(i => {
        const original = originals.get(i);
        return {x:original.x + dx, y:original.y + dy};
      });
      const normalized = await normalizeMapPoints(targets);
      movingIndexes.forEach((i, n) => {
        const original = originals.get(i);
        const point = normalized[n];
        state.mapRows[i].x = point && !point.unresolved ? Number(point.x) : original.x;
        state.mapRows[i].y = point && !point.unresolved ? Number(point.y) : original.y;
      });
      commitMapEdit(movingIndexes.length > 1 ? `已移動 ${movingIndexes.length} 個物件` : '已移動物件');
    } catch (e) {
      movingIndexes.forEach(i => Object.assign(state.mapRows[i], originals.get(i)));
      toast(e.message, 'error');
    }
    renderMapTable();
    renderMapVisual();
  };

  window.addEventListener('pointermove', move);
  window.addEventListener('pointerup', up, {once:true});
}

function startMarqueeSelection(event, bounds) {
  event.preventDefault();
  event.stopPropagation();
  state.mapSuppressClick = true;

  const stage = $('#map-stage');
  const rect = stage.getBoundingClientRect();
  const startX = event.clientX - rect.left;
  const startY = event.clientY - rect.top;
  const box = document.createElement('div');
  box.className = 'map-marquee';
  stage.appendChild(box);

  const move = e => {
    const x = Math.max(0, Math.min(rect.width, e.clientX - rect.left));
    const y = Math.max(0, Math.min(rect.height, e.clientY - rect.top));
    box.style.left = `${Math.min(startX,x)}px`;
    box.style.top = `${Math.min(startY,y)}px`;
    box.style.width = `${Math.abs(x-startX)}px`;
    box.style.height = `${Math.abs(y-startY)}px`;
  };

  const up = e => {
    window.removeEventListener('pointermove', move);
    window.removeEventListener('pointerup', up);
    const endX = Math.max(0, Math.min(rect.width, e.clientX - rect.left));
    const endY = Math.max(0, Math.min(rect.height, e.clientY - rect.top));
    const left = Math.min(startX,endX), right = Math.max(startX,endX);
    const top = Math.min(startY,endY), bottom = Math.max(startY,endY);
    const selected = [];
    state.mapRows.forEach((row,i) => {
      if (!markerVisible(row)) return;
      const p = worldToPercent(row.x,row.y,bounds);
      const x = p.left / 100 * rect.width;
      const y = p.top / 100 * rect.height;
      if (x >= left && x <= right && y >= top && y <= bottom) selected.push(i);
    });
    setMapSelection(selected, selected.at(-1) ?? null);
    box.remove();
    renderMapVisual();
  };

  window.addEventListener('pointermove', move);
  window.addEventListener('pointerup', up, {once:true});
}

function wireMapPanAndZoom() {
  const scroller = $('#map-stage-scroller');
  const stage = $('#map-stage');
  if (!scroller || !stage) return;

  scroller.onwheel = event => {
    if (!event.ctrlKey && !event.metaKey) return;
    event.preventDefault();
    const factor = event.deltaY < 0 ? 1.15 : 1 / 1.15;
    setMapZoom(state.mapZoom * factor, event.clientX, event.clientY);
  };

  scroller.onpointerdown = event => {
    const canPan = event.button === 1 || (state.mapSpaceDown && event.button === 0);
    if (!canPan) return;
    event.preventDefault();
    state.mapSuppressClick = true;
    const startX = event.clientX;
    const startY = event.clientY;
    const startLeft = scroller.scrollLeft;
    const startTop = scroller.scrollTop;
    scroller.classList.add('panning');

    const move = e => {
      scroller.scrollLeft = startLeft - (e.clientX - startX);
      scroller.scrollTop = startTop - (e.clientY - startY);
    };
    const up = () => {
      scroller.classList.remove('panning');
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up, {once:true});
  };
}

function setMapZoom(value, anchorClientX=null, anchorClientY=null) {
  const scroller = $('#map-stage-scroller');
  const stage = $('#map-stage');
  if (!scroller || !stage) return;

  const oldZoom = state.mapZoom;
  const nextZoom = Math.max(.25, Math.min(4, Number(value.toFixed(3))));
  if (Math.abs(nextZoom - oldZoom) < .001) return;

  let ax = scroller.clientWidth / 2;
  let ay = scroller.clientHeight / 2;
  if (anchorClientX != null && anchorClientY != null) {
    const r = scroller.getBoundingClientRect();
    ax = anchorClientX - r.left;
    ay = anchorClientY - r.top;
  }
  const worldScrollX = (scroller.scrollLeft + ax) / oldZoom;
  const worldScrollY = (scroller.scrollTop + ay) / oldZoom;

  state.mapZoom = nextZoom;
  stage.style.width = `${Math.round(nextZoom * 100)}%`;
  requestAnimationFrame(() => {
    scroller.scrollLeft = worldScrollX * nextZoom - ax;
    scroller.scrollTop = worldScrollY * nextZoom - ay;
  });
  updateMapZoomLabel();
}

function mapZoomIn() { setMapZoom(state.mapZoom * 1.2); }
function mapZoomOut() { setMapZoom(state.mapZoom / 1.2); }
function mapFit() {
  const stage = $('#map-stage');
  const scroller = $('#map-stage-scroller');
  if (!stage || !scroller) return;
  const bounds = state.mapViewBounds || mapBounds();
  const worldW = Math.max(1, bounds.maxX - bounds.minX);
  const worldH = Math.max(1, bounds.maxY - bounds.minY);
  const baseHeight = Math.max(1, scroller.clientWidth * worldH / worldW);
  const fit = Math.max(.25, Math.min(1, scroller.clientHeight / baseHeight));
  state.mapZoom = fit;
  stage.style.width = `${Math.round(fit * 100)}%`;
  scroller.scrollLeft = 0;
  scroller.scrollTop = 0;
  updateMapZoomLabel();
}
function updateMapZoomLabel() {
  const el = $('#map-zoom-label');
  if (el) el.textContent = `${Math.round(state.mapZoom * 100)}%`;
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
  const indexes = state.mapSelectedIndexes;
  if (indexes.length > 1) {
    const rows = indexes.map(i => state.mapRows[i]).filter(Boolean);
    const enemyMode = state.mapTab === 'enemies';
    host.innerHTML = `
      <div class="map-inspector-head"><div><p class="eyebrow">MULTI SELECT</p><h3>已選 ${rows.length} 個${enemyMode ? '怪物' : ' NPC'}</h3></div><span class="badge good">群組</span></div>
      <div class="validation-box muted">拖曳其中任一標記即可整組移動。可用 Ctrl+C / Ctrl+V 複製貼上，Delete 刪除。</div>
      ${enemyMode ? `<div class="field" style="margin-top:12px"><label>全部改成怪物</label><select id="map-group-enemy"><option value="">— 不變 —</option>${enemyOptionList('')}</select></div>` : ''}
      <div class="map-inspector-actions"><button class="button ghost" id="map-copy-selected">複製</button><button class="button danger" id="map-delete-selected">刪除全部</button></div>`;
    if (enemyMode) {
      $('#map-group-enemy').onchange = e => {
        if (!e.target.value) return;
        indexes.forEach(i => state.mapRows[i].enemyId = Number(e.target.value));
        commitMapEdit(`已更新 ${indexes.length} 個怪物種類`);
        renderMapTable();
        renderMapVisual();
      };
    }
    $('#map-copy-selected').onclick = copyMapSelection;
    $('#map-delete-selected').onclick = deleteSelectedMapRows;
    return;
  }

  const i = state.mapSelectedIndex;
  if (i == null || !state.mapRows[i]) {
    host.innerHTML = '<div class="empty-state compact-empty"><div class="empty-icon">⌖</div><h3>選擇地圖物件</h3><p>點標記、Shift 點多選，或 Shift 拖曳框選。</p></div>';
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
      commitMapEdit('已修改怪物種類');
      renderMapTable();
      renderMapVisual();
    };
    $('#map-inspector-flags').onchange = e => {
      row.sourceFlags = Number(e.target.value || 0);
      commitMapEdit('已修改 Source Flags');
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
      commitMapEdit('已修改 NPC 名稱');
      renderMapTable();
      renderMapVisual();
    };
    $('#map-inspector-script').onchange = e => {
      row.script = e.target.value;
      commitMapEdit('已修改 NPC Script');
      renderMapTable();
    };
    $('#map-inspector-type').onchange = e => {
      row.npcTypeId = Number(e.target.value || 0);
      commitMapEdit('已修改 NPC Type');
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
      commitMapEdit('已修改座標');
      renderMapTable();
      renderMapVisual();
    } catch (e) {
      toast(e.message, 'error');
    }
  };
  $('#map-inspector-x').onchange = updateCoords;
  $('#map-inspector-y').onchange = updateCoords;
  $('#map-duplicate').onclick = () => duplicateMapRows([i]);
  $('#map-delete-selected').onclick = deleteSelectedMapRows;
  updateTerrainStatus(row);
}

function renderMapTable() {
  const enemy = state.mapTab === 'enemies';
  const selected = new Set(state.mapSelectedIndexes);
  $('#map-head').innerHTML = enemy
    ? '<tr><th>#</th><th>怪物</th><th>Enemy ID</th><th>X</th><th>Y</th><th>Source Flags</th><th></th></tr>'
    : '<tr><th>#</th><th>NPC Type</th><th>Name</th><th>Script</th><th>X</th><th>Y</th><th></th></tr>';

  $('#map-body').innerHTML = state.mapRows.map((r,i) => enemy ? `
    <tr data-i="${i}" class="${selected.has(i) ? 'selected' : ''}">
      <td>${i + 1}</td><td>${esc(getEnemyById(r.enemyId)?.name || 'Unknown')}</td>
      <td><input data-k="enemyId" type="number" value="${r.enemyId}"></td>
      <td><input data-k="x" type="number" value="${r.x}"></td>
      <td><input data-k="y" type="number" value="${r.y}"></td>
      <td><input data-k="sourceFlags" type="number" value="${r.sourceFlags}"></td>
      <td><button class="icon-button row-delete">×</button></td>
    </tr>` : `
    <tr data-i="${i}" class="${selected.has(i) ? 'selected' : ''}">
      <td>${i + 1}</td><td><input data-k="npcTypeId" type="number" value="${r.npcTypeId}"></td>
      <td><input data-k="name" value="${esc(r.name)}"></td>
      <td><input data-k="script" value="${esc(r.script)}" title="點兩下開啟腳本"></td>
      <td><input data-k="x" type="number" value="${r.x}"></td>
      <td><input data-k="y" type="number" value="${r.y}"></td>
      <td><button class="icon-button row-delete">×</button></td>
    </tr>`).join('');

  $$('#map-body tr').forEach((tr, i) => {
    $$('input[data-k]', tr).forEach(inp => {
      inp.onchange = async () => {
        const key = inp.dataset.k;
        const oldValue = state.mapRows[i][key];
        const value = inp.type === 'number' ? Number(inp.value || 0) : inp.value;
        if ((key === 'x' || key === 'y') && inp.type === 'number') {
          const x = key === 'x' ? value : Number(state.mapRows[i].x);
          const y = key === 'y' ? value : Number(state.mapRows[i].y);
          const point = await normalizeMapPoint(x,y);
          if (!point) {
            inp.value = oldValue;
            return;
          }
          state.mapRows[i].x = point.x;
          state.mapRows[i].y = point.y;
        } else {
          state.mapRows[i][key] = value;
        }
        commitMapEdit('已修改原始清單');
        renderMapVisual();
        renderMapTable();
      };
      if (inp.dataset.k === 'script') inp.ondblclick = () => openScriptFromMap(inp.value);
    });
    $('.row-delete', tr).onclick = () => {
      setMapSelection([i],i);
      deleteSelectedMapRows();
    };
    tr.onclick = e => {
      if (e.target.matches('input,button')) return;
      if (e.shiftKey || e.ctrlKey || e.metaKey) toggleMapSelection(i);
      else setMapSelection([i],i);
      renderMapVisual();
      renderMapTable();
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

  const row = state.mapTab === 'enemies'
    ? {index:-1, enemyId:Number(state.mapSelectedEnemyId), x:normalized.x, y:normalized.y, sourceFlags:0}
    : {index:-1, npcTypeId:1, name:'新 NPC', script:'NewNpc.txt', x:normalized.x, y:normalized.y};

  state.mapRows.push(row);
  setMapSelection([state.mapRows.length - 1], state.mapRows.length - 1);
  commitMapEdit('已新增地圖物件');
  renderMapTable();
  renderMapVisual();
}

function generateScatterPoints(centerX, centerY, count, radius) {
  const points = [];
  const golden = Math.PI * (3 - Math.sqrt(5));
  for (let i=0; i<count; i++) {
    const ratio = Math.sqrt((i + .5) / Math.max(1,count));
    const angle = i * golden;
    points.push({
      x:centerX + Math.cos(angle) * radius * ratio,
      y:centerY + Math.sin(angle) * radius * ratio,
    });
  }
  return points;
}

function armBatchPlacement() {
  if (state.mapTab !== 'enemies') return;
  if (!state.mapSelectedEnemyId) return toast('請先選一隻怪物', 'error');
  state.mapBatchArmed = !state.mapBatchArmed;
  $('#map-batch-arm')?.classList.toggle('active', state.mapBatchArmed);
  renderMapVisual();
}

async function addMapBatchAt(x,y) {
  const count = Math.max(2, Math.min(100, Number($('#map-batch-count')?.value || 10)));
  const radius = Math.max(16, Math.min(3000, Number($('#map-batch-radius')?.value || 320)));
  state.mapBatchArmed = false;
  $('#map-batch-arm')?.classList.remove('active');

  const requested = generateScatterPoints(x,y,count,radius);
  try {
    const normalized = await normalizeMapPoints(requested);
    const existing = new Set(state.mapRows.map(r => `${Math.round(r.x)}:${Math.round(r.y)}:${r.enemyId || ''}`));
    const addedIndexes = [];
    for (const point of normalized) {
      if (!point || point.unresolved) continue;
      const key = `${point.x}:${point.y}:${state.mapSelectedEnemyId}`;
      if (existing.has(key)) continue;
      existing.add(key);
      state.mapRows.push({
        index:-1,
        enemyId:Number(state.mapSelectedEnemyId),
        x:Number(point.x),
        y:Number(point.y),
        sourceFlags:0,
      });
      addedIndexes.push(state.mapRows.length - 1);
    }
    setMapSelection(addedIndexes, addedIndexes.at(-1) ?? null);
    commitMapEdit(`已批量新增 ${addedIndexes.length} 隻怪物`);
    renderMapTable();
    renderMapVisual();
  } catch (e) {
    toast(e.message,'error');
  }
}

async function addMapRow() {
  const bounds = state.mapViewBounds || mapBounds();
  await addMapRowAt(
    Math.round((bounds.minX + bounds.maxX) / 2),
    Math.round((bounds.minY + bounds.maxY) / 2)
  );
}

async function duplicateMapRows(indexes=state.mapSelectedIndexes) {
  const valid = indexes.filter(i => state.mapRows[i]);
  if (!valid.length) return;
  const targets = valid.map(i => ({
    x:Number(state.mapRows[i].x) + 32,
    y:Number(state.mapRows[i].y) + 32,
  }));
  let normalized;
  try {
    normalized = await normalizeMapPoints(targets, false);
  } catch {
    normalized = targets;
  }

  const added = [];
  valid.forEach((i,n) => {
    const source = state.mapRows[i];
    const p = normalized[n] && !normalized[n].unresolved ? normalized[n] : targets[n];
    state.mapRows.push({...source, index:-1, x:Number(p.x), y:Number(p.y)});
    added.push(state.mapRows.length - 1);
  });
  setMapSelection(added, added.at(-1) ?? null);
  commitMapEdit(`已複製 ${added.length} 個物件`);
  renderMapTable();
  renderMapVisual();
}

function copyMapSelection() {
  const indexes = state.mapSelectedIndexes.length
    ? state.mapSelectedIndexes
    : (state.mapSelectedIndex != null ? [state.mapSelectedIndex] : []);
  state.mapClipboard = indexes.map(i => ({...state.mapRows[i]})).filter(Boolean);
  if (state.mapClipboard.length) toast(`已複製 ${state.mapClipboard.length} 個物件`);
}

async function pasteMapSelection() {
  if (!state.mapClipboard.length) return;
  const points = state.mapClipboard.map(row => ({x:Number(row.x)+48, y:Number(row.y)+48}));
  let normalized;
  try {
    normalized = await normalizeMapPoints(points, false);
  } catch {
    normalized = points;
  }
  const added = [];
  state.mapClipboard.forEach((row,n) => {
    const p = normalized[n] && !normalized[n].unresolved ? normalized[n] : points[n];
    state.mapRows.push({...row, index:-1, x:Number(p.x), y:Number(p.y)});
    added.push(state.mapRows.length - 1);
  });
  setMapSelection(added, added.at(-1) ?? null);
  commitMapEdit(`已貼上 ${added.length} 個物件`);
  renderMapTable();
  renderMapVisual();
}

function deleteSelectedMapRows() {
  const indexes = state.mapSelectedIndexes.length
    ? [...state.mapSelectedIndexes]
    : (state.mapSelectedIndex != null ? [state.mapSelectedIndex] : []);
  if (!indexes.length) return;
  const remove = new Set(indexes);
  state.mapRows = state.mapRows.filter((_,i) => !remove.has(i));
  setMapSelection([]);
  commitMapEdit(`已刪除 ${indexes.length} 個物件`);
  renderMapTable();
  renderMapVisual();
}

function deleteMapRow(index) {
  setMapSelection([index],index);
  deleteSelectedMapRows();
}

async function saveMap() {
  try {
    state.mapRows = await api(`/api/maps/${state.mapId}/${state.mapTab}`, {
      method:'PUT',
      body:state.mapRows,
    });
    resetMapHistory();
    toast('地圖資料已儲存');
    await loadMaps();
  } catch (e) {
    toast(e.message, 'error');
  }
}

async function openScriptFromMap(name) {
  if (!name) return;
  const changed = await go('scripts');
  if (changed === false) return;
  await openScript(name);
}

function handleMapKeyboard(event) {
  const target = event.target;
  const typing = target && (target.matches('input,textarea,select') || target.isContentEditable);
  if (event.code === 'Space' && !typing) {
    state.mapSpaceDown = event.type === 'keydown';
    if (event.type === 'keydown') event.preventDefault();
    return;
  }
  if (event.type !== 'keydown' || typing || state.view !== 'maps') return;

  const cmd = event.ctrlKey || event.metaKey;
  if (cmd && event.key.toLowerCase() === 'z') {
    event.preventDefault();
    if (event.shiftKey) redoMapEdit(); else undoMapEdit();
  } else if (cmd && event.key.toLowerCase() === 'y') {
    event.preventDefault();
    redoMapEdit();
  } else if (cmd && event.key.toLowerCase() === 'c') {
    event.preventDefault();
    copyMapSelection();
  } else if (cmd && event.key.toLowerCase() === 'v') {
    event.preventDefault();
    pasteMapSelection();
  } else if (cmd && event.key.toLowerCase() === 'a') {
    event.preventDefault();
    selectAllMapRows();
  } else if (event.key === 'Delete' || event.key === 'Backspace') {
    event.preventDefault();
    deleteSelectedMapRows();
  } else if (event.key === 'Escape') {
    state.mapBatchArmed = false;
    clearMapSelection();
  } else if (event.key === '+' || event.key === '=') {
    mapZoomIn();
  } else if (event.key === '-') {
    mapZoomOut();
  }
}

window.addEventListener('keydown', handleMapKeyboard);
window.addEventListener('keyup', handleMapKeyboard);
window.addEventListener('beforeunload', event => {
  if (!state.mapDirty) return;
  event.preventDefault();
  event.returnValue = '';
});
