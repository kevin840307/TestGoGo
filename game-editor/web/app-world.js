// Enemies
async function loadEnemies() {
  state.enemies = await api(`/api/enemies?q=${encodeURIComponent($('#enemy-search').value.trim())}`);
  renderEnemyList();
}
function renderEnemyList(){ const b=$('#enemy-list'); b.innerHTML=state.enemies.map(e=>`<tr data-id="${e.id}" class="${state.selectedEnemy?.id===e.id?'selected':''}"><td>${e.id}</td><td><div class="row-title">${esc(e.name)}</div><div class="row-sub">${esc(e.sprite)}</div></td><td>${e.level}</td><td>${e.maxHp}</td></tr>`).join(''); $$('tr[data-id]',b).forEach(r=>r.onclick=()=>openEnemy(+r.dataset.id)); }
async function openEnemy(id){state.selectedEnemy=await api(`/api/enemies/${id}`);renderEnemyList();renderEnemyEditor();}
function newEnemy(){const max=Math.max(0,...state.enemies.map(x=>x.id));state.selectedEnemy={id:max+1,name:'新怪物',level:1,sprite:'',maxHp:100,cloneFromId:state.enemies[0]?.id||'',_new:true};renderEnemyList();renderEnemyEditor();}
function renderEnemyEditor(){const e=state.selectedEnemy;if(!e)return;$('#enemy-editor').innerHTML=`<div class="editor-inner"><div class="editor-heading"><div><h2>${esc(e.name)}</h2><p>${e._new?'新增怪物：建議 Clone 現有怪物保留 legacy 欄位':`CSV ${e.columnCount||''} columns`}</p></div><div class="editor-actions"><button class="button danger" id="enemy-delete" ${e._new?'disabled':''}>刪除</button><button class="button primary" id="enemy-save">儲存</button></div></div><div class="form-grid">${field('enemy-id','Enemy ID',e.id,'number')}${field('enemy-name','名稱',e.name)}${field('enemy-level','Level',e.level,'number')}${field('enemy-hp','Max HP',e.maxHp,'number')}${field('enemy-sprite','Sprite',e.sprite)}${e._new?field('enemy-clone','Clone From ID',e.cloneFromId||'','number'):''}</div><div class="validation-box muted" style="margin-top:16px">Editor 只改 ID / 名稱 / Level / Sprite / MaxHP；其餘 CSV 欄位原樣保留。新增怪物時建議 Clone 一隻行為相近的既有怪物。</div></div>`;$('#enemy-save').onclick=saveEnemy;$('#enemy-delete').onclick=deleteEnemy;}
async function saveEnemy(){try{const e=state.selectedEnemy;const id=Number($('#enemy-id').value);if(id!==e.id&&!e._new)throw new Error('既有怪物不允許直接改 ID');const doc={name:$('#enemy-name').value.trim(),level:+$('#enemy-level').value,maxHp:+$('#enemy-hp').value,sprite:$('#enemy-sprite').value.trim(),cloneFromId:e._new?($('#enemy-clone').value||null):null};state.selectedEnemy=await api(`/api/enemies/${id}`,{method:'PUT',body:doc});toast('怪物已儲存');await loadEnemies();renderEnemyEditor();await loadStatus();}catch(e){toast(e.message,'error');}}
async function deleteEnemy(){if(!confirm(`確定刪除怪物 ${state.selectedEnemy.id}？地圖若仍引用它會失效。`))return;try{await api(`/api/enemies/${state.selectedEnemy.id}`,{method:'DELETE'});state.selectedEnemy=null;$('#enemy-editor').innerHTML=emptyEditor('♜','選擇一隻怪物','只改常用欄位，其他 legacy CSV 欄位會保留。');await loadEnemies();toast('怪物已刪除');await loadStatus();}catch(e){toast(e.message,'error');}}

// Maps
async function loadMaps(){
  const old=state.mapId; state.maps=await api('/api/maps');
  $('#map-select').innerHTML=state.maps.map(m=>`<option value="${m.id}">${esc(m.name)} · ${m.enemyCount} monsters · ${m.npcCount} NPC</option>`).join('');
  if(old&&state.maps.some(x=>x.id===old)) state.mapId=old; else state.mapId=state.maps[0]?.id||null;
  if(state.mapId) $('#map-select').value=state.mapId;
  fillScriptMapSelect(); await loadMapRows();
}
async function loadMapRows(){if(!state.mapId)return;state.mapRows=await api(`/api/maps/${state.mapId}/${state.mapTab}`);renderMapTable();}
function renderMapTable(){
  const enemy=state.mapTab==='enemies';
  $('#map-head').innerHTML=enemy?'<tr><th>#</th><th>Enemy ID</th><th>X</th><th>Y</th><th>Source Flags</th><th></th></tr>':'<tr><th>#</th><th>NPC Type</th><th>Name</th><th>Script</th><th>X</th><th>Y</th><th></th></tr>';
  $('#map-body').innerHTML=state.mapRows.map((r,i)=>enemy?`<tr data-i="${i}"><td>${i}</td><td><input data-k="enemyId" type="number" value="${r.enemyId}"></td><td><input data-k="x" type="number" value="${r.x}"></td><td><input data-k="y" type="number" value="${r.y}"></td><td><input data-k="sourceFlags" type="number" value="${r.sourceFlags}"></td><td><button class="icon-button row-delete">×</button></td></tr>`:`<tr data-i="${i}"><td>${i}</td><td><input data-k="npcTypeId" type="number" value="${r.npcTypeId}"></td><td><input data-k="name" value="${esc(r.name)}"></td><td><input data-k="script" value="${esc(r.script)}" title="點兩下開啟腳本"></td><td><input data-k="x" type="number" value="${r.x}"></td><td><input data-k="y" type="number" value="${r.y}"></td><td><button class="icon-button row-delete">×</button></td></tr>`).join('');
  $$('#map-body tr').forEach((tr,i)=>{ $$('input[data-k]',tr).forEach(inp=>{inp.oninput=()=>state.mapRows[i][inp.dataset.k]=inp.type==='number'?Number(inp.value||0):inp.value;if(inp.dataset.k==='script')inp.ondblclick=()=>openScriptFromMap(inp.value);}); $('.row-delete',tr).onclick=()=>{state.mapRows.splice(i,1);renderMapTable();}; });
  const m=state.maps.find(x=>x.id===state.mapId);$('#map-meta').textContent=`${m?.name||''} · ${state.mapRows.length} ${enemy?'monster spawns':'NPC records'} · 新增 Enemy 記錄會使用 207-byte 零值 payload；複製既有列可保留 payload。`;
}
async function saveMap(){try{state.mapRows=await api(`/api/maps/${state.mapId}/${state.mapTab}`,{method:'PUT',body:state.mapRows});toast('地圖資料已儲存');await loadMaps();}catch(e){toast(e.message,'error');}}
function addMapRow(){if(state.mapTab==='enemies'){state.mapRows.push({index:-1,enemyId:1,x:0,y:0,sourceFlags:0});}else{state.mapRows.push({index:-1,npcTypeId:1,name:'New NPC',script:'NewNpc.txt',x:0,y:0});}renderMapTable();}
async function openScriptFromMap(name){if(!name)return;await go('scripts');await openScript(name);}

