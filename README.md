# Salary Manager

A small Windows desktop payroll app for tracking employees, monthly attendance, advance ledgers, and salary disbursement. Built for a single user / single workstation; backed by a local SQLite database.

Generates monthly payroll PDFs and Excel summaries, plus an ICICI bulk-payment file for salary transfers and per-employee advance ledgers.

## Stack

- **WPF** on **.NET 10** (`net10.0-windows`)
- **MVVM** via [`CommunityToolkit.Mvvm`](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- **EF Core 10** + **SQLite** (`Microsoft.EntityFrameworkCore.Sqlite`)
- **ClosedXML** for Excel I/O
- **QuestPDF** for PDF rendering (Community license)
- **CsvHelper** for CSV exports
- **xUnit** for tests

## Getting started

Requires the **.NET 10 SDK** (or newer). Windows only — the app project targets `net10.0-windows` because it's WPF.

```powershell
# Clone
git clone https://github.com/kta136/SalaryManager.git
cd SalaryManager

# Restore + build
dotnet build SalaryManager.slnx

# Run the app
dotnet run --project src/SalaryManager.App

# Run the tests
dotnet test SalaryManager.slnx
```

The first launch creates `salary.db` in the same folder as the running app and applies all EF migrations automatically. Runtime files stay beside the app: `salary.db`, `settings.json`, SQLite sidecars, and the `Slips\` folder are not moved to `%LocalAppData%` or any other per-user data directory.

## Project layout

```
SalaryManager.slnx
├── src/
│   ├── SalaryManager.App/         WPF UI: views, viewmodels, services
│   │   ├── Helpers/               RangeObservableCollection, month options
│   │   ├── Services/              DialogService, BackupService,
│   │   │                          ExcelImportService, ExcelExportService,
│   │   │                          PdfSlipService, DatabaseInitializer
│   │   ├── ViewModels/            One per tab + Add/Employees dialogs
│   │   └── Views/                 Matching XAML user controls
│   └── SalaryManager.Data/        EF Core layer
│       ├── Entities/              Employee, AttendanceRecord, Advance,
│       │                          SalaryRevision, PaymentMode
│       ├── Migrations/            EF migrations (run automatically)
│       ├── Services/              SalaryCalculator, AdvanceLedger,
│       │                          AdvanceQueryExtensions
│       └── Validation/            Employee and payroll validation rules
└── tests/SalaryManager.Tests/     xUnit tests
```

## Features

- **Dashboard** — active-employee count, gross monthly payroll, outstanding advances, recent advance and salary-revision activity.
- **Salary Sheet** — editable per-month attendance grid (days absent, ESIC/PF deductions, advance deduction). Live recalculation per row. Posts deductions back to the advance ledger.
- **Advances** — per-employee running ledger with searchable employee rail, balance-only filter, and add-entry form.
- **Reports** — month-bound payroll summary as PDF / Excel / CSV; per-employee advance ledger as PDF / Excel.
- **Employees** modal — add, toggle active/inactive, change payment mode (Cash / ICICI / OtherBank), import from Excel using flexible header aliases (`BNF_NAME`, `EMPLOYEE_NAME`, `AMOUNT`, `BENE_ACC_NO`, `BENE_IFSC`, `PYMT_MODE`, etc.).
- **Backup / Restore** — `VACUUM INTO` produces an atomic, consistent SQLite snapshot regardless of journal mode.
- **ICICI Excel export** — generates the bulk-payment template with `PAB_VENDOR` / `FT` / `NEFT` rows ready to upload.

## Docs

- [Architecture](docs/architecture.md)
- [Import format](docs/import-format.md)
- [ICICI export](docs/icici-export.md)
- [Backup and restore](docs/backup-restore.md)

## Development

```powershell
dotnet format SalaryManager.slnx --verify-no-changes --verbosity minimal
dotnet build SalaryManager.slnx --configuration Release
dotnet test SalaryManager.slnx --configuration Release
```

For EF commands, use the data project directly. The design-time context uses a scratch database beside the running assemblies rather than `%LocalAppData%` or the user's live runtime database.

```powershell
dotnet ef migrations list --project src/SalaryManager.Data
```

## License

MIT — see [LICENSE](LICENSE).
