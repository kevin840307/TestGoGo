CREATE TABLE IF NOT EXISTS account_mall_points (
    account_id INTEGER PRIMARY KEY,
    points INTEGER NOT NULL DEFAULT 0,
    lifetime_donation INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(account_id) REFERENCES accounts(id) ON DELETE CASCADE
);
