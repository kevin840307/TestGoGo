CREATE TABLE IF NOT EXISTS clubs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    level INTEGER NOT NULL DEFAULT 1,
    exp INTEGER NOT NULL DEFAULT 0,
    saving INTEGER NOT NULL DEFAULT 0,
    message TEXT NOT NULL DEFAULT '',
    active_research_id INTEGER NOT NULL DEFAULT 0,
    active_research_end_utc_ms INTEGER NOT NULL DEFAULT 0,
    created_utc_ms INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS club_members (
    club_id INTEGER NOT NULL,
    character_id INTEGER NOT NULL UNIQUE,
    position INTEGER NOT NULL DEFAULT 0,
    donation INTEGER NOT NULL DEFAULT 0,
    joined_utc_ms INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(club_id, character_id),
    FOREIGN KEY(club_id) REFERENCES clubs(id) ON DELETE CASCADE,
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS club_research (
    club_id INTEGER NOT NULL,
    research_id INTEGER NOT NULL,
    level INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(club_id, research_id),
    FOREIGN KEY(club_id) REFERENCES clubs(id) ON DELETE CASCADE
);
