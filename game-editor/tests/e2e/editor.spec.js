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
