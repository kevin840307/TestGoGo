CREATE TABLE IF NOT EXISTS rank_messages (
    rank_type INTEGER NOT NULL,
    character_id INTEGER NOT NULL,
    message TEXT NOT NULL DEFAULT '',
    updated_utc_ms INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(rank_type, character_id),
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE CASCADE
);
