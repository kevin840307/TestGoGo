CREATE TABLE IF NOT EXISTS external_mall_credits (
    source TEXT NOT NULL,
    external_transaction_id TEXT NOT NULL,
    account_id INTEGER NOT NULL,
    points INTEGER NOT NULL,
    lifetime_donation INTEGER NOT NULL DEFAULT 0,
    created_utc_ms INTEGER NOT NULL,
    PRIMARY KEY(source, external_transaction_id),
    FOREIGN KEY(account_id) REFERENCES accounts(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_external_mall_credits_account
    ON external_mall_credits(account_id, created_utc_ms);
