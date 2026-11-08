CREATE TABLE IF NOT EXISTS character_quests (
    character_id INTEGER NOT NULL,
    quest_id INTEGER NOT NULL,
    status INTEGER NOT NULL DEFAULT 1,
    current_step INTEGER NOT NULL DEFAULT 0,
    step_progress INTEGER NOT NULL DEFAULT 0,
    updated_utc_ms INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(character_id, quest_id),
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE CASCADE
);
