# Backup And Restore

The live database is `salary.db` in the same folder as the running app.

## Backup

Backups use SQLite `VACUUM INTO` to create a consistent snapshot even when SQLite sidecar files exist. The backup target cannot be the live database path.

## Restore

Restore validates the selected file with SQLite `PRAGMA integrity_check` before replacing the live database. The app removes stale `salary.db-wal` and `salary.db-shm` sidecars around restore and then shuts down. Restart the app after restore so all view models reopen against the restored database.

## Data Location

Salary Manager always uses the database beside the app. It does not copy or move runtime data to `%LocalAppData%`; moving the app folder together with `salary.db`, `settings.json`, sidecar files, and `Slips\` moves the full local data set.
