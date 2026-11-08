const { test, expect } = require('@playwright/test');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const editorRoot = path.resolve(__dirname, '../..');
const editorPy = path.join(editorRoot, 'editor.py');
let fixtureRoot;
let server;
let baseUrl;
let serverLog = '';

function mkdir(p) { fs.mkdirSync(p, { recursive: true }); }

function makeFixture() {
  fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'sgo-editor-e2e-'));
  mkdir(path.join(fixtureRoot, 'client', 'Skills'));
  mkdir(path.join(fixtureRoot, 'client', 'Assets', 'GameInfo'));
  mkdir(path.join(fixtureRoot, 'client', 'maps'));
  mkdir(path.join(fixtureRoot, 'server'));
  mkdir(path.join(fixtureRoot, 'Old', '服務端', 'Dat', 'Enemy'));
  mkdir(path.join(fixtureRoot, 'Old', '服務端', 'Dat', 'Map', 'Map13'));
  mkdir(path.join(fixtureRoot, 'Old', '服務端', 'Dat', 'Quest'));
  mkdir(path.join(fixtureRoot, 'game-editor', '.generated', 'map-previews'));

  const cols = Array(180).fill('');
  cols[0] = '1';
  cols[1] = '1';
  cols[2] = 'E2E Slime';
  cols[3] = '1';
  cols[6] = 'EN001';
  cols[9] = '100';
  fs.writeFileSync(
    path.join(fixtureRoot, 'Old', '服務端', 'Dat', 'Enemy', 'Enemy_Data.csv'),
    cols.join(',') + '\r\n',
    'ascii'
  );

  fs.writeFileSync(
    path.join(fixtureRoot, 'Old', '服務端', 'Dat', 'Quest', 'NpcOk.txt'),
    'function start()\nend\n',
    'utf8'
  );

  fs.writeFileSync(
    path.join(fixtureRoot, 'client', 'maps', 'map_bounds.json'),
    JSON.stringify({ '13': { min_x: 0, min_y: 0, max_x: 1024, max_y: 1024 } }),
    'utf8'
  );
  fs.writeFileSync(
    path.join(fixtureRoot, 'client', 'Assets', 'GameInfo', 'MapSpawnConfig.json'),
    JSON.stringify({ '13': { MapName: 'E2E Test Map', Spawners: [] } }),
    'utf8'
  );

  const mapDir = path.join(fixtureRoot, 'Old', '服務端', 'Dat', 'Map', 'Map13');
  const blocking = Buffer.alloc(8 + 64 * 64);
  blocking.writeInt32LE(64, 0);
  blocking.writeInt32LE(64, 4);
  fs.writeFileSync(path.join(mapDir, 'Blocking.dat'), blocking);

  const emptyDat = Buffer.alloc(4);
  emptyDat.writeInt32LE(0, 0);
  fs.writeFileSync(path.join(mapDir, 'Enemy.dat'), emptyDat);
  fs.writeFileSync(path.join(mapDir, 'NPC.dat'), emptyDat);

  // Valid 1x1 PNG: enough to exercise preview metadata/HTTP/UI layer in E2E.
  const png = Buffer.from(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+g45LAAAAAElFTkSuQmCC',
    'base64'
  );
  fs.writeFileSync(
    path.join(fixtureRoot, 'game-editor', '.generated', 'map-previews', 'Map13.png'),
    png
  );
}

async function waitForServer(url) {
  const deadline = Date.now() + 15000;
  while (Date.now() < deadline) {
    if (server.exitCode !== null) throw new Error('editor server exited early:\n' + serverLog);
    try {
      const response = await fetch(url + '/api/status');
      if (response.ok) return;
    } catch {}
    await new Promise(resolve => setTimeout(resolve, 150));
  }
  throw new Error('timed out waiting for editor server:\n' + serverLog);
}

test.beforeAll(async () => {
  makeFixture();
  const port = 18000 + (process.pid % 1000);
  baseUrl = `http://127.0.0.1:${port}`;

  const isWindows = process.platform === 'win32';
  const command = process.env.PYTHON || (isWindows ? 'py' : 'python3');
  const args = [];
  if (isWindows && command.toLowerCase().endsWith('py')) args.push('-3');
  args.push(editorPy, '--project-root', fixtureRoot, '--port', String(port));

  server = spawn(command, args, {
    cwd: editorRoot,
    env: { ...process.env, PYTHONUNBUFFERED: '1' },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  server.stdout.on('data', chunk => { serverLog += chunk.toString(); });
  server.stderr.on('data', chunk => { serverLog += chunk.toString(); });
  await waitForServer(baseUrl);
});

test.afterAll(async () => {
  if (server && server.exitCode === null) {
    server.kill();
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  if (fixtureRoot) fs.rmSync(fixtureRoot, { recursive: true, force: true });
});

test('map add drag undo redo save and reload persists real coordinates', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('sgo-editor-help-v1', 'dismissed'));
  await page.goto(baseUrl);
  await expect(page.getByRole('heading', { name: '專案總覽' })).toBeVisible();

  await page.locator('[data-view="maps"]').click();
  await expect(page.locator('#map-select')).toHaveValue('13');
  await expect(page.locator('#map-preview-toggle')).toBeVisible();
  await expect(page.locator('.map-preview-layer')).toHaveCount(1);

  const stage = page.locator('#map-stage');
  await expect(stage).toBeVisible();
  const stageBox = await stage.boundingBox();
  expect(stageBox).not.toBeNull();

  await page.mouse.click(
    stageBox.x + stageBox.width * 0.52,
    stageBox.y + stageBox.height * 0.52
  );
  await expect(page.locator('.map-marker.monster')).toHaveCount(1);

  const xInput = page.locator('#map-inspector-x');
  const yInput = page.locator('#map-inspector-y');
  const beforeX = Number(await xInput.inputValue());
  const beforeY = Number(await yInput.inputValue());

  const marker = page.locator('.map-marker.monster').first();
  const markerBox = await marker.boundingBox();
  expect(markerBox).not.toBeNull();
  await page.mouse.move(markerBox.x + markerBox.width / 2, markerBox.y + markerBox.height / 2);
  await page.mouse.down();
  await page.mouse.move(
    markerBox.x + markerBox.width / 2 + 72,
    markerBox.y + markerBox.height / 2 + 48,
    { steps: 8 }
  );
  await page.mouse.up();

  await expect.poll(async () => Number(await xInput.inputValue())).not.toBe(beforeX);
  const movedX = Number(await xInput.inputValue());
  const movedY = Number(await yInput.inputValue());
  expect(movedX).toBeGreaterThan(beforeX);
  expect(movedY).toBeGreaterThan(beforeY);

  await page.locator('#map-undo').click();
  await page.locator('.map-marker.monster').first().click();
  await expect(page.locator('#map-inspector-x')).toHaveValue(String(beforeX));
  await expect(page.locator('#map-inspector-y')).toHaveValue(String(beforeY));

  await page.locator('#map-redo').click();
  await page.locator('.map-marker.monster').first().click();
  await expect(page.locator('#map-inspector-x')).toHaveValue(String(movedX));
  await expect(page.locator('#map-inspector-y')).toHaveValue(String(movedY));

  await page.locator('#map-save').click();
  await expect(page.locator('.toast').filter({ hasText: '地圖資料已儲存' })).toBeVisible();

  await page.reload();
  await page.locator('[data-view="maps"]').click();
  await expect(page.locator('.map-marker.monster')).toHaveCount(1);
  await page.locator('.map-marker.monster').first().click();
  await expect(page.locator('#map-inspector-x')).toHaveValue(String(movedX));
  await expect(page.locator('#map-inspector-y')).toHaveValue(String(movedY));
});

test('safe map view preferences persist across reload without editor payload', async ({ page }) => {
  // Each Playwright test gets a fresh browser context, so preferences start
  // empty already. Only suppress first-run help; do not clear prefs in an
  // init script because init scripts run again on page.reload().
  await page.addInitScript(() => {
    localStorage.setItem('sgo-editor-help-v1', 'dismissed');
  });
  await page.goto(baseUrl);
  await page.locator('[data-view="maps"]').click();

  await page.locator('#map-show-labels').uncheck();
  await page.locator('#map-snap-walkable').uncheck();
  await page.locator('[data-map-tab="npcs"]').click();
  await expect(page.locator('#map-show-labels')).not.toBeChecked();
  await expect(page.locator('#map-snap-walkable')).not.toBeChecked();

  const stored = await page.evaluate(() => JSON.parse(localStorage.getItem('sgo-editor-prefs-v1')));
  expect(stored.mapTab).toBe('npcs');
  expect(stored.mapShowLabels).toBe(false);
  expect(stored.mapSnapWalkable).toBe(false);
  for (const unsafe of ['mapRows', 'mapUndo', 'mapRedo', 'mapClipboard', 'mapDirty']) {
    expect(Object.prototype.hasOwnProperty.call(stored, unsafe)).toBe(false);
  }

  await page.reload();
  await page.locator('[data-view="maps"]').click();
  await expect(page.locator('[data-map-tab="npcs"]')).toHaveClass(/active/);
  await expect(page.locator('#map-show-labels')).not.toBeChecked();
  await expect(page.locator('#map-snap-walkable')).not.toBeChecked();
});

test('stale second browser tab cannot overwrite newer map data', async ({ context }) => {
  const pageA = await context.newPage();
  const pageB = await context.newPage();
  await pageA.goto(baseUrl);
  await pageB.goto(baseUrl);

  const load = page => page.evaluate(async () => {
    const response = await fetch('/api/maps/13/enemies?envelope=1');
    return response.json();
  });
  const [a, b] = await Promise.all([load(pageA), load(pageB)]);
  expect(a.revision).toBeTruthy();
  expect(b.revision).toBe(a.revision);

  const nextX = 64 + a.items.length * 16;
  const next = [...a.items, {index:-1, enemyId:1, x:nextX, y:64, sourceFlags:0}];

  const saveA = await pageA.evaluate(async ({items, revision}) => {
    const response = await fetch('/api/maps/13/enemies', {
      method:'PUT',
      headers:{'Content-Type':'application/json'},
      body:JSON.stringify({items, expectedRevision:revision}),
    });
    return {status:response.status, body:await response.json()};
  }, {items:next, revision:a.revision});
  expect(saveA.status).toBe(200);
  expect(saveA.body.revision).not.toBe(a.revision);

  const saveB = await pageB.evaluate(async ({items, revision}) => {
    const response = await fetch('/api/maps/13/enemies', {
      method:'PUT',
      headers:{'Content-Type':'application/json'},
      body:JSON.stringify({items, expectedRevision:revision}),
    });
    return {status:response.status, body:await response.json()};
  }, {items:b.items, revision:b.revision});
  expect(saveB.status).toBe(400);
  expect(saveB.body.error).toContain('其他 Editor 分頁或程序修改');

  const after = await load(pageA);
  expect(after.revision).toBe(saveA.body.revision);
  expect(after.items.length).toBe(next.length);
  expect(after.items.some(item => item.x === nextX && item.y === 64)).toBe(true);

  await pageA.close();
  await pageB.close();
});

test('stale force-delete confirmation is rejected when references change', async ({ page }) => {
  await page.goto(baseUrl);

  const before = await page.evaluate(async () => {
    const response = await fetch('/api/references/enemy/1');
    return response.json();
  });
  expect(before.count).toBeGreaterThan(0);
  expect(before.token).toBeTruthy();

  const payload = await page.evaluate(async () => {
    const response = await fetch('/api/maps/13/enemies?envelope=1');
    return response.json();
  });
  const nextX = 160 + payload.items.length * 16;
  const nextItems = [...payload.items, {index:-1, enemyId:1, x:nextX, y:96, sourceFlags:0}];
  const save = await page.evaluate(async ({items, revision}) => {
    const response = await fetch('/api/maps/13/enemies', {
      method:'PUT',
      headers:{'Content-Type':'application/json'},
      body:JSON.stringify({items, expectedRevision:revision}),
    });
    return {status:response.status, body:await response.json()};
  }, {items:nextItems, revision:payload.revision});
  expect(save.status).toBe(200);

  const after = await page.evaluate(async () => {
    const response = await fetch('/api/references/enemy/1');
    return response.json();
  });
  expect(after.token).not.toBe(before.token);

  const staleDelete = await page.evaluate(async token => {
    const response = await fetch(
      '/api/enemies/1?force=1&referenceToken=' + encodeURIComponent(token),
      {method:'DELETE'}
    );
    return {status:response.status, body:await response.json()};
  }, before.token);
  expect(staleDelete.status).toBe(400);
  expect(staleDelete.body.error).toContain('引用在確認刪除後已發生變化');

  const stillExists = await page.evaluate(async () => {
    const response = await fetch('/api/enemies/1');
    return {status:response.status, body:await response.json()};
  });
  expect(stillExists.status).toBe(200);
  expect(stillExists.body.id).toBe(1);
});

test('first-run help can be dismissed and remembered', async ({ page }) => {
  await page.goto(baseUrl);
  await expect(page.locator('#help-modal')).toBeVisible();
  await page.locator('#help-dont-show').check();
  await page.locator('#help-ok').click();
  await expect(page.locator('#help-modal')).toBeHidden();

  await page.reload();
  await expect(page.locator('#help-modal')).toBeHidden();
  await page.locator('#help-open').click();
  await expect(page.locator('#help-modal')).toBeVisible();
});
