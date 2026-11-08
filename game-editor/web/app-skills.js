// Skills
async function loadSkills() {
  const q = $('#skill-search').value.trim();
  state.skills = await api(`/api/skills?q=${encodeURIComponent(q)}`);
  renderSkillList();
}
function renderSkillList() {
  const body = $('#skill-list');
  body.innerHTML = state.skills.map(s => `
    <tr data-id="${s.id}" class="${state.selectedSkill?.id === s.id ? 'selected' : ''}">
      <td>${s.id}</td>
      <td><div class="row-title">${esc(s.name)}</div><div class="row-sub">Icon ${s.iconId}</div></td>
      <td>${s.skillType}/${s.damageType}</td><td>${s.levelCount}</td>
    </tr>`).join('');
  $$('tr[data-id]', body).forEach(row => row.onclick = () => openSkill(Number(row.dataset.id)));
}
async function openSkill(id) {
  if (state.skillDirty) {
    if (state.selectedSkill?.id === id) return;
    if (!confirm('目前技能有尚未儲存的修改，確定要切換嗎？')) return;
  }
  state.selectedSkill = await api(`/api/skills/${id}`);
  state.skillDirty = false;
  renderSkillList();
  renderSkillEditor();
}
function blankLevel(level=1) {
  return {
    level, castRange:0, aoeRadius:0, costHp:0, costMp:0, costSp:0,
    baseDamage:0, damageMultiplier:100, cooldown:0, duration:0, tickCount:1,
    maxHpBonus:0, maxMpBonus:0, maxSpBonus:0, atkBonus:0, defBonus:0,
    matkBonus:0, mdefBonus:0, hitBonus:0, fleeBonus:0, lukBonus:0,
    aspdBonus:0, moveSpeedBonus:0, critRateBonus:0, critDamageBonus:0
  };
}
function newSkill() {
  if (state.skillDirty && !confirm('目前技能有尚未儲存的修改，確定要建立新技能嗎？')) return;
  const maxId = Math.max(0, ...state.skills.map(x => x.id));
  state.selectedSkill = {
    id:maxId+1, name:'新技能', description:'', skillType:0, damageType:0, iconId:0,
    fxUse:'', fxHit:'', fxExtra:'', specialSkill:'', levels:[blankLevel(1)], _new:true
  };
  state.skillDirty = true;
  renderSkillList();
  renderSkillEditor();
}
const COMMON_LEVEL_FIELDS = [
  ['baseDamage','基礎傷害'],
  ['damageMultiplier','傷害倍率 %'],
  ['costMp','MP 消耗'],
  ['cooldown','冷卻時間（秒）'],
  ['castRange','施放距離'],
  ['aoeRadius','範圍半徑'],
  ['duration','持續時間（秒）'],
  ['tickCount','作用次數'],
];
const ADVANCED_LEVEL_FIELDS = [
  ['costHp','HP 消耗'],['costSp','SP 消耗'],
  ['maxHpBonus','Max HP+'],['maxMpBonus','Max MP+'],['maxSpBonus','Max SP+'],
  ['atkBonus','ATK+'],['defBonus','DEF+'],['matkBonus','MATK+'],['mdefBonus','MDEF+'],
  ['hitBonus','HIT+'],['fleeBonus','FLEE+'],['lukBonus','LUK+'],['aspdBonus','ASPD+'],
  ['moveSpeedBonus','Move+'],['critRateBonus','Crit Rate+'],['critDamageBonus','Crit Dmg+'],
];
function renderSkillEditor() {
  const s = state.selectedSkill;
  const host = $('#skill-editor');
  if (!s) return;
  host.innerHTML = `
    <div class="editor-inner">
      <div class="editor-heading">
        <div><h2>${esc(s.name)}</h2><p>${s._new ? '新技能，尚未寫入檔案' : `Skill #${s.id}`}</p></div>
        <div class="editor-actions"><button class="button danger" id="skill-delete" ${s._new ? 'disabled' : ''}>刪除</button><button class="button primary" id="skill-save">儲存</button></div>
      </div>

      <div class="section-label">常用資料</div>
      <div class="form-grid">
        ${field('skill-name','技能名稱',s.name)}
        ${field('skill-id','Skill ID',s.id,'number')}
        <div class="field full"><label>玩家看到的描述</label><textarea id="skill-description">${esc(s.description)}</textarea></div>
        ${field('skill-icon','圖示 ID',s.iconId,'number')}
        ${field('skill-special','特殊行為',s.specialSkill)}
      </div>

      <details class="editor-details">
        <summary>進階：類型與特效設定</summary>
        <div class="form-grid">
          ${field('skill-type','Skill Type',s.skillType,'number')}
          ${field('skill-damage-type','Damage Type',s.damageType,'number')}
          ${field('skill-fx-use','施放特效 FX Use',s.fxUse)}
          ${field('skill-fx-hit','命中特效 FX Hit',s.fxHit)}
          ${field('skill-fx-extra','額外特效 FX Extra',s.fxExtra)}
        </div>
        <p class="helper">一般調數值不需要碰這區；只有技能類型或特效要改時才需要。</p>
      </details>

      <div class="section-title">
        <div><h3>技能等級</h3><span class="helper">常用戰鬥數值直接顯示，角色屬性加成收在進階區。</span></div>
        <button class="button ghost" id="skill-add-level">＋ 新增等級</button>
      </div>
      <div id="skill-levels"></div>
    </div>`;
  renderLevels(s.levels);
  host.oninput = () => { state.skillDirty = true; };
  $('#skill-add-level').onclick = () => {
    const nextLevel = Math.max(0, ...s.levels.map(x => Number(x.level))) + 1;
    const previous = s.levels.at(-1);
    s.levels.push(previous ? {...previous, level:nextLevel} : blankLevel(nextLevel));
    state.skillDirty = true;
    renderLevels(s.levels);
    toast(previous ? '已複製上一級數值，可直接微調' : '已新增等級');
  };
  $('#skill-save').onclick = saveSkill;
  $('#skill-delete').onclick = deleteSkill;
}
function field(id, label, value, type='text') {
  return `<div class="field"><label for="${id}">${label}</label><input id="${id}" type="${type}" value="${esc(value ?? '')}" /></div>`;
}
function levelFields(fields, lv) {
  return fields.map(([k,label]) => `<div class="field"><label>${label}</label><input data-k="${k}" type="number" value="${Number(lv[k] ?? 0)}" /></div>`).join('');
}
function renderLevels(levels) {
  const host = $('#skill-levels');
  host.innerHTML = levels.map((lv,i) => `
    <div class="level-card" data-level-index="${i}">
      <div class="level-card-head">
        <strong>Level <input class="inline-level" data-k="level" type="number" min="1" value="${Number(lv.level)}" /></strong>
        <div class="level-actions">
          <button class="button ghost level-duplicate">複製此級</button>
          <button class="icon-button level-remove" title="刪除此等級">×</button>
        </div>
      </div>
      <div class="level-grid common-level-grid">${levelFields(COMMON_LEVEL_FIELDS, lv)}</div>
      <details class="inline-advanced level-advanced">
        <summary>進階屬性加成</summary>
        <div class="level-grid">${levelFields(ADVANCED_LEVEL_FIELDS, lv)}</div>
      </details>
    </div>`).join('');

  $$('.level-card', host).forEach((card, i) => {
    $$('input[data-k]', card).forEach(inp => inp.oninput = () => {
      levels[i][inp.dataset.k] = Number(inp.value || 0);
      state.skillDirty = true;
    });
    $('.level-duplicate', card).onclick = () => {
      const clone = {...levels[i], level:Math.max(0, ...levels.map(x => Number(x.level))) + 1};
      levels.splice(i + 1, 0, clone);
      state.skillDirty = true;
      renderLevels(levels);
    };
    $('.level-remove', card).onclick = () => {
      if (levels.length <= 1) return toast('技能至少需要一個等級', 'error');
      levels.splice(i, 1);
      state.skillDirty = true;
      renderLevels(levels);
    };
  });
}
function collectSkill() {
  const s = state.selectedSkill;
  return {
    ...s,
    id:Number($('#skill-id').value),
    name:$('#skill-name').value.trim(),
    description:$('#skill-description').value,
    skillType:Number($('#skill-type').value || 0),
    damageType:Number($('#skill-damage-type').value || 0),
    iconId:Number($('#skill-icon').value || 0),
    specialSkill:$('#skill-special').value,
    fxUse:$('#skill-fx-use').value,
    fxHit:$('#skill-fx-hit').value,
    fxExtra:$('#skill-fx-extra').value,
    levels:s.levels,
  };
}
async function saveSkill() {
  try {
    const doc = collectSkill();
    if (doc.id !== state.selectedSkill.id && !state.selectedSkill._new) {
      throw new Error('既有技能不允許直接改 ID；請新增後刪除舊技能');
    }
    state.selectedSkill = await api(`/api/skills/${doc.id}`, {method:'PUT', body:doc});
    state.skillDirty = false;
    toast('技能已儲存');
    await loadSkills();
    renderSkillEditor();
    await loadStatus();
  } catch (e) { toast(e.message, 'error'); }
}
async function deleteSkill() {
  if (!confirm(`確定刪除技能 ${state.selectedSkill.id}？刪除前仍會建立備份。`)) return;
  try {
    await api(`/api/skills/${state.selectedSkill.id}`, {method:'DELETE'});
    state.selectedSkill = null;
    state.skillDirty = false;
    $('#skill-editor').innerHTML = emptyEditor('✦','選擇一個技能','常用欄位會先顯示，進階設定需要時再展開。');
    await loadSkills();
    toast('技能已刪除');
    await loadStatus();
  } catch (e) { toast(e.message, 'error'); }
}
