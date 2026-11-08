"""Security/contract guards for external mall integrations."""
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent


class MallExternalSecurityTests(unittest.TestCase):
    def test_paypal_webhook_verifies_before_settlement(self):
        service = (
            ROOT / "server" / "Application" / "Mall" /
            "PayPalWebhookService.cs"
        ).read_text(encoding="utf-8-sig")
        verify = service.index("VerifyWebhookAsync(")
        verified_guard = service.index("if (!verified.Verified)")
        settlement = service.index("SettlePayPalDonation(")
        self.assertLess(verify, verified_guard)
        self.assertLess(verified_guard, settlement)

        endpoint = (
            ROOT / "server" / "Transport" / "Http" /
            "MallIntegrationEndpoint.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertIn('"/integrations/paypal/webhook"', endpoint)
        self.assertIn("service.HandleAsync(", endpoint)
        self.assertNotIn("SettlePayPalDonation(", endpoint)
        self.assertIn("Status413PayloadTooLarge", endpoint)

    def test_external_credit_ledger_is_idempotent(self):
        migration = (
            ROOT / "server" / "Database" / "Sqlite" /
            "017_external_mall_credits.sql"
        ).read_text(encoding="utf-8-sig")
        self.assertIn(
            "PRIMARY KEY(source, external_transaction_id)",
            migration,
        )

        store = (
            ROOT / "server" / "Infrastructure" / "Persistence" /
            "Sqlite" / "SqliteGameStore.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertIn("INSERT OR IGNORE INTO external_mall_credits", store)
        self.assertIn("AlreadyApplied", (
            ROOT / "server" / "Application" / "Abstractions" /
            "GameStorePorts.cs"
        ).read_text(encoding="utf-8-sig"))

    def test_router_forwards_credentials_without_logging_them(self):
        router = (
            ROOT / "server" / "Application" / "PacketRouter.cs"
        ).read_text(encoding="utf-8-sig")
        mall_call = router[
            router.index("return _mall.Handle("):
            router.index('case "TrainTravelReady"')
        ]
        self.assertIn('req.Str("LoginName")', mall_call)
        self.assertIn('req.Str("Password")', mall_call)
        self.assertIn('req.Str("RequestID")', mall_call)

        endpoint = (
            ROOT / "server" / "Transport" / "WebSockets" /
            "WebSocketEndpoint.cs"
        ).read_text(encoding="utf-8-sig")
        self.assertNotIn("Raw={Payload}", endpoint)
        self.assertNotIn("Password={Password}", endpoint)

    def test_client_mutations_attach_request_ids(self):
        client = (
            ROOT / "client" / "scripts" / "mall_window.gd"
        ).read_text(encoding="utf-8-sig")
        self.assertIn(
            '"RequestID": _next_external_request_id("paypal")',
            client,
        )
        self.assertIn(
            '"RequestID": _next_external_request_id("legacy")',
            client,
        )


if __name__ == "__main__":
    unittest.main()
