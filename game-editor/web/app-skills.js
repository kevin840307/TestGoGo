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
      <td>${s.id}</td><td><div class="row-title">${esc(s.name)}</div><div class="row-sub">icon ${s.iconId}</div></td>
      <td>${s.skillType}/${s.damageType}</td><td>${s.levelCount}</td>
    </tr>`).join('');
  $$('tr[data-id]', body).forEach(row => row.onclick = () => openSkill(Number(row.dataset.id)));
}
async function openSkill(id) {
  state.selectedSkill = await api(`/api/skills/${id}`);
  renderSkillList(); renderSkillEditor();
}
function blankLevel(level=1) {
  return {level, castRange:0, aoeRadius:0, costHp:0, costMp:0, costSp:0, baseDamage:0, damageMultiplier:100, cooldown:0, duration:0, tickCount:1, maxHpBonus:0, maxMpBonus:0, maxSpBonus:0, atkBonus:0, defBonus:0, matkBonus:0, mdefBonus:0, hitBonus:0, fleeBonus:0, lukBonus:0, aspdBonus:0, moveSpeedBonus:0, critRateBonus:0, critDamageBonus:0};
}
function newSkill() {
  const maxId = Math.max(0, ...state.skills.map(x => x.id));
  state.selectedSkill = {id:maxId+1,name:'新技能',description:'',skillType:0,damageType:0,iconId:0,fxUse:'',fxHit:'',fxExtra:'',specialSkill:'',levels:[blankLevel(1)],_new:true};
  renderSkillList(); renderSkillEditor();
}
const LEVEL_FIELDS = [
  ['castRange','攻擊距離'],['aoeRadius','AOE 半徑'],['costHp','HP 消耗'],['costMp','MP 消耗'],['costSp','SP 消耗'],
  ['baseDamage','基礎傷害'],['damageMultiplier','倍率 %'],['cooldown','CD 秒'],['duration','持續秒'],['tickCount','Tick'],
  ['maxHpBonus','Max HP+'],['maxMpBonus','Max MP+'],['maxSpBonus','Max SP+'],['atkBonus','ATK+'],['defBonus','DEF+'],
  ['matkBonus','MATK+'],['mdefBonus','MDEF+'],['hitBonus','HIT+'],['fleeBonus','FLEE+'],['lukBonus','LUK+'],
  ['aspdBonus','ASPD+'],['moveSpeedBonus','Move+'],['critRateBonus','Crit Rate+'],['critDamageBonus','Crit Dmg+'],
];
function renderSkillEditor() {
  const s = state.selectedSkill;
  const host = $('#skill-editor');
  if (!s) return;
  host.innerHTML = `<div class="editor-inner">
    <div class="editor-heading"><div><h2>${esc(s.name)}</h2><p>${s._new ? '新技能，尚未寫入檔案' : `Skill ${s.id}`}</p></div>
      <div class="editor-actions"><button class="button danger" id="skill-delete" ${s._new?'disabled':''}>刪除</button><button class="button primary" id="skill-save">儲存</button></div></div>
    <div class="form-grid">
      ${field('skill-id','ID',s.id,'number')}${field('skill-name','名稱',s.name)}
      <div class="field full"><label>描述</label><textarea id="skill-description">${esc(s.description)}</textarea></div>
      ${field('skill-type','Skill Type',s.skillType,'number')}${field('skill-damage-type','Damage Type',s.damageType,'number')}
      ${field('skill-icon','Icon ID',s.iconId,'number')}${field('skill-special','Special Skill',s.specialSkill)}
      ${field('skill-fx-use','FX Use',s.fxUse)}${field('skill-fx-hit','FX Hit',s.fxHit)}
      ${field('skill-fx-extra','FX Extra',s.fxExtra)}
    </div>
    <div class="section-title"><h3>等級參數</h3><button class="button ghost" id="skill-add-level">＋ 新增等級</button></div>
    <div id="skill-levels"></div>
  </div>`;
  renderLevels(s.levels);
  $('#skill-add-level').onclick = () => { s.levels.push(blankLevel(Math.max(0,...s.levels.map(x=>+x.level))+1)); renderLevels(s.levels); };
  $('#skill-save').onclick = saveSkill;
  $('#skill-delete').onclick = deleteSkill;
}
function field(id,label,value,type='text') { return `<div class="field"><label for="${id}">${label}</label><input id="${id}" type="${type}" value="${esc(value ?? '')}" /></div>`; }
function renderLevels(levels) {
  const host = $('#skill-levels');
  host.innerHTML = levels.map((lv,i)=>`<div class="level-card" data-level-index="${i}">
    <div class="level-card-head"><strong>Level <input class="inline-level" data-k="level" type="number" min="1" value="${+lv.level}" /></strong><button class="icon-button level-remove" title="刪除此等級">×</button></div>
    <div class="level-grid">${LEVEL_FIELDS.map(([k,label])=>`<div class="field"><label>${label}</label><input data-k="${k}" type="number" value="${Number(lv[k]??0)}" /></div>`).join('')}</div>
  </div>`).join('');
  $$('.level-card',host).forEach((card,i)=>{
    $$('input[data-k]',card).forEach(inp=> inp.oninput=()=>{ levels[i][inp.dataset.k]=Number(inp.value||0); });
    $('.level-remove',card).onclick=()=>{ if(levels.length<=1) return toast('技能至少需要一個等級','error'); levels.splice(i,1); renderLevels(levels); };
  });
}
function collectSkill() {
  const s = state.selectedSkill;
  return {...s,
    id:Number($('#skill-id').value), name:$('#skill-name').value.trim(), description:$('#skill-description').value,
    skillType:Number($('#skill-type').value||0), damageType:Number($('#skill-damage-type').value||0), iconId:Number($('#skill-icon').value||0),
    specialSkill:$('#skill-special').value, fxUse:$('#skill-fx-use').value, fxHit:$('#skill-fx-hit').value, fxExtra:$('#skill-fx-extra').value,
    levels:s.levels
  };
}
async function saveSkill() {
  try {
    const doc = collectSkill();
    if (doc.id !== state.selectedSkill.id && !state.selectedSkill._new) throw new Error('既有技能不允許直接改 ID；請新增後刪除舊技能');
    state.selectedSkill = await api(`/api/skills/${doc.id}`, {method:'PUT', body:doc});
    toast('技能已儲存'); await loadSkills(); renderSkillEditor(); await loadStatus();
  } catch(e) { toast(e.message,'error'); }
}
async function deleteSkill() {
  if(!confirm(`確定刪除技能 ${state.selectedSkill.id}？刪除前仍會建立備份。`)) return;
  try { await api(`/api/skills/${state.selectedSkill.id}`,{method:'DELETE'}); state.selectedSkill=null; $('#skill-editor').innerHTML=emptyEditor('✦','選擇一個技能','右側會顯示技能資訊與每級參數。'); await loadSkills(); toast('技能已刪除'); await loadStatus(); } catch(e){toast(e.message,'error');}
}

