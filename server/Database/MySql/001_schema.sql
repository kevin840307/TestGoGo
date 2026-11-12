CREATE TABLE IF NOT EXISTS accounts (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    username VARCHAR(100) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    email VARCHAR(255) NOT NULL DEFAULT ''
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS characters (
    id BIGINT PRIMARY KEY AUTO_INCREMENT,
    account_id BIGINT NOT NULL,
    name VARCHAR(100) NOT NULL UNIQUE,
    password VARCHAR(255) NOT NULL,
    race INT NOT NULL,
    hair INT NOT NULL,
    hair_color INT NOT NULL,
    str INT NOT NULL,
    dex INT NOT NULL,
    con INT NOT NULL,
    wis INT NOT NULL,
    level INT NOT NULL DEFAULT 1,
    map_id INT NOT NULL DEFAULT 15,
    x DOUBLE NOT NULL DEFAULT 10000,
    y DOUBLE NOT NULL DEFAULT 10000,
    hp INT NOT NULL DEFAULT 100,
    max_hp INT NOT NULL DEFAULT 100,
    mp INT NOT NULL DEFAULT 50,
    max_mp INT NOT NULL DEFAULT 50,
    sp INT NOT NULL DEFAULT 100,
    max_sp INT NOT NULL DEFAULT 100,
    money BIGINT NOT NULL DEFAULT 0,
    exp_use BIGINT NOT NULL DEFAULT 0,
    exp_acc BIGINT NOT NULL DEFAULT 0,
    exp_death BIGINT NOT NULL DEFAULT 0,
    max_exp BIGINT NOT NULL DEFAULT 1000,
    career INT NOT NULL DEFAULT 0,
    job INT NOT NULL DEFAULT 0,
    attack INT NOT NULL DEFAULT 10,
    defense INT NOT NULL DEFAULT 5,
    magic_attack INT NOT NULL DEFAULT 5,
    magic_defense INT NOT NULL DEFAULT 5,
    hit INT NOT NULL DEFAULT 100,
    dodge INT NOT NULL DEFAULT 0,
    lucky INT NOT NULL DEFAULT 0,
    move_speed INT NOT NULL DEFAULT 100,
    attack_speed INT NOT NULL DEFAULT 100,
    attack_range INT NOT NULL DEFAULT 80,
    crit_rate INT NOT NULL DEFAULT 0,
    crit_damage INT NOT NULL DEFAULT 125,
    state VARCHAR(32) NOT NULL DEFAULT 'idle',
    pk_status INT NOT NULL DEFAULT 1,
    red_man INT NOT NULL DEFAULT 0,
    red_man_kill_left INT NOT NULL DEFAULT 0,
    pk_pink_until_utc_ms BIGINT NOT NULL DEFAULT 0,
    reborn_map_id INT NOT NULL DEFAULT 15,
    reborn_x DOUBLE NOT NULL DEFAULT 9408,
    reborn_y DOUBLE NOT NULL DEFAULT 10336,
    summoned_horse_uid BIGINT NOT NULL DEFAULT -1,
    CONSTRAINT fk_characters_account
        FOREIGN KEY(account_id) REFERENCES accounts(id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS inventory_items (
    uid BIGINT PRIMARY KEY AUTO_INCREMENT,
    character_id BIGINT NOT NULL,
    item_index INT NOT NULL,
    count INT NOT NULL DEFAULT 1,
    in_bank TINYINT NOT NULL DEFAULT 0,
    in_shop TINYINT NOT NULL DEFAULT 0,
    CONSTRAINT fk_inventory_character
        FOREIGN KEY(character_id) REFERENCES characters(id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS equipment_items (
    uid BIGINT PRIMARY KEY AUTO_INCREMENT,
    character_id BIGINT NOT NULL,
    item_index INT NOT NULL,
    damage INT NOT NULL DEFAULT 0,
    color INT NOT NULL DEFAULT 0,
    part INT NOT NULL DEFAULT 255,
    in_bank TINYINT NOT NULL DEFAULT 0,
    in_shop TINYINT NOT NULL DEFAULT 0,
    CONSTRAINT fk_equipment_character
        FOREIGN KEY(character_id) REFERENCES characters(id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pets (
    uid BIGINT PRIMARY KEY AUTO_INCREMENT,
    character_id BIGINT NOT NULL,
    pet_id INT NOT NULL,
    name VARCHAR(100) NOT NULL DEFAULT '',
    hp INT NOT NULL DEFAULT 100,
    max_hp INT NOT NULL DEFAULT 100,
    mp INT NOT NULL DEFAULT 50,
    max_mp INT NOT NULL DEFAULT 50,
    attack INT NOT NULL DEFAULT 10,
    defense INT NOT NULL DEFAULT 5,
    magic_attack INT NOT NULL DEFAULT 5,
    magic_defense INT NOT NULL DEFAULT 5,
    hit INT NOT NULL DEFAULT 100,
    dodge INT NOT NULL DEFAULT 0,
    con INT NOT NULL DEFAULT 0,
    str INT NOT NULL DEFAULT 0,
    wis INT NOT NULL DEFAULT 0,
    dex INT NOT NULL DEFAULT 0,
    extra_str INT NOT NULL DEFAULT 0,
    extra_dex INT NOT NULL DEFAULT 0,
    extra_con INT NOT NULL DEFAULT 0,
    extra_wis INT NOT NULL DEFAULT 0,
    life_count INT NOT NULL DEFAULT 0,
    recovery_count INT NOT NULL DEFAULT 0,
    summon_wait_count INT NOT NULL DEFAULT 0,
    exp BIGINT NOT NULL DEFAULT 0,
    max_exp BIGINT NOT NULL DEFAULT 100,
    level INT NOT NULL DEFAULT 1,
    use_exp BIGINT NOT NULL DEFAULT 0,
    feed_exp_limit BIGINT NOT NULL DEFAULT 0,
    evol_id INT NOT NULL DEFAULT 0,
    equip0 BIGINT NOT NULL DEFAULT 0,
    equip1 BIGINT NOT NULL DEFAULT 0,
    equip2 BIGINT NOT NULL DEFAULT 0,
    feed_value INT NOT NULL DEFAULT 0,
    state INT NOT NULL DEFAULT 0,
    main_skill_id INT NOT NULL DEFAULT 0,
    in_shop TINYINT NOT NULL DEFAULT 0,
    shop_price BIGINT NOT NULL DEFAULT 0,
    CONSTRAINT fk_pets_character
        FOREIGN KEY(character_id) REFERENCES characters(id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS horses (
    uid BIGINT PRIMARY KEY AUTO_INCREMENT,
    character_id BIGINT NOT NULL,
    horse_index INT NOT NULL,
    name VARCHAR(100) NOT NULL DEFAULT '',
    horse_type VARCHAR(16) NOT NULL DEFAULT 'MS',
    exp BIGINT NOT NULL DEFAULT 0,
    max_exp BIGINT NOT NULL DEFAULT 1000,
    energy_left INT NOT NULL DEFAULT 0,
    level INT NOT NULL DEFAULT 1,
    equip_color0 INT NOT NULL DEFAULT 0,
    equip_color1 INT NOT NULL DEFAULT 0,
    equip_color2 INT NOT NULL DEFAULT 0,
    equip_color3 INT NOT NULL DEFAULT 0,
    angry INT NOT NULL DEFAULT 0,
    CONSTRAINT fk_horses_character
        FOREIGN KEY(character_id) REFERENCES characters(id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS horse_equipment (
    uid BIGINT PRIMARY KEY AUTO_INCREMENT,
    character_id BIGINT NOT NULL,
    equip_index INT NOT NULL,
    horse_uid BIGINT NOT NULL DEFAULT -1,
    damage INT NOT NULL DEFAULT 0,
    color INT NOT NULL DEFAULT 0,
    part INT NOT NULL DEFAULT 255,
    in_bank TINYINT NOT NULL DEFAULT 0,
    in_shop TINYINT NOT NULL DEFAULT 0,
    CONSTRAINT fk_horse_equipment_character
        FOREIGN KEY(character_id) REFERENCES characters(id)
        ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE INDEX ix_characters_account ON characters(account_id);
CREATE INDEX ix_inventory_character ON inventory_items(character_id);
CREATE INDEX ix_equipment_character ON equipment_items(character_id);
CREATE INDEX ix_pets_character ON pets(character_id);
CREATE INDEX ix_horses_character ON horses(character_id);
CREATE INDEX ix_horse_equipment_character ON horse_equipment(character_id);
