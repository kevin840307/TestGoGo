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
  if (state.enemyDirty) {
    if (state.selectedEnemy?.id === id) return;
    if (!confirm('目前怪物有尚未儲存的修改，確定要切換嗎？')) return;
  }
  state.selectedEnemy = await api(`/api/enemies/${id}`);
  state.enemyDirty = false;
  renderEnemyList();
  renderEnemyEditor();
}
function newEnemy() {
  if (state.enemyDirty && !confirm('目前怪物有尚未儲存的修改，確定要建立新怪物嗎？')) return;
  const max = Math.max(0, ...state.enemies.map(x => x.id));
  state.selectedEnemy = {
    id: max + 1, name: '新怪物', level: 1, sprite: '', maxHp: 100,
    cloneFromId: state.enemies[0]?.id || '', _new: true
  };
  state.enemyDirty = true;
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
  $('#enemy-editor').oninput = () => { state.enemyDirty = true; };
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
    state.enemyDirty = false;
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
    state.enemyDirty = false;
    $('#enemy-editor').innerHTML = emptyEditor('♜','選擇一隻怪物','名稱與預覽優先；Legacy 欄位仍會完整保留。');
    await loadEnemies();
    toast('怪物已刪除');
    await loadStatus();
  } catch (e) { toast(e.message, 'error'); }
}
