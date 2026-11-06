CREATE INDEX IF NOT EXISTS ix_characters_account
    ON characters(account_id);

CREATE INDEX IF NOT EXISTS ix_inventory_character
    ON inventory_items(character_id);

CREATE INDEX IF NOT EXISTS ix_equipment_character
    ON equipment_items(character_id);

CREATE INDEX IF NOT EXISTS ix_pets_character
    ON pets(character_id);

CREATE INDEX IF NOT EXISTS ix_horses_character
    ON horses(character_id);

CREATE INDEX IF NOT EXISTS ix_horse_equipment_character
    ON horse_equipment(character_id);
