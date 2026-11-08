CREATE TABLE IF NOT EXISTS character_cosmetic_overrides (
    character_id INTEGER NOT NULL,
    part INTEGER NOT NULL,
    item_index INTEGER NOT NULL,
    color INTEGER NOT NULL DEFAULT 0,
    source_item_id INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(character_id, part),
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_character_cosmetic_source
    ON character_cosmetic_overrides(source_item_id);
