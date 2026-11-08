// Backup history / diff / restore
async function loadHistory() {
  const q = $('#history-search')?.value?.trim() || '';
  state.history = await api(`/api/history?q=${encodeURIComponent(q)}&limit=400`);
  renderHistoryList();
  if (state.selectedHistory) {
    const stillExists = state.history.some(item => item.id === state.selectedHistory.id);
    if (!stillExists) clearHistoryDetail();
  }
}

function renderHistoryList() {
  const host = $('#history-list');
  if (!host) return;
  if (!state.history.length) {
    host.innerHTML = '<div class="empty-state compact-empty"><div class="empty-icon">↶</div><h3>目前沒有備份</h3><p>第一次修改資料後，Editor 會自動建立修改前版本。</p></div>';
    return;
  }
  host.innerHTML = state.history.map(item => `
    <button class="history-item ${state.selectedHistory?.id === item.id ? 'active' : ''}" data-history-id="${encodeURIComponent(item.id)}">
      <span class="history-item-main">
        <b title="${esc(item.target)}">${esc(item.target)}</b>
        <small>${esc(item.reason || 'edit')} · ${esc(formatHistoryTime(item.timestamp))}</small>
      </span>
      <span class="history-size">${formatBytes(item.size)}</span>
    </button>
  `).join('');
  $$('.history-item', host).forEach(button => {
    button.onclick = () => openHistory(decodeURIComponent(button.dataset.historyId));
  });
}

async function openHistory(id) {
  try {
    const detail = await api(`/api/history/detail?id=${encodeURIComponent(id)}`);
    state.selectedHistory = detail;
    renderHistoryList();
    renderHistoryDetail();
  } catch (e) {
    toast(e.message, 'error');
  }
}

function renderHistoryDetail() {
  const detail = state.selectedHistory;
  if (!detail) return clearHistoryDetail();
  $('#history-detail-title').textContent = detail.target;
  const status = detail.same
    ? '<span class="badge good">目前內容相同</span>'
    : '<span class="badge neutral">與目前版本不同</span>';
  $('#history-detail-meta').innerHTML = `
    <div><span>備份原因</span><b>${esc(detail.reason || 'edit')}</b></div>
    <div><span>備份大小</span><b>${formatBytes(detail.backupSize)}</b></div>
    <div><span>目前檔案</span><b>${detail.currentExists ? formatBytes(detail.currentSize) : '不存在 / 已刪除'}</b></div>
    <div><span>狀態</span><b>${status}</b></div>
  `;

  const diff = $('#history-diff');
  if (detail.text) {
    diff.classList.remove('binary');
    diff.textContent = detail.diff || (detail.same ? '目前檔案與這份備份完全相同。' : '沒有可顯示的文字差異。');
    if (detail.diffTruncated) diff.textContent += '\n\n… Diff 過長，已截斷顯示。';
  } else {
    diff.classList.add('binary');
    diff.textContent =
      `Binary / large file\n\nBackup SHA256 : ${detail.backupSha256}\nCurrent SHA256: ${detail.currentSha256 || '(missing)'}\n\n`
      + (detail.same ? '兩份內容完全相同。' : '二進位檔不做逐行 Diff；可依 SHA256 與大小確認差異。');
  }

  $('#history-restore').disabled = !!detail.same;
}

function clearHistoryDetail() {
  state.selectedHistory = null;
  const title = $('#history-detail-title');
  if (title) title.textContent = '選擇一筆備份';
  const meta = $('#history-detail-meta');
  if (meta) meta.innerHTML = '';
  const diff = $('#history-diff');
  if (diff) {
    diff.classList.remove('binary');
    diff.textContent = '從左側選擇備份後，這裡會顯示與目前檔案的差異。';
  }
  const restore = $('#history-restore');
  if (restore) restore.disabled = true;
}

async function restoreSelectedHistory() {
  const detail = state.selectedHistory;
  if (!detail) return;
  const warning = detail.currentExists
    ? '目前版本會先再建立一份 restore 備份，因此仍可回復。'
    : '目前檔案不存在，這會重新建立該檔案。';
  if (!confirm(`要把「${detail.target}」還原成這份備份嗎？\n\n${warning}`)) return;
  try {
    const result = await api('/api/history/restore', {
      method:'POST',
      body:{id:detail.id},
    });
    toast(`已還原 ${result.target}`);
    state.selectedHistory = null;
    await Promise.all([loadHistory(), loadStatus()]);
    loadHealth(true).catch(() => {});
  } catch (e) {
    toast(e.message, 'error');
  }
}

function formatHistoryTime(value) {
  if (!value) return '未知時間';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString('zh-TW', {hour12:false});
}

$('#history-search')?.addEventListener('input', debounce(() => {
  loadHistory().catch(e => toast(e.message, 'error'));
}, 180));
$('#history-refresh')?.addEventListener('click', () => {
  loadHistory().catch(e => toast(e.message, 'error'));
});
$('#history-restore')?.addEventListener('click', restoreSelectedHistory);
