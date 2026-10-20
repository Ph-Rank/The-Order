-- Official 0.32.0 fresh portable schema; no user rows.
CREATE TABLE "seaql_migrations" ( "version" varchar NOT NULL PRIMARY KEY, "applied_at" integer NOT NULL );
CREATE TABLE "games" (
            "id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            "id_type" TEXT NOT NULL,
            "date" TEXT,
            "localpath" TEXT,
            "savepath" TEXT,
            "autosave" INTEGER DEFAULT 0,
            "clear" INTEGER DEFAULT 0,
            "created_at" INTEGER DEFAULT (strftime('%s', 'now')),
            "updated_at" INTEGER DEFAULT (strftime('%s', 'now'))
        , custom_data TEXT, maxbackups INTEGER DEFAULT 20, "le_launch" integer DEFAULT 0, "magpie" integer DEFAULT 0, user_rating REAL
            GENERATED ALWAYS AS (
                CAST(json_extract(custom_data, '$.user_rating') AS REAL)
            ) VIRTUAL, executable TEXT, launch_type TEXT NOT NULL DEFAULT 'local'
                CHECK (launch_type IN ('local', 'steam')), steam_launch_id TEXT
                CHECK (
                    steam_launch_id IS NULL OR (
                        length(steam_launch_id) BETWEEN 1 AND 20
                        AND substr(steam_launch_id, 1, 1) BETWEEN '1' AND '9'
                        AND steam_launch_id NOT GLOB '*[^0-9]*'
                        AND (
                            length(steam_launch_id) < 20
                            OR steam_launch_id <= '18446744073709551615'
                        )
                    )
                )
                CHECK (
                    (launch_type = 'local' AND steam_launch_id IS NULL)
                    OR (launch_type = 'steam' AND steam_launch_id IS NOT NULL)
                ));
CREATE TABLE "game_sessions" (
            "session_id" INTEGER PRIMARY KEY AUTOINCREMENT,
            "game_id" INTEGER NOT NULL,
            "start_time" INTEGER NOT NULL,
            "end_time" INTEGER NOT NULL,
            "duration" INTEGER NOT NULL,
            "date" TEXT NOT NULL,
            FOREIGN KEY("game_id") REFERENCES "games"("id") ON DELETE CASCADE
        );
CREATE TABLE "game_statistics" (
            "game_id" INTEGER PRIMARY KEY,
            "total_time" INTEGER,
            "session_count" INTEGER,
            "last_played" INTEGER,
            "daily_stats" TEXT,
            FOREIGN KEY("game_id") REFERENCES "games"("id") ON DELETE CASCADE
        );
CREATE TABLE "savedata" (
            "id" INTEGER PRIMARY KEY AUTOINCREMENT,
            "game_id" INTEGER NOT NULL,
            "file" TEXT NOT NULL,
            "backup_time" INTEGER NOT NULL,
            "file_size" INTEGER NOT NULL,
            FOREIGN KEY("game_id") REFERENCES "games"("id") ON DELETE CASCADE
        );
CREATE TABLE "user" (
            "id" INTEGER PRIMARY KEY,
            "bgm_auth" TEXT,
            "save_root_path" TEXT,
            "db_backup_path" TEXT
        , "le_path" text NULL, "magpie_path" text NULL, "vndb_token" TEXT, install_root_path TEXT, "hikarinagi_auth" TEXT, "default_le_launch" BOOLEAN NOT NULL DEFAULT 0, "default_magpie" BOOLEAN NOT NULL DEFAULT 0, "nextmoe_auth" TEXT);
CREATE TABLE "collections" ( "id" integer NOT NULL PRIMARY KEY AUTOINCREMENT, "name" text NOT NULL, "parent_id" integer, "sort_order" integer NOT NULL DEFAULT 0, "icon" text, "created_at" integer DEFAULT ((strftime('%s', 'now'))), "updated_at" integer DEFAULT ((strftime('%s', 'now'))), FOREIGN KEY ("parent_id") REFERENCES "collections" ("id") ON DELETE CASCADE );
CREATE TABLE "game_collection_link" ( "id" integer NOT NULL PRIMARY KEY AUTOINCREMENT, "game_id" integer NOT NULL, "collection_id" integer NOT NULL, "sort_order" integer NOT NULL DEFAULT 0, "created_at" integer DEFAULT ((strftime('%s', 'now'))), FOREIGN KEY ("game_id") REFERENCES "games" ("id") ON DELETE CASCADE, FOREIGN KEY ("collection_id") REFERENCES "collections" ("id") ON DELETE CASCADE );
CREATE TABLE game_sources (
                game_id INTEGER NOT NULL,
                source TEXT NOT NULL CHECK (trim(source) <> ''),
                external_id TEXT,
                data TEXT,
                score REAL GENERATED ALWAYS AS (
                    CAST(json_extract(data, '$.score') AS REAL)
                ) VIRTUAL,
                rank INTEGER GENERATED ALWAYS AS (
                    CAST(json_extract(data, '$.rank') AS INTEGER)
                ) VIRTUAL,
                PRIMARY KEY (game_id, source),
                FOREIGN KEY (game_id) REFERENCES games(id) ON DELETE CASCADE,
                CHECK (external_id IS NOT NULL OR data IS NOT NULL),
                CHECK (data IS NULL OR json_valid(data))
            );
CREATE TABLE tasks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_type TEXT NOT NULL CHECK (trim(task_type) <> ''),
                    title TEXT NOT NULL CHECK (trim(title) <> ''),
                    status TEXT NOT NULL CHECK (status IN (
                        'pending', 'running', 'paused', 'completed', 'failed', 'cancelled'
                    )),
                    stage TEXT,
                    payload_json TEXT NOT NULL CHECK (json_valid(payload_json)),
                    result_json TEXT CHECK (result_json IS NULL OR json_valid(result_json)),
                    progress_current INTEGER NOT NULL DEFAULT 0 CHECK (progress_current >= 0),
                    progress_total INTEGER CHECK (progress_total IS NULL OR progress_total >= 0),
                    progress_unit TEXT,
                    dedupe_key TEXT,
                    error_code TEXT,
                    error_message TEXT,
                    created_at INTEGER NOT NULL,
                    started_at INTEGER,
                    updated_at INTEGER NOT NULL,
                    finished_at INTEGER
                );
CREATE UNIQUE INDEX "idx_game_collection_link_unique" ON "game_collection_link" ("game_id", "collection_id");
CREATE INDEX idx_games_id_type ON games(id_type);
CREATE INDEX idx_games_localpath ON games(localpath);
CREATE INDEX idx_games_date_asc ON games((date IS NULL), date ASC, id ASC);
CREATE INDEX idx_games_date_desc ON games((date IS NULL), date DESC, id ASC);
CREATE INDEX idx_game_sessions_game_start_time ON game_sessions(game_id, start_time DESC);
CREATE INDEX idx_game_sessions_start_time ON game_sessions(start_time DESC);
CREATE INDEX idx_savedata_game_backup_time ON savedata(game_id, backup_time DESC);
CREATE INDEX idx_collections_parent_sort_order ON collections(parent_id, sort_order);
CREATE INDEX idx_game_collection_link_collection_sort_order ON game_collection_link(collection_id, sort_order);
CREATE INDEX idx_game_sources_source_external_id ON game_sources(source, external_id) WHERE external_id IS NOT NULL AND trim(external_id) <> '';
CREATE INDEX idx_game_sources_source_score ON game_sources(source, score DESC) WHERE score IS NOT NULL;
CREATE INDEX idx_game_sources_source_rank ON game_sources(source, rank) WHERE rank IS NOT NULL;
CREATE INDEX idx_games_user_rating ON games(user_rating DESC) WHERE user_rating IS NOT NULL AND user_rating > 0;
CREATE INDEX idx_tasks_status ON tasks(status);
CREATE INDEX idx_tasks_type ON tasks(task_type);
CREATE INDEX idx_tasks_created_at ON tasks(created_at DESC);
CREATE UNIQUE INDEX idx_tasks_active_dedupe
                ON tasks(dedupe_key)
                WHERE dedupe_key IS NOT NULL
                  AND status IN ('pending', 'running', 'paused');
INSERT INTO user (id) VALUES (1);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20250927_000001_baseline_migration',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20250928_000002_split_games_table',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20250930_000003_add_collections',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20251229_000004_hybrid_single_table',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260104_000005_add_le_magpie_fields',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260131_000006_migrate_clear_to_play_status',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260201_000007_clean_empty_strings',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260318_000008_add_vndb_token_and_collection_sync',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260331_000009_add_kungal_support',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260505_000010_remove_redundant_created_at',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260508_000011_bgm_oauth',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260525_000012_move_custom_date_to_games',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260706_000013_reconcile_indexes',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260706_000014_migrate_game_sources',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260712_000015_split_game_local_path',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260722_000016_backfill_game_defaults',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260801_000017_add_tasks',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260805_000018_hikarinagi_oauth',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260809_000019_add_steam_launch',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260922_000020_savedata_backup_root_semantics',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20260923_000021_tool_integration_defaults',0);
INSERT INTO seaql_migrations (version,applied_at) VALUES ('m20261006_000022_nextmoe_oauth',0);