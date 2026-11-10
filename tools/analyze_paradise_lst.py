#!/usr/bin/env python3
"""Inspect the checked-in Paradise.exe.lst disassembly without inventing engine details.

The file is ~40 MB, so GitHub's file API may omit its text. CI scans it on
the runner and prints only bounded evidence for renderer / entity / network
architecture. This is analysis; it does not decompile executable behavior.
"""
from collections import Counter
from pathlib import Path
import re

SOURCE = Path("Old/ClientData/Paradise.exe.lst")
TERMS = {
    "renderer": re.compile(r"(?:BeginScene|EndScene|DrawPrimitive|DrawIndexedPrimitive|DrawSprite|SpriteBatch|Present|DirectDraw|Direct3D|Gdi|BitBlt|D3DXSprite)", re.I),
    "entity": re.compile(r"(?:Monster|GroundItem|DropItem|ItemDrop|ItemObject|ObjectManager|SpriteManager|EntityManager|NPC|Character)", re.I),
    "network": re.compile(r"(?:recv|WSARecv|send|WSASend|Socket|ProcessPacket|RecvPacket|PacketHandler|UpdateEntity)", re.I),
    "resource": re.compile(r"(?:LoadTexture|CreateTexture|ReleaseTexture|TextureCache|ResourceManager|SpriteCache|LoadSprite)", re.I),
    "tick": re.compile(r"(?:GetTickCount|QueryPerformanceCounter|timeGetTime|Sleep|SetTimer|UpdateFrame|UpdateWorld|MoveMonster)", re.I),
}
counts = Counter()
samples = {k: [] for k in TERMS}
prologue = []
lines = 0
size = SOURCE.stat().st_size
with SOURCE.open("r", encoding="utf-8", errors="replace") as fh:
    for line in fh:
        lines += 1
        if len(prologue) < 10:
            prologue.append(line.rstrip()[:180])
        for category, pat in TERMS.items():
            if pat.search(line):
                counts[category] += 1
                if len(samples[category]) < 8:
                    samples[category].append((lines, line.strip()[:220]))
print(f"[LST_AUDIT] source={SOURCE} bytes={size} lines={lines}")
for line in prologue:
    print("[LST_HEADER] " + line)
for category in TERMS:
    print(f"[LST_AUDIT] {category} matching_lines={counts[category]}")
    for no, line in samples[category]:
        print(f"[LST_EVIDENCE] {category} L{no}: {line}")
print("[LST_AUDIT] Evidence is symbol/string matches only; do not infer AOI, culling, tick frequency, or caching without control-flow verification.")
