CREATE TABLE IF NOT EXISTS character_timed_effects (
    character_id INTEGER NOT NULL,
    effect_key TEXT NOT NULL,
    effect_value INTEGER NOT NULL DEFAULT 0,
    expires_utc_ms INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(character_id, effect_key),
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_character_timed_effects_expiry
    ON character_timed_effects(expires_utc_ms);
