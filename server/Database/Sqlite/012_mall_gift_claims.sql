CREATE TABLE IF NOT EXISTS account_mall_gift_claims (
    account_id INTEGER NOT NULL,
    reward_id INTEGER NOT NULL,
    character_id INTEGER NOT NULL,
    claimed_utc_ms INTEGER NOT NULL,
    PRIMARY KEY(account_id, reward_id),
    FOREIGN KEY(account_id) REFERENCES accounts(id) ON DELETE CASCADE,
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_account_mall_gift_claims_character
    ON account_mall_gift_claims(character_id);
