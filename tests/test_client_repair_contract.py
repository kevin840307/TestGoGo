from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
REPAIR_WINDOW = ROOT / "client" / "scripts" / "repair_window.gd"
NETWORK_MANAGER = ROOT / "client" / "scripts" / "network_manager.gd"


class ClientRepairContractTests(unittest.TestCase):
    def setUp(self):
        self.repair = REPAIR_WINDOW.read_text(encoding="utf-8-sig")
        self.network = NETWORK_MANAGER.read_text(encoding="utf-8-sig")

    def test_repair_ui_rejects_full_durability_before_network_request(self):
        self.assertIn(
            'var damage = int(item_data.get("Damage", item_data.get("damage", 0)))',
            self.repair,
        )
        self.assertIn(
            'ItemLoader.get_item_property(item_idx, "durability", 0)',
            self.repair,
        )
        self.assertIn("if damage >= durability:", self.repair)
        rejection = self.repair.index("if damage >= durability:")
        send = self.repair.index("NetworkManager.send_repair_item_request")
        self.assertLess(rejection, send)

    def test_equipment_tab_maps_to_server_equip_contract(self):
        self.assertIn(
            "if tab == SharedInventoryPanel.TabType.EQUIP:",
            self.repair,
        )
        self.assertIn(
            'uid = int(item_data.get("ItemID", item_data.get("item_id", 0)))',
            self.repair,
        )
        self.assertIn('item_type = "Equip"', self.repair)

    def test_mecha_tab_maps_to_server_horse_equip_contract(self):
        self.assertIn(
            "elif tab == SharedInventoryPanel.TabType.MECHA:",
            self.repair,
        )
        self.assertIn(
            'uid = int(item_data.get("EquipUID", item_data.get("equip_uid", 0)))',
            self.repair,
        )
        self.assertIn('item_type = "HorseEquip"', self.repair)

    def test_ui_only_sends_positive_uid_through_network_manager(self):
        self.assertIn("if uid > 0 and NetworkManager:", self.repair)
        self.assertIn(
            "NetworkManager.send_repair_item_request(item_type, uid)",
            self.repair,
        )

    def test_network_manager_packet_matches_server_schema(self):
        start = self.network.index(
            "func send_repair_item_request(item_type: String, uid: int)"
        )
        end = self.network.index("\nfunc ", start + 1)
        body = self.network[start:end]

        self.assertIn('"Type": "RepairItemRequest"', body)
        self.assertIn('"ItemType": item_type', body)
        self.assertIn('"UID": uid', body)
        self.assertIn("send_secure_packet(", body)


if __name__ == "__main__":
    unittest.main()
