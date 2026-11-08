// Project health / cross-file validation
async function loadHealth(force=false) {
  const host = $('#health-list');
  if (host) host.innerHTML = '<span class="muted">正在掃描跨檔案引用、Blocking 與腳本…</span>';
  try {
    state.health = await api(`/api/health${force ? '?refresh=1' : ''}`);
    refreshHealthCategoryOptions();
    renderHealth();
    return state.health;
  } catch (e) {
    if (host) host.innerHTML = `<div class="health-empty bad">健康檢查失敗：${esc(e.message)}</div>`;
    throw e;
  }
}

function renderHealth() {
  const health = state.health;
  if (!health) return;
  $('#health-errors').textContent = health.summary.errors;
  $('#health-warnings').textContent = health.summary.warnings;
  $('#health-total').textContent = health.summary.issues;

  const host = $('#health-list');
  if (!host) return;
  if (!health.issues.length) {
    host.innerHTML = '<div class="health-empty good">✓ 目前沒有發現跨檔案資料問題。</div>';
    return;
  }

  const query = ($('#health-search')?.value || '').trim().toLowerCase();
  const severity = $('#health-severity')?.value || '';
  const category = $('#health-category')?.value || '';
  const filtered = health.issues
    .map((issue, index) => ({issue, index}))
    .filter(({issue}) => {
      if (severity && issue.severity !== severity) return false;
      if (category && issue.category !== category) return false;
      if (!query) return true;
      const targetText = issue.target ? JSON.stringify(issue.target) : '';
      const haystack = [
        issue.severity, issue.category, issue.title, issue.detail, targetText,
      ].join(' ').toLowerCase();
      return haystack.includes(query);
    });

  if (!filtered.length) {
    host.innerHTML = '<div class="health-empty">沒有符合目前篩選條件的問題。</div>';
    return;
  }

  host.innerHTML = filtered.slice(0, 120).map(({issue, index}) => {
    const target = issue.target || {};
    const canOpen = !!target.view;
    const repair = issue.suggestion
      ? `<div class="health-repair">建議：(${issue.suggestion.from.x}, ${issue.suggestion.from.y}) → (${issue.suggestion.to.x}, ${issue.suggestion.to.y})</div>`
      : '';
    return `
      <div class="health-issue ${esc(issue.severity)}">
        <div class="health-issue-main">
          <span class="health-severity">${issue.severity === 'error' ? 'ERROR' : 'WARN'}</span>
          <div>
            <b>${esc(issue.title)}</b>
            <span>${esc(issue.detail || '')}</span>
            ${repair}
          </div>
        </div>
        ${canOpen ? `<button class="button ghost health-open" data-i="${index}">前往</button>` : ''}
      </div>`;
  }).join('')
    + `<div class="helper">顯示 ${Math.min(filtered.length, 120)} / ${filtered.length} 筆篩選結果</div>`
    + (health.truncated ? '<div class="helper">Health 掃描結果過多，來源清單已截斷；Summary 仍包含完整數量。</div>' : '');

  $$('.health-open', host).forEach(button => {
    button.onclick = () => openHealthIssue(Number(button.dataset.i));
  });
}

function refreshHealthCategoryOptions() {
  const select = $('#health-category');
  if (!select || !state.health) return;
  const current = select.value;
  const categories = Object.keys(state.health.summary?.categories || {}).sort();
  select.innerHTML = '<option value="">全部類型</option>' + categories.map(category => {
    const count = state.health.summary.categories[category];
    return `<option value="${esc(category)}">${esc(category)} (${count})</option>`;
  }).join('');
  if (categories.includes(current)) select.value = current;
}

async function openHealthIssue(index) {
  const issue = state.health?.issues?.[index];
  const target = issue?.target;
  if (!target) return;

  if (target.view === 'maps') {
    state.mapId = Number(target.mapId);
    state.mapTab = target.tab || 'enemies';
    const changed = await go('maps');
    if (changed === false) return;
    $$('[data-map-tab]').forEach(x => x.classList.toggle('active', x.dataset.mapTab === state.mapTab));
    if (Number.isInteger(Number(target.index))) {
      const rowIndex = Number(target.index);
      if (rowIndex >= 0 && rowIndex < state.mapRows.length) {
        setMapSelection([rowIndex], rowIndex);
        renderMapTable();
        renderMapVisual();
      }
    }
    return;
  }

  if (target.view === 'scripts' && target.script) {
    const changed = await go('scripts');
    if (changed === false) return;
    await openScript(target.script);
    return;
  }

  if (target.view === 'skills') {
    const changed = await go('skills');
    if (changed === false) return;
    if (target.id) await openSkill(Number(target.id));
  }
}

async function referenceImpact(kind, key) {
  return api(`/api/references/${encodeURIComponent(kind)}/${encodeURIComponent(key)}`);
}

function formatReferenceImpact(report) {
  if (!report?.count) return '';
  const examples = report.references.slice(0, 6).map(ref => {
    if (ref.type === 'map_enemy') return `Map${String(ref.mapId).padStart(2,'0')} Enemy #${ref.index + 1} (${ref.x},${ref.y})`;
    if (ref.type === 'map_npc') return `Map${String(ref.mapId).padStart(2,'0')} ${ref.name || 'NPC'} (${ref.x},${ref.y})`;
    if (ref.type === 'quest_include') return `${ref.script} #include`;
    return ref.type;
  });
  const suffix = report.count > examples.length ? `\n…另有 ${report.count - examples.length} 筆` : '';
  return `\n\n目前仍有 ${report.count} 筆引用：\n- ${examples.join('\n- ')}${suffix}`;
}

function validationChangeText(changes={}) {
  return `新增 ${changes.added || 0} · 刪除 ${changes.deleted || 0} · 移動 ${changes.moved || 0} · 修改 ${changes.changed || 0} · 總數 ${changes.total || 0}`;
}

function showMapValidation(report) {
  return new Promise(resolve => {
    const modal = $('#map-validation-modal');
    const body = $('#map-validation-body');
    const confirmButton = $('#map-validation-confirm');
    const cancelButton = $('#map-validation-cancel');
    const applyButton = $('#map-validation-apply');
    const closeButton = $('#map-validation-close');

    const errors = report.errors || [];
    const warnings = report.warnings || [];
    const suggestions = report.suggestions || [];
    body.innerHTML = `
      <div class="validation-change-summary">${esc(validationChangeText(report.changes))}</div>
      ${errors.length ? `
        <div class="validation-group bad">
          <h3>必須先修正（${errors.length}）</h3>
          ${errors.slice(0,12).map(x => `<div class="validation-row"><b>Row ${Number(x.rowIndex)+1}</b><span>${esc(x.message)}</span></div>`).join('')}
        </div>` : ''}
      ${warnings.length ? `
        <div class="validation-group warn">
          <h3>警告（${warnings.length}）</h3>
          ${warnings.slice(0,12).map(x => `<div class="validation-row"><b>Row ${Number(x.rowIndex)+1}</b><span>${esc(x.message)}</span></div>`).join('')}
        </div>` : ''}
      ${suggestions.length ? `
        <div class="validation-group neutral">
          <h3>修復預覽</h3>
          ${suggestions.slice(0,12).map(x => `<div class="validation-row"><b>Row ${Number(x.rowIndex)+1}</b><span>(${x.from.x}, ${x.from.y}) → (${x.to.x}, ${x.to.y})</span></div>`).join('')}
        </div>` : ''}
      ${!errors.length && !warnings.length ? '<div class="health-empty good">✓ 驗證通過，可以安全儲存。</div>' : ''}
    `;

    confirmButton.hidden = errors.length > 0;
    confirmButton.textContent = warnings.length ? '接受警告並儲存' : '儲存';
    cancelButton.textContent = errors.length ? '返回修正' : '取消';
    applyButton.hidden = suggestions.length === 0;
    applyButton.textContent = suggestions.length ? `套用 ${suggestions.length} 筆建議位置` : '套用建議位置';
    modal.hidden = false;

    const finish = value => {
      modal.hidden = true;
      confirmButton.onclick = null;
      cancelButton.onclick = null;
      applyButton.onclick = null;
      closeButton.onclick = null;
      modal.onclick = null;
      resolve(value);
    };
    confirmButton.onclick = () => finish(true);
    applyButton.onclick = () => {
      let applied = 0;
      for (const suggestion of suggestions) {
        const index = Number(suggestion.rowIndex);
        const row = state.mapRows[index];
        if (!row || !suggestion.to) continue;
        row.x = Number(suggestion.to.x);
        row.y = Number(suggestion.to.y);
        applied++;
      }
      if (applied) {
        commitMapEdit(`已套用 ${applied} 筆 Blocking 修復建議`);
        renderMapTable();
        renderMapVisual();
        toast(`已套用 ${applied} 筆建議位置，請確認後再次儲存`);
      }
      finish(false);
    };
    cancelButton.onclick = () => finish(false);
    closeButton.onclick = () => finish(false);
    modal.onclick = e => { if (e.target === modal) finish(false); };
  });
}

async function validateMapBeforeSave() {
  const report = await api(`/api/maps/${state.mapId}/validate`, {
    method:'POST',
    body:{tab:state.mapTab, items:state.mapRows},
  });
  if ((report.errors || []).length || (report.warnings || []).length) {
    return (await showMapValidation(report)) ? report : null;
  }
  return report;
}

$('#health-refresh')?.addEventListener('click', () => {
  loadHealth(true).catch(e => toast(e.message, 'error'));
});

$('#health-search')?.addEventListener('input', debounce(renderHealth, 120));
$('#health-severity')?.addEventListener('change', renderHealth);
$('#health-category')?.addEventListener('change', renderHealth);
