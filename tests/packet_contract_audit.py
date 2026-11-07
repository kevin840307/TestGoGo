#!/usr/bin/env python3
"""Static client/server wire-contract audit for the recovered Godot client.

Run from any directory:
    python3 tests/packet_contract_audit.py --strict
    python3 tests/packet_contract_audit.py --output packet-contract-report.json

This checks Type dispatch and literal Action routing. It does not assert that a
handler's gameplay behavior or its payload schema is correct; those need E2E
tests. Uses only Python's standard library.
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
WIRE_NAME = r"[A-Za-z][A-Za-z0-9_]*"
CLIENT_TYPE = re.compile(r"""['"]Type['"]\s*:\s*['"](""" + WIRE_NAME + r""")['"]""")
CLIENT_DISPATCH = re.compile(r'(?m)^\t\t"(' + WIRE_NAME + r')":')
ROUTER_CASE = re.compile(r'(?m)^ {12}case "(' + WIRE_NAME + r')":')
PRELOGIN = re.compile(r'\btype\s*==\s*"(' + WIRE_NAME + r')"')
MODULE_HANDLER = re.compile(r'\bPacketType\s*=>\s*"(' + WIRE_NAME + r')"')
SERVER_RESPONSE = re.compile(r'\bType\s*=\s*"(' + WIRE_NAME + r')"')
ACTION_CALL = re.compile(
    r'\bsend_(trade|shop|pet|mall|party)_action\s*\(\s*"('
    + WIRE_NAME + r')"'
)
SWITCH_ARM = re.compile(r'(?m)^\s*"(' + WIRE_NAME + r')"\s*=>')
ANY_CASE = re.compile(r'\bcase\s+"(' + WIRE_NAME + r')":')


def text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def scan(root: Path) -> dict:
    client_root = root / "client"
    server_root = root / "server"
    network_file = client_root / "scripts" / "network_manager.gd"
    router_file = server_root / "Application" / "PacketRouter.cs"
    if not network_file.is_file() or not router_file.is_file():
        raise FileNotFoundError("Expected client/scripts/network_manager.gd and server/Application/PacketRouter.cs")

    outgoing: set[str] = set()
    actions: dict[str, set[str]] = {
        name: set() for name in ("trade", "shop", "pet", "mall", "party")
    }
    sending_files = 0
    for path in client_root.rglob("*.gd"):
        source = text(path)
        # Filter out arbitrary data dictionaries that happen to use a Type key.
        if "send_secure_packet(" in source:
            sending_files += 1
            outgoing.update(CLIENT_TYPE.findall(source))
        for kind, action in ACTION_CALL.findall(source):
            actions[kind].add(action)
            if kind == "trade":
                outgoing.add(action)  # Trade uses the Action name as packet Type.

    router = text(router_file)
    incoming = set(ROUTER_CASE.findall(router))
    incoming.update(PRELOGIN.findall(router))
    for path in server_root.rglob("*.cs"):
        incoming.update(MODULE_HANDLER.findall(text(path)))

    client_dispatch = set(CLIENT_DISPATCH.findall(text(network_file)))
    emitted: set[str] = set()
    for path in server_root.rglob("*.cs"):
        emitted.update(SERVER_RESPONSE.findall(text(path)))

    # Dynamic Action strings must map to the appropriate service, not merely
    # match some unrelated string elsewhere in the repository.
    action_server_files = {
        "shop": server_root / "Application" / "Shops" / "PlayerShopService.cs",
        "pet": router_file,
        "mall": server_root / "Application" / "Mall" / "MallService.cs",
        "party": server_root / "Application" / "Party" / "PartyService.cs",
    }
    missing_actions: dict[str, list[str]] = {}
    for kind, requested in actions.items():
        if kind == "trade":
            supported = incoming
        elif kind == "pet":
            supported = set(ANY_CASE.findall(router))
        else:
            source = text(action_server_files[kind])
            supported = set(SWITCH_ARM.findall(source))
            if kind == "mall":
                supported.update(ANY_CASE.findall(source))
            if kind == "shop":
                # NPC shop actions are handled in PacketRouter, not PlayerShop.
                supported.update(
                    re.findall(r'\baction\s*==\s*"(' + WIRE_NAME + r')"', router)
                )
        diff = sorted(requested - supported)
        if diff:
            missing_actions[kind] = diff

    # Mark known placeholder/partially implemented gameplay explicitly.
    # ROUTED only proves that a wire dispatcher exists, not E2E correctness.
    partial_requests = {
        "MallAction": "OpenMall is routed; paid items, PayPal, legacy points and donation gifts are not configured.",
        "AnnouncementListRequest": "Responds with an empty announcement list.",
        "UseItemRequest": "Some item categories report that their effects are not supported.",
    }
    partial_events = {
        "MallListResponse": "The item and gift catalog is currently empty.",
        "AnnouncementListResponse": "The server currently responds with an empty list.",
        "PayPalDonationCreated": "The response reports that payment is not configured.",
        "LegacyPointWithdrawalResponse": "No legacy points provider is configured.",
    }
    routes = []
    for packet_type in sorted(outgoing):
        status = (
            "MISSING_SERVER_HANDLER" if packet_type not in incoming
            else "PARTIAL" if packet_type in partial_requests
            else "ROUTED"
        )
        routes.append({
            "direction": "client_to_server",
            "type": packet_type,
            "status": status,
            "note": partial_requests.get(packet_type, ""),
        })
    for packet_type in sorted(emitted):
        status = (
            "MISSING_CLIENT_HANDLER" if packet_type not in client_dispatch
            else "PARTIAL" if packet_type in partial_events
            else "ROUTED"
        )
        routes.append({
            "direction": "server_to_client",
            "type": packet_type,
            "status": status,
            "note": partial_events.get(packet_type, ""),
        })

    return {
        "routes": routes,
        "client_outgoing_count": len(outgoing),
        "server_incoming_count": len(incoming),
        "server_outgoing_count": len(emitted),
        "client_incoming_count": len(client_dispatch),
        "client_sending_files": sending_files,
        "unhandled_client_requests": sorted(outgoing - incoming),
        "unhandled_server_events": sorted(emitted - client_dispatch),
        "unhandled_dynamic_actions": missing_actions,
        "client_requests": sorted(outgoing),
        "server_events": sorted(emitted),
        "actions_by_category": {kind: sorted(values) for kind, values in actions.items()},
        "limits": [
            "Static Type/Action coverage only; does not prove payload field/schema compatibility.",
            "Dynamic computed Type/Action values require explicit integration testing.",
            "Handlers can be intentionally incomplete (e.g. payment configuration).",
            "Does not replace Godot/WebSocket multiplayer E2E testing.",
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT)
    parser.add_argument("--strict", action="store_true", help="fail if any literal wire route is missing")
    parser.add_argument("--output", type=Path, help="write a machine-readable JSON report")
    args = parser.parse_args()
    report = scan(args.root)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            json.dumps(report, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )

    for left, right in (
        ("client_outgoing_count", "server_incoming_count"),
        ("server_outgoing_count", "client_incoming_count"),
    ):
        print(f"{left}={report[left]}, {right}={report[right]}")
    for name in (
        "unhandled_client_requests",
        "unhandled_server_events",
        "unhandled_dynamic_actions",
    ):
        print(f"{name}: {report[name]}")
    missing = any(
        report[name] for name in (
            "unhandled_client_requests",
            "unhandled_server_events",
            "unhandled_dynamic_actions",
        )
    )
    if args.strict and missing:
        print("FAIL: one or more client/server wire routes are missing")
        return 1
    print("PASS: all literal wire Type/Action routes are paired" if not missing
          else "WARNING: unresolved wire routes found (non-strict mode)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
