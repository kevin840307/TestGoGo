const state={status:null,events:[],refreshing:false,anomalyOnly:false,currentPage:'overview'};
const pageMeta={
 overview:['總覽','伺服器健康狀態與多人運作摘要'],
 analysis:['分析','Runtime、Shared Map AI 與診斷計數'],
 gm:['GM 小工具','管理操作集中區，不與監控資訊混在一起'],
 players:['玩家','目前在線多人 session 與所在位置'],
 logs:['Log / 異常','快速篩選 Warning / Error 與玩家情境']
};
const $=id=>document.getElementById(id);
const esc=v=>String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmtUptime=v=>{let s=Math.max(0,Math.floor(Number(v)||0));const d=Math.floor(s/86400);s%=86400;const h=Math.floor(s/3600);s%=3600;const m=Math.floor(s/60);return(d?d+'d ':'')+(h?h+'h ':'')+m+'m'};
const metric=(label,value,foot='',kind='')=>'<div class="metric '+kind+'"><div class="metric-label">'+esc(label)+'</div><div class="metric-value">'+esc(value)+'</div><div class="metric-foot">'+esc(foot)+'</div></div>';
const row=(k,v,cls='')=>'<div>'+esc(k)+'</div><div class="'+cls+'">'+esc(v)+'</div>';

function switchPage(page){
 state.currentPage=page;
 document.querySelectorAll('.menu-item').forEach(x=>x.classList.toggle('active',x.dataset.page===page));
 document.querySelectorAll('.page').forEach(x=>x.classList.toggle('active',x.dataset.pageView===page));
 $('pageTitle').textContent=pageMeta[page][0];$('pageSubtitle').textContent=pageMeta[page][1];
}
function eventContext(x){
 const parts=[];if(x.characterId!=null)parts.push('Char:'+x.characterId);if(x.accountId!=null)parts.push('Acc:'+x.accountId);
 if(x.mapId!=null)parts.push('Map:'+x.mapId);if(x.connectionId)parts.push('Conn:'+String(x.connectionId).slice(0,8));return parts.join(' · ');
}
function renderEvents(){
 const level=$('eventLevel').value.toLowerCase(),q=$('eventSearch').value.trim().toLowerCase();
 const rows=state.events.filter(x=>{const lv=String(x.level||'Information').toLowerCase();const abnormal=['critical','error','warning'].includes(lv);const hay=(x.category+' '+x.message+' '+eventContext(x)).toLowerCase();return(!state.anomalyOnly||abnormal)&&(!level||lv===level)&&(!q||hay.includes(q))});
 $('events').innerHTML=rows.length?rows.map(x=>{const lv=String(x.level||'Information').toLowerCase();return'<tr><td class="mono">#'+esc(x.sequence)+' · '+esc(new Date(x.timestamp).toLocaleTimeString())+'</td><td><span class="level level-'+esc(lv)+'">'+esc(x.level)+'</span></td><td>'+esc(x.category)+'</td><td class="mono">'+esc(eventContext(x)||'-')+'</td><td>'+esc(x.message)+'</td><td class="mono">'+esc(x.elapsedMilliseconds==null?'':Number(x.elapsedMilliseconds).toFixed(1)+' ms')+'</td></tr>'}).join(''):'<tr><td colspan="6" class="empty-state">沒有符合條件的事件</td></tr>';
}
function renderStatus(s){
 const c=s.counters||{},ai=s.sharedMapAi||{},issueCount=(c.packetErrors||0)+(c.databaseFailures||0)+(c.worldTickFailures||0);
 $('summary').innerHTML=[
  metric('Online Players',s.onlinePlayers,'active sessions'),
  metric('Working Set',s.runtime.workingSetMb+' MB','process memory',s.runtime.workingSetMb>=1024?'warn':''),
  metric('AI Tick',Number(ai.elapsedMs||0).toFixed(1)+' ms','target < 250 ms',ai.elapsedMs>250?'bad':''),
  metric('Errors',issueCount,'packet / DB / tick',issueCount?'bad':'')
 ].join('');
 $('playerBadge').textContent=s.onlinePlayers+' players';$('playersPageBadge').textContent=s.onlinePlayers+' players';
 $('maps').innerHTML=s.maps.length?s.maps.map(x=>'<div class="map-tile"><span>Map '+esc(x.mapId)+'</span><strong>'+esc(x.players)+'</strong></div>').join(''):'<div class="empty-state">目前無活躍地圖</div>';
 $('players').innerHTML=s.players.length?s.players.map(p=>'<tr><td>'+esc(p.characterId)+'</td><td>'+esc(p.accountId)+'</td><td>'+esc(p.mapId)+'</td><td class="mono">'+esc(p.x)+', '+esc(p.y)+'</td><td class="mono">'+esc(p.connectionId)+'</td></tr>').join(''):'<tr><td colspan="5" class="empty-state">目前沒有在線玩家</td></tr>';
 const healthBad=issueCount>0||(ai.elapsedMs||0)>250||(c.resourceWarnings||0)>0;
 $('healthBadge').textContent=healthBad?'Attention':'Healthy';$('healthBadge').className='pill '+(healthBad?'warning':'good');
 $('healthSummary').innerHTML=row('Packet errors',c.packetErrors||0,c.packetErrors?'danger-text':'good-text')+row('DB failures',c.databaseFailures||0,c.databaseFailures?'danger-text':'good-text')+row('World tick failures',c.worldTickFailures||0,c.worldTickFailures?'danger-text':'good-text')+row('Resource warnings',c.resourceWarnings||0,c.resourceWarnings?'warn-text':'good-text');
 $('uptime').textContent=fmtUptime(s.uptimeSeconds);
 $('runtime').innerHTML=row('Working set',s.runtime.workingSetMb+' MB')+row('Private memory',s.runtime.privateMemoryMb+' MB')+row('GC heap',s.runtime.gcHeapMb+' MB')+row('Threads',s.runtime.threads)+row('GC Gen 0 / 1 / 2',s.runtime.gcGen0+' / '+s.runtime.gcGen1+' / '+s.runtime.gcGen2)+row('CPU total',Math.round(s.runtime.cpuTotalMilliseconds/1000)+' sec');
 $('aiHealth').textContent=(ai.elapsedMs||0)<=250?'Healthy':'Slow';$('aiHealth').className='pill '+((ai.elapsedMs||0)<=250?'good':'warning');
 $('ai').innerHTML=row('Active sessions',ai.activeSessions||0)+row('Active maps',ai.activeMaps||0)+row('Ticked maps',ai.tickedMaps||0)+row('Monsters',ai.monsterCount||0)+row('Assigned monsters',ai.assignedMonsterCount||0)+row('Last tick',Number(ai.elapsedMs||0).toFixed(1)+' ms');
 $('diagnosticCounters').innerHTML=row('Slow packets',c.slowPackets||0,c.slowPackets?'warn-text':'good-text')+row('AI slow ticks',c.mapAiSlowTicks||0,c.mapAiSlowTicks?'warn-text':'good-text')+row('Map pressure',c.mapPressureWarnings||0,c.mapPressureWarnings?'warn-text':'good-text')+row('Broadcast backlog',c.broadcastBacklogWarnings||0,c.broadcastBacklogWarnings?'warn-text':'good-text')+row('Trade conflicts',c.tradeConflicts||0,c.tradeConflicts?'warn-text':'good-text')+row('Pickup conflicts',c.pickupConflicts||0,c.pickupConflicts?'warn-text':'good-text');
 $('dataHealth').textContent=s.gameData.available?'Available':'Missing';$('dataHealth').className='pill '+(s.gameData.available?'good':'danger');
 $('gameData').innerHTML=row('Items',s.gameData.items)+row('Skills',s.gameData.skills)+row('Pets',s.gameData.pets)+row('Root',s.gameData.root||'--');
 $('sidebarStatus').textContent='Live · '+new Date(s.serverTime).toLocaleTimeString();$('lastUpdated').textContent='更新 '+new Date().toLocaleTimeString();
}
async function refresh(){
 if(state.refreshing||document.hidden)return;state.refreshing=true;
 try{
  const [sr,er]=await Promise.all([fetch('/admin/api/status',{cache:'no-store'}),fetch('/admin/api/events?count=150',{cache:'no-store'})]);
  if(!sr.ok||!er.ok)throw new Error('HTTP '+sr.status+'/'+er.status);
  state.status=await sr.json();state.events=await er.json();renderStatus(state.status);renderEvents();
 }catch(e){$('sidebarStatus').textContent='Disconnected · '+e.message}finally{state.refreshing=false}
}
async function sendBroadcast(){
 const el=$('broadcast'),out=$('broadcastResult'),message=el.value.trim();if(!message){out.textContent='請輸入公告內容';return}
 try{const r=await fetch('/admin/api/broadcast',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({message})});if(!r.ok){const x=await r.json().catch(()=>({}));throw new Error(x.error||('HTTP '+r.status))}el.value='';out.textContent='公告已送出';await refresh()}catch(e){out.textContent='送出失敗：'+e.message}
}
document.querySelectorAll('.menu-item').forEach(x=>x.addEventListener('click',()=>switchPage(x.dataset.page)));
$('refreshBtn').addEventListener('click',refresh);$('eventLevel').addEventListener('change',renderEvents);$('eventSearch').addEventListener('input',renderEvents);
$('anomalyOnly').addEventListener('click',()=>{state.anomalyOnly=!state.anomalyOnly;$('anomalyOnly').textContent=state.anomalyOnly?'顯示全部':'只看異常';renderEvents()});
$('broadcastBtn').addEventListener('click',sendBroadcast);$('broadcast').addEventListener('keydown',e=>{if(e.key==='Enter')sendBroadcast()});
refresh();setInterval(refresh,10000);document.addEventListener('visibilitychange',()=>{if(!document.hidden)refresh()});
