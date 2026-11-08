#!/usr/bin/env python3
"""Audit critical Server -> Godot response shapes, not only Type names.

This is a source-level regression guard.  It checks every literal C#
anonymous packet declaration for the selected Types, and verifies that the
Godot network dispatcher still recognizes them. It does not certify JSON
runtime values, dynamic dictionary payloads, or real multiplayer behavior.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# These are wire fields the recovered client consumes, or fields needed by
# other receivers to reconstruct the same state. Optional fields are excluded.
REQUIRED = {
    "PacketError": ("RequestType", "Code", "Message"),
    "LoginResponse": ("Success", "Message", "Characters"),
    "RegisterAccountResponse": ("Success", "Message"),
    "AnnouncementListResponse": ("Success", "Announcements"),
    "WorldReadyResponse": ("Success", "MapID"),
    "UpdateShortcutResponse": ("Success", "SlotIndex", "ShortcutType", "TargetID"),
    "InventorySync": ("Items", "MaxSize"),
    "EquipSync": ("Equips", "MaxSize"),
    "PetSync": ("Pets", "MaxSize"),
    "HorseSync": ("Horses", "HorseEquips", "MaxHorseEquipSize"),
    "SkillSync": ("Skills",),
    "SkillCast": (
        "CasterID", "CasterType", "SkillID", "SkillLevel",
        "TargetID", "TargetX", "TargetY", "AoeTargets"
    ),
    "EntityDamage": (
        "TargetID", "TargetType", "Damage", "CurrentHP", "MaxHP",
        "IsCrit", "SkillID", "SkillLevel", "AttackerID"
    ),
    "EntityHeal": (
        "TargetID", "TargetType", "Amount", "CurrentHP", "MaxHP",
        "SkillID", "SkillLevel", "HealerID"
    ),
    "EntityDeath": ("EntityID", "EntityType", "KillerID"),
    "DespawnEntity": ("EntityID",),
    # High-volume world and UI synchronization, shared by many emitters.
    "SpawnEntity": ("EntityID", "EntityType", "Name"),
    "MoveEntity": ("EntityID", "X", "Y", "Dir", "SpeedTime"),
    "SyncState": ("EntityID", "State", "Dir"),
    "ChangeMap": ("MapID", "X", "Y"),
    "PositionCorrection": ("X", "Y"),
    "SyncMoney": ("Money",),
    "SyncBuffs": ("Buffs",),
    "SyncStatus": ("EntityID", "HP", "MP", "SP", "MaxHP", "MaxMP", "MaxSP"),
    "ItemPickupFeedback": ("Message",),
    "SystemMessageFlash": ("Message",),
    "QuestSync": ("Quests",),
    "QuestUpdate": ("QuestID", "Status", "CurrentStep", "StepProgress"),
    "MallListResponse": ("Points", "Items", "Gifts"),
    "MallPointsSync": ("Points",),
    "PayPalDonationCreated": ("Success", "Message", "ApprovalUrl"),
    "LegacyPointWithdrawalResponse": (
        "Success", "Operation", "Message", "LegacyPoints"
    ),
    "CraftItemResponse": ("Success", "Message", "ResultItems"),
    "DismantleItemResponse": ("Success", "Message", "ResultItems"),
    "PartySync": ("PartyID", "LeaderDBID", "Members"),
    "ClubSync": ("ClubID", "ClubName", "Members", "Researches"),
    "TradeSync": (
        "MyMoney", "OtherMoney", "MyItems", "OtherItems",
        "MyLocked", "OtherLocked"
    ),
    "RankDataResponse": (
        "RankType", "RankList", "Top1Message", "Top1Equips"
    ),
    "UpdateRankMessageResponse": ("Success", "Message"),
}
PACKET = re.compile(r'\bType\s*=\s*"([A-Za-z][A-Za-z0-9_]*)"')
MEMBER = re.compile(r'(?:^|[,\n])\s*([A-Za-z][A-Za-z0-9_]*)\s*=')
GODOT_CASE = re.compile(r'^\s*"([A-Za-z][A-Za-z0-9_]*)"\s*:', re.M)


def scan(root: Path) -> dict:
    source_root = root / "server"
    client_file = root / "client/scripts/network_manager.gd"
    if not source_root.is_dir():
        raise FileNotFoundError(source_root)
    if not client_file.is_file():
        raise FileNotFoundError(client_file)

    dispatch = set(GODOT_CASE.findall(
        client_file.read_text(encoding="utf-8-sig")
    ))
    declarations: dict[str, list[dict]] = {name: [] for name in REQUIRED}
    for path in sorted(source_root.rglob("*.cs")):
        source = path.read_text(encoding="utf-8-sig")
        for match in PACKET.finditer(source):
            name = match.group(1)
            if name not in REQUIRED:
                continue
            # The protocol objects use 'Type' first and the remaining
            # properties appear before the closing initializer brace.
            # This deliberately does not attempt to parse all C# syntax.
            end = source.find("}", match.end())
            if end < 0:
                continue
            body = source[match.end():end]
            found = set(MEMBER.findall(body))
            declarations[name].append({
                "path": path.relative_to(root).as_posix(),
                "line": source.count("\n", 0, match.start()) + 1,
                "missing": sorted(set(REQUIRED[name]) - found)
            })

    checks = []
    for name, expected in REQUIRED.items():
        actual = declarations[name]
        bad = [x for x in actual if x["missing"]]
        status = (
            "MISSING_CLIENT_HANDLER" if name not in dispatch else
            "MISSING_SERVER_PACKET" if not actual else
            "INCOMPLETE_SERVER_FIELDS" if bad else "MATCHED"
        )
        checks.append({
            "type": name,
            "status": status,
            "required_fields": list(expected),
            "declarations_checked": len(actual),
            "violations": bad,
            "client_handler_present": name in dispatch,
        })
    return {
        "response_type_count": len(REQUIRED),
        "response_declarations_checked": sum(
            len(x) for x in declarations.values()),
        "matched_count": sum(x["status"] == "MATCHED" for x in checks),
        "checks": checks,
        "issues": [x for x in checks if x["status"] != "MATCHED"],
        "limits": [
            "Only selected literal anonymous C# response packet shapes.",
            "Cannot infer all dynamic object/dictionary fields.",
            "Checks presence, not value types or correct runtime ordering.",
            "Must complement real WebSocket and Godot multiplayer E2E tests."
        ]
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--strict", action="store_true")
    args = parser.parse_args()
    result = scan(args.root)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            json.dumps(result, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8"
        )
    print("Server response contracts: {}/{} types, {} declarations".format(
        result["matched_count"], result["response_type_count"],
        result["response_declarations_checked"]))
    for issue in result["issues"]:
        print("{}: {} {}".format(
            issue["status"], issue["type"], issue["violations"][:8]))
    return int(args.strict and bool(result["issues"]))


if __name__ == "__main__":
    raise SystemExit(main())
