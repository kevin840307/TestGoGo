# Database initialization

Database structure is stored as versioned SQL files. C# code does not embed `CREATE TABLE` or `CREATE INDEX` statements.

## SQLite

```text
Database/Sqlite/
├─ 001_schema.sql
├─ 002_indexes.sql
└─ 003_legacy_character_columns.sql
```

At server startup:

```text
SqliteGameStore
  -> SqliteSchemaMigrator.Apply()
     -> read Database/Sqlite/001_schema.sql
     -> read Database/Sqlite/002_indexes.sql
     -> process legacy @ensure-column directives
```

The SQL files are copied into build and publish output by `SGO.Server.csproj`.

## MySQL

A MySQL-compatible schema is kept separately:

```text
Database/MySql/
└─ 001_schema.sql
```

When `MySqlGameStore` is implemented, it should execute the files under `Database/MySql/` rather than duplicating DDL in C#.

## Future seed data

Keep seed data separate from schema migrations, for example:

```text
Database/Sqlite/010_seed_reference_data.sql
Database/MySql/010_seed_reference_data.sql
```

Game content that already comes from legacy files such as `Dat/Item/*.itd`, `Dat/Enemy/*.csv`, and `Dat/Map/*` should normally stay in those legacy data files rather than being duplicated into SQL.
