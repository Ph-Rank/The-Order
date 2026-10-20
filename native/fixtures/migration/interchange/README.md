# Independent engine fixtures

All names, IDs, notes and `Q:\ForeignInterop` paths are synthetic. No game,
artwork, account, private backup or credential is present. `SHA256.json` freezes
the checked files; binary serialization UUIDs/timestamps need not regenerate to
the same byte sequence. The fixtures exercise publicly verified formats rather
than an invented interchangeable JSON contract.

| Fixture | Producer | Expected facts |
| --- | --- | --- |
| vnite-pouchdb9-mixed.zip | PouchDB 9.0.0, Vnite 4.14.0 data shape | Two live games plus a deleted document; VN's second winning revision; 7,200 whole seconds from 7,200,123 ms; 1,800 session + 600 manual daily seconds; ordinary Action game excluded |
| vnite-pouchdb9-snappy.zip | Same independent PouchDB writer, note update then two close/reopen cycles | Real Snappy-compressed `.ldb` tables, including repeated Unicode-safe synthetic notes; reproduces corruption failure in the old NuGet native engine and passes with the pinned engine |
| playnite10-sdk-mixed.zip | Unmodified Playnite 10.62 `GameDatabase.GetCollectionMapper`, SDK `Game`, `Backup.BackupData` in x86 .NET Framework | Three records; exact linked VN has 12,345 seconds, seven launches, score 83/100 and HTML notes; ordinary game and title-only homonym excluded |
| reinamanager-sqlite32.zip | SQLite `VACUUM INTO` after constructing the official fresh 0.32.0 schema with synthetic rows | One VN, 90 minutes total, one reliable 30-minute session; source count two is preserved independently |
| lunabox-duckdb-csv.zip | DuckDB 1.5.4 `EXPORT DATABASE ... (FORMAT CSV)` using official 1.13.3 DDL | One VN, 1,800 seconds; original TIMESTAMPTZ 2026-01-02 23:45 +09, same UTC instant after export-zone formatting; note and score |

These source fixtures are independent of `ManagerArchiveWriter`. Unit tests also
perform a separate export/reimport roundtrip for all five target managers. They
do not claim that every historical manager version or a real user migration has
been tested. PouchDB is test tooling only; DuckDB is not a product dependency.

Pinned upstream versions, schema provenance, license attribution and separate
native application evidence are in `docs/manager-interchange.md` and
`THIRD_PARTY_NOTICES.md`. The new schema captures retain the ReinaManager and
LunaBox AGPL terms. The fixture records and locally written generation logic are
The Order synthetic test data under the repository license.

Generation method: PouchDB `put → put(_rev) → remove → close`; Playnite SDK
`GetCollection<Game>().Insert` followed by `Backup.BackupData`; SQLite fixed DDL
and parameterized inserts followed by `VACUUM INTO`; DuckDB fixed DDL/inserts and
native `EXPORT DATABASE`. No reader, exporter or manager program is invoked by
merely running these fixtures' unit tests, and no source SQL is executed.
