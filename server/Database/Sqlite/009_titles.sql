CREATE TABLE IF NOT EXISTS character_titles (
    character_id INTEGER NOT NULL,
    title_id INTEGER NOT NULL,
    unlocked_utc_ms INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(character_id, title_id),
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS character_title_state (
    character_id INTEGER PRIMARY KEY,
    current_title INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE CASCADE
);
