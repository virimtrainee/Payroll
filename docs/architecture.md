# Architecture

Salary Manager is a single-user WPF app backed by SQLite. UI state lives in view models, persistence is isolated behind EF Core `AppDbContext`, and payroll/ledger rules live in the data project so tests can exercise them without launching WPF.

## Runtime Data

Runtime files live in the same folder as the running app:

- `salary.db` - SQLite database.
- `salary.db-wal` / `salary.db-shm` - SQLite sidecars when present.
- `settings.json` - user settings such as the saved ICICI debit account.
- `Slips\` - generated salary slips.

On startup, `DatabaseInitializer` prepares the app folder and `Slips\` directory, then runs EF migrations against the local `salary.db`. The app does not migrate runtime data to `%LocalAppData%` or any other per-user directory.

## Loading Model

Views are materialized lazily through WPF `DataTemplate`s. View models gate database loads on `DatabaseInitializer.ReadyTask`. Filter-driven views use generation guards so stale async loads do not overwrite newer selections.

## Validation

Employee and payroll validation lives in `SalaryManager.Data.Validation`. UI flows call these validators before saving, posting advance deductions, generating reports, or exporting payment files. Invalid data is reported to the user instead of being silently clamped or defaulted.

## EF Migrations

Runtime migrations run automatically at startup. Design-time EF commands use `AppDbContextDesignTimeFactory`, which points at a scratch database beside the running assemblies by default.
