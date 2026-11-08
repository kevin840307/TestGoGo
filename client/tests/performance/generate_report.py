#!/usr/bin/env python3
"""Offline SGO performance report (Python stdlib only; no chart packages).

Reads test-results/performance/<session>/hardware.csv and godot_events.csv.
Never substitutes zero for missing GPU counters.
"""
from __future__ import annotations

import argparse
import csv
from collections import defaultdict
from datetime import datetime
from html import escape
import json
from pathlib import Path
import statistics


HARDWARE = (
    ("cpu_percent", "CPU（所有測試 Client 總和）", "%", "CPU 使用率，已依邏輯處理器數標準化"),
    ("ram_working_mb", "RAM Working Set 總和", "MB", "程序工作集；多開時共用頁可能重複計入"),
    ("ram_private_mb", "RAM Private Bytes 總和", "MB", "各程序私有記憶體總和"),
    ("gpu_3d_percent", "GPU 3D 引擎使用率總和", "%", "WDDM 各程序 3D 引擎加總，非整張 GPU 使用率"),
    ("gpu_vram_dedicated_mb", "GPU 專用記憶體總和", "MB", "WDDM per-process dedicated usage；非整張顯卡 VRAM"),
)
RENDER = (
    ("fps", "Godot FPS", "FPS", "2 秒附近的引擎 FPS 快照；不等於 1% Low"),
    ("draw_calls", "Draw Calls", "/幀", "Godot 每影格繪圖呼叫次數"),
    ("canvas_nodes", "Canvas Nodes", "個", "Godot 場景內 Canvas 節點數"),
    ("visible_monsters", "可見怪物", "隻", "比較模式時請固定怪物數"),
    ("process_ms", "Godot Process 耗時", "ms", "每幀 Process 耗時快照，不是 Windows CPU%"),
)
ALL_COLUMNS = ("run_id", "scenario", "stage", "target_count", "hardware_samples",
               "render_samples", "process_samples", "cpu_percent",
               "ram_working_mb", "ram_private_mb", "gpu_3d_percent",
               "gpu_vram_dedicated_mb", "fps", "draw_calls",
               "canvas_nodes", "visible_monsters", "process_ms", "mode_observed",
               "monster_simplify_observed", "warnings")


def number(value):
    if value is None or value == "":
        return None
    try:
        return float(value)
    except (ValueError, TypeError):
        return None


def median(items):
    vals = [x for x in items if x is not None]
    return round(statistics.median(vals), 3) if vals else None


def read_csv(path):
    if not path.is_file():
        return []
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def load_sessions(root: Path):
    sessions = []
    for directory in sorted(root.iterdir()) if root.exists() else []:
        if not directory.is_dir():
            continue
        metadata_path = directory / "session.json"
        if not metadata_path.is_file():
            continue
        try:
            meta = json.loads(metadata_path.read_text(encoding="utf-8-sig"))
        except (ValueError, UnicodeError, OSError):
            continue
        sessions.append((directory, meta, read_csv(directory / "hardware.csv"),
                         read_csv(directory / "godot_events.csv")))
    return sessions


def aggregate_hardware(rows):
    """First sum individual client processes per sampling tick, then median ticks."""
    grouped = defaultdict(list)
    for row in rows:
        grouped[row.get("sample_index", "")].append(row)
    aggregated = defaultdict(list)
    for sample_rows in grouped.values():
        for key, _, _, _ in HARDWARE:
            vals = [number(r.get(key)) for r in sample_rows]
            valid = [v for v in vals if v is not None]
            if valid:
                aggregated[key].append(sum(valid))
    return {key: median(aggregated.get(key, [])) for key, _, _, _ in HARDWARE}


def aggregate_events(rows):
    """Use only foreground, valid-render snapshots (exclude background/stale)."""
    rendered = [r for r in rows if r.get("kind") == "render"
                and r.get("render_stats_valid", "true").lower() != "false"
                and r.get("focus", "foreground") == "foreground"]
    processed = [r for r in rows if r.get("kind") == "process"
                 and r.get("focus", "foreground") == "foreground"]
    results = {
        "fps": median([number(r.get("fps")) for r in rendered]),
        "draw_calls": median([number(r.get("draw_calls")) for r in rendered]),
        "canvas_nodes": median([number(r.get("canvas_nodes")) for r in rendered]),
        "visible_monsters": median([number(r.get("visible_monsters")) for r in rendered]),
        "process_ms": median([number(r.get("process_ms")) for r in processed]),
    }
    modes = sorted({r.get("mode", "") for r in rendered if r.get("mode")})
    simplify = sorted({r.get("monster_simplify", "") for r in rendered
                       if r.get("monster_simplify")})
    results["mode_observed"] = ", ".join(modes)
    results["monster_simplify_observed"] = ", ".join(simplify)
    results["render_samples"] = len(rendered)
    results["process_samples"] = len(processed)
    return results


def summarize(sessions):
    summary = []
    for directory, meta, hardware_rows, events in sessions:
        stages = list(dict.fromkeys(r.get("stage", "") for r in hardware_rows))
        for stage in stages:
            samples = [r for r in hardware_rows if r.get("stage") == stage]
            godot = [r for r in events if r.get("stage") == stage]
            row = {
                "run_id": meta.get("run_id", directory.name),
                "scenario": meta.get("scenario", ""),
                "stage": stage,
                "target_count": meta.get("target_count", 0),
                "hardware_samples": len({r.get("sample_index") for r in samples}),
            }
            row.update(aggregate_hardware(samples))
            row.update(aggregate_events(godot))
            warnings = []
            if row["gpu_3d_percent"] is None:
                warnings.append("GPU 3D 計數器無數據")
            if row["gpu_vram_dedicated_mb"] is None:
                warnings.append("VRAM 計數器無數據")
            if not row["render_samples"]:
                warnings.append("無 Godot 圖形 Log（可能為附加既有程序）")
            stage_parts = stage.split("-")
            expected = stage_parts[2] if stage_parts[0] == "multi" and len(stage_parts) > 2 else stage_parts[0]
            requested_simplify = stage_parts[-1] if stage_parts[-1] in ("on", "off") else ""
            observed = row["mode_observed"].split(", ")
            if expected in ("balanced", "low", "multibox") and row["mode_observed"] and expected not in observed:
                warnings.append("標籤與 Godot 實際模式不一致")
            if requested_simplify and row["monster_simplify_observed"] and requested_simplify not in row["monster_simplify_observed"].split(", "):
                warnings.append("標籤與 Godot 怪物簡化設定不一致")
            row["warnings"] = "；".join(warnings)
            summary.append(row)
    return summary


def fmt(value, digits=1):
    if value is None or value == "":
        return "—"
    if isinstance(value, (int, float)):
        return f"{value:,.{digits}f}"
    return str(value)


def svg_bar(rows, key, heading, unit, description):
    colors = ("#38bdf8", "#34d399", "#fbbf24", "#a78bfa", "#fb7185", "#7dd3fc")
    indexed = [(i, r, number(r.get(key))) for i, r in enumerate(rows)]
    finite = [v for _, _, v in indexed if v is not None]
    if not finite:
        return (f'<section class="panel"><h3>{escape(heading)}</h3>'
                f'<p class="empty">沒有可用數據；Windows GPU 計數器未提供時不會以 0 代替。</p></section>')
    width, left, right, top, row_height = 860, 180, 110, 28, 43
    max_value = max(finite) * 1.12 if max(finite) > 0 else 1
    available = width - left - right
    h = top + len(indexed) * row_height + 34
    result = [f'<section class="panel"><h3>{escape(heading)}</h3>'
              f'<p class="chart-desc">{escape(description)}</p>',
              f'<svg viewBox="0 0 {width} {h}" role="img" aria-label="{escape(heading)}">']
    for i, (original, record, value) in enumerate(indexed):
        y = top + i * row_height
        name = f'{record["stage"]} · {record["target_count"]}開'
        if len(rows) > 6:
            name += f' · {str(record["run_id"])[-6:]}'
        name = name[:35]
        result.append(f'<text x="{left-12}" y="{y+18}" text-anchor="end" class="axis">'
                      f'{escape(name)}</text>')
        result.append(f'<rect x="{left}" y="{y}" width="{available}" height="24" rx="5" fill="#1e293b"/>')
        if value is None:
            result.append(f'<text x="{left+12}" y="{y+18}" class="missing">無資料</text>')
            continue
        bar_width = max(1.0, available * value / max_value)
        color = colors[original % len(colors)]
        result.append(f'<rect x="{left}" y="{y}" width="{bar_width:.1f}" height="24" rx="5" fill="{color}"/>')
        result.append(f'<text x="{left+bar_width+9:.1f}" y="{y+18}" class="value">'
                      f'{escape(fmt(value))} {escape(unit)}</text>')
    result.append('</svg></section>')
    return "".join(result)


def render_html(rows, session_count):
    metrics = HARDWARE + RENDER
    charts = "".join(svg_bar(rows, key, title, unit, description)
                     for key, title, unit, description in metrics)
    headings = [
        ("stage", "測試階段"), ("target_count", "視窗數"),
        ("hardware_samples", "硬體採樣點"), ("cpu_percent", "CPU %"),
        ("ram_working_mb", "RAM MB"), ("ram_private_mb", "Private MB"),
        ("gpu_3d_percent", "GPU 3D %"), ("gpu_vram_dedicated_mb", "VRAM MB"),
        ("fps", "FPS"), ("draw_calls", "Draw Calls"),
        ("visible_monsters", "怪物"), ("process_ms", "Process ms"),
        ("mode_observed", "Godot 實際模式"), ("monster_simplify_observed", "怪物簡化"),
        ("warnings", "資料品質")
    ]
    table_header = "".join(f"<th>{escape(title)}</th>" for _, title in headings)
    table_rows = []
    for r in rows:
        cells = []
        for key, _ in headings:
            value = r.get(key)
            if key == "stage":
                value = str(value) + " · " + str(r.get("run_id", ""))[-6:]
            cells.append(f"<td>{escape(fmt(value))}</td>")
        table_rows.append("<tr>" + "".join(cells) + "</tr>")
    if not rows:
        charts = '<p class="empty">尚無採樣資料。先執行 BAT 選項 7 或 8。</p>'
    generated = datetime.now().strftime("%Y-%m-%d %H:%M")
    return f"""<!doctype html>
<html lang="zh-Hant"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>SGO Client 效能比較報告</title>
<style>
:root{{color-scheme:dark;font:14px/1.55 system-ui,-apple-system,"Microsoft JhengHei",sans-serif;background:#0b1220;color:#e2e8f0}}
*{{box-sizing:border-box}}body{{margin:0}}main{{max-width:1440px;margin:auto;padding:32px 24px 64px}}
h1{{font-size:30px;letter-spacing:-.5px;margin:8px 0}}h2{{font-size:22px;margin:36px 0 14px}}h3{{font-size:17px;margin:0 0 10px}}
p{{margin:6px 0}}.lead,.chart-desc,.muted{{color:#94a3b8}}.lead{{font-size:16px;max-width:980px}}
.statline{{display:flex;gap:12px;flex-wrap:wrap;margin:26px 0}}
.stat{{background:#172337;border:1px solid #243247;border-radius:12px;padding:12px 18px;min-width:140px}}
.stat b{{font-size:21px;color:#7dd3fc;display:block}}.stat span{{color:#94a3b8}}
.grid{{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,530px),1fr));gap:16px}}
.panel{{background:#111e30;border:1px solid #26354c;border-radius:14px;padding:20px;min-width:0}}
.panel svg{{width:100%;height:auto;max-width:100%;overflow:visible}}svg text.axis{{fill:#b8c6d6;font-size:14px}}
svg text.value{{fill:#e2e8f0;font-size:15px;font-weight:600}}svg text.missing{{fill:#64748b;font-size:14px}}
.table-scroll{{overflow:auto;border:1px solid #26354c;border-radius:12px}}
table{{width:100%;border-collapse:collapse;white-space:nowrap;min-width:1080px}}
th,td{{padding:12px 10px;text-align:right;border-bottom:1px solid #26354c}}
th{{background:#1a2940;color:#cbd5e1;position:sticky;top:0}}td:first-child,th:first-child{{text-align:left}}
tr:nth-child(even){{background:#111c2d}}.empty{{color:#fda4af}}
.note{{border-top:1px solid #26354c;padding:18px 0;color:#aabbd1}}.small{{font-size:12px}}
</style></head><body><main>
<p class="muted">SGO / Performance Benchmark</p>
<h1>Client 效能比較報告</h1>
<p class="lead">Windows 真實遊戲程序 CPU、RAM、GPU 3D、VRAM，以及 Godot FPS、Draw Calls。各階段先暖機，再按取樣點統計中位數；未取得的數據顯示 —。</p>
<div class="statline"><div class="stat"><b>{session_count}</b><span>測試 Session</span></div>
<div class="stat"><b>{len(rows)}</b><span>模式測試階段</span></div>
<div class="stat"><b>{escape(generated)}</b><span>報告產生時間</span></div></div>
<h2>模式效能比較</h2><div class="grid">{charts}</div>
<h2>完整比較表</h2><div class="table-scroll"><table><thead><tr>{table_header}</tr></thead>
<tbody>{"".join(table_rows)}</tbody></table></div>
<h2>解讀方式與限制</h2>
<div class="note">CPU % 為測試 Client 的程序 CPU 時間差，除以經過時間與邏輯處理器數後加總；不是整台電腦的 CPU %。Working Set 在多開時可能重複計入共用記憶體。</div>
<div class="note">GPU 3D % 是各 Client WDDM 3D 引擎利用率加總（多 GPU／多引擎可能超過 100%），不是整張顯示卡的整體使用率；VRAM 為 Windows WDDM Dedicated Usage 程序加總，不等於整張 GPU 已占用記憶體。計數器無法讀取就留白。</div>
<div class="note">Godot FPS、Draw Calls 為有啟動 Log 的 Client 前景快照。附加既有 Client 時，如果無法取得 Godot Log，相關欄位顯示 —。FPS 取樣頻率不足以推導 1% Low／P99 Frame Time。</div>
<div class="note">跨模式比較時應固定地圖、解析度、怪物數、焦點及戰鬥狀態；不同怪物數下的 Draw Calls 不宜直接解讀為百分比改善。測試階段名稱由操作人員確認，報告不會冒稱自動切換遊戲內設定。</div>
<p class="muted small">Generated offline with Python standard library · SGO Performance Monitor</p>
</main></body></html>"""


def generate(root: Path):
    sessions = load_sessions(root)
    rows = summarize(sessions)
    root.mkdir(parents=True, exist_ok=True)
    with (root / "summary.csv").open("w", encoding="utf-8-sig", newline="") as out:
        writer = csv.DictWriter(out, fieldnames=ALL_COLUMNS, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)
    html_path = root / "report.html"
    html_path.write_text(render_html(rows, len(sessions)), encoding="utf-8")
    return html_path, rows


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    args = parser.parse_args()
    html_path, rows = generate(args.root)
    print(f"[PERF_REPORT] sessions={len(load_sessions(args.root))} stages={len(rows)}")
    print(f"[PERF_REPORT] html={html_path}")
    print(f"[PERF_REPORT] csv={args.root / 'summary.csv'}")
    for row in rows:
        print(f'  {row["stage"]} x{row["target_count"]}: '
              f'CPU={fmt(row["cpu_percent"])}% RAM={fmt(row["ram_working_mb"])}MB '
              f'GPU3D={fmt(row["gpu_3d_percent"])}% VRAM={fmt(row["gpu_vram_dedicated_mb"])}MB '
              f'FPS={fmt(row["fps"])} DrawCalls={fmt(row["draw_calls"])} '
              f'({row["warnings"] or "OK"})')


if __name__ == "__main__":
    main()
