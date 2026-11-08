"use strict";

const assert = require("assert");
const MapMath = require("../web/app-map-math.js");

const bounds = {minX: 1808, minY: 1440, maxX: 7888, maxY: 5744};

assert.deepStrictEqual(
  MapMath.worldToPercent(bounds.minX, bounds.minY, bounds),
  {left: 0, top: 0}
);
assert.deepStrictEqual(
  MapMath.worldToPercent(bounds.maxX, bounds.maxY, bounds),
  {left: 100, top: 100}
);

const rect = {left: 100, top: 50, width: 608, height: 430.4};
for (const point of [
  {x: bounds.minX, y: bounds.minY},
  {x: 3104, y: 2918},
  {x: 7874, y: 5734},
  {x: bounds.maxX, y: bounds.maxY},
]) {
  const pct = MapMath.worldToPercent(point.x, point.y, bounds);
  const clientX = rect.left + rect.width * pct.left / 100;
  const clientY = rect.top + rect.height * pct.top / 100;
  const roundTrip = MapMath.clientToWorld(clientX, clientY, rect, bounds);
  assert.ok(Math.abs(roundTrip.x - point.x) <= 1, `X round-trip drift: ${point.x} -> ${roundTrip.x}`);
  assert.ok(Math.abs(roundTrip.y - point.y) <= 1, `Y round-trip drift: ${point.y} -> ${roundTrip.y}`);
}

assert.deepStrictEqual(MapMath.worldToCell(0, 0), {x:0, y:0});
assert.deepStrictEqual(MapMath.worldToCell(15.999, 15.999), {x:0, y:0});
assert.deepStrictEqual(MapMath.worldToCell(16, 16), {x:1, y:1});
assert.deepStrictEqual(MapMath.worldToCell(3104, 2918), {
  x: Math.floor(3104 / 16),
  y: Math.floor(2918 / 16),
});
assert.deepStrictEqual(MapMath.cellCenter(194, 182), {x:3112, y:2920});

const scatter = MapMath.generateScatterPoints(3000, 2000, 20, 320);
assert.strictEqual(scatter.length, 20);
for (const p of scatter) {
  const distance = Math.hypot(p.x - 3000, p.y - 2000);
  assert.ok(distance <= 320 + 1e-9, `scatter point outside radius: ${distance}`);
}

console.log("PASS: map coordinate math matches world/grid contract");
