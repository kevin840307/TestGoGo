CREATE TABLE IF NOT EXISTS character_transform_state (
    character_id INTEGER PRIMARY KEY,
    transform_type INTEGER NOT NULL DEFAULT 0,
    transform_id TEXT NOT NULL DEFAULT '',
    color_index INTEGER NOT NULL DEFAULT 0,
    source_item_id INTEGER NOT NULL DEFAULT 0,
    started_utc_ms INTEGER NOT NULL DEFAULT 0,
    expires_utc_ms INTEGER NOT NULL DEFAULT 0,
    attack_bonus INTEGER NOT NULL DEFAULT 0,
    defense_bonus INTEGER NOT NULL DEFAULT 0,
    magic_attack_bonus INTEGER NOT NULL DEFAULT 0,
    magic_defense_bonus INTEGER NOT NULL DEFAULT 0,
    hit_bonus INTEGER NOT NULL DEFAULT 0,
    dodge_bonus INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_character_transform_expiry
    ON character_transform_state(expires_utc_ms);
