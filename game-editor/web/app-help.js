// First-run onboarding / shortcut help
const HELP_STORAGE_KEY = 'sgo-editor-help-v1';

function openHelp({firstRun=false}={}) {
  const modal = $('#help-modal');
  if (!modal) return;
  $('#help-dont-show').checked = false;
  modal.dataset.firstRun = firstRun ? '1' : '0';
  modal.hidden = false;
}

function closeHelp({remember=false}={}) {
  const modal = $('#help-modal');
  if (!modal) return;
  const firstRun = modal.dataset.firstRun === '1';
  if (firstRun || remember || $('#help-dont-show')?.checked) {
    try { localStorage.setItem(HELP_STORAGE_KEY, 'dismissed'); } catch {}
  }
  modal.hidden = true;
  modal.dataset.firstRun = '0';
}

function maybeShowFirstRunHelp() {
  let dismissed = false;
  try { dismissed = localStorage.getItem(HELP_STORAGE_KEY) === 'dismissed'; } catch {}
  if (!dismissed) openHelp({firstRun:true});
}

$('#help-open')?.addEventListener('click', () => openHelp());
$('#help-close')?.addEventListener('click', () => closeHelp());
$('#help-ok')?.addEventListener('click', () => closeHelp({remember:$('#help-dont-show')?.checked}));
$('#help-modal')?.addEventListener('click', event => {
  if (event.target === $('#help-modal')) closeHelp();
});
window.addEventListener('keydown', event => {
  if (event.key === 'Escape' && !$('#help-modal')?.hidden) closeHelp();
});

window.addEventListener('DOMContentLoaded', () => {
  setTimeout(maybeShowFirstRunHelp, 120);
});
