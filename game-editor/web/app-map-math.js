(function(root) {
  "use strict";

  function worldToPercent(x, y, bounds) {
    const width = Number(bounds.maxX) - Number(bounds.minX);
    const height = Number(bounds.maxY) - Number(bounds.minY);
    if (!(width > 0) || !(height > 0)) throw new Error("Invalid map bounds");
    return {
      left: ((Number(x) - Number(bounds.minX)) / width) * 100,
      top: ((Number(y) - Number(bounds.minY)) / height) * 100,
    };
  }

  function clientToWorld(clientX, clientY, rect, bounds) {
    const width = Math.max(1, Number(rect.width));
    const height = Math.max(1, Number(rect.height));
    const px = Math.max(0, Math.min(1, (Number(clientX) - Number(rect.left)) / width));
    const py = Math.max(0, Math.min(1, (Number(clientY) - Number(rect.top)) / height));
    return {
      x: Math.round(Number(bounds.minX) + px * (Number(bounds.maxX) - Number(bounds.minX))),
      y: Math.round(Number(bounds.minY) + py * (Number(bounds.maxY) - Number(bounds.minY))),
    };
  }

  function worldToCell(x, y, cellSize=16) {
    return {
      x: Math.floor(Number(x) / Number(cellSize)),
      y: Math.floor(Number(y) / Number(cellSize)),
    };
  }

  function cellCenter(cellX, cellY, cellSize=16) {
    const size = Number(cellSize);
    return {
      x: Number(cellX) * size + size / 2,
      y: Number(cellY) * size + size / 2,
    };
  }

  function generateScatterPoints(centerX, centerY, count, radius) {
    const points = [];
    const total = Math.max(0, Number(count) | 0);
    const r = Math.max(0, Number(radius));
    const golden = Math.PI * (3 - Math.sqrt(5));
    for (let i = 0; i < total; i++) {
      const ratio = Math.sqrt((i + 0.5) / Math.max(1, total));
      const angle = i * golden;
      points.push({
        x: Number(centerX) + Math.cos(angle) * r * ratio,
        y: Number(centerY) + Math.sin(angle) * r * ratio,
      });
    }
    return points;
  }

  const api = { worldToPercent, clientToWorld, worldToCell, cellCenter, generateScatterPoints };
  root.SGOMapMath = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(typeof globalThis !== "undefined" ? globalThis : this);
