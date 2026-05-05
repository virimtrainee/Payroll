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

The first launch creates `salary.db` next to the executable and applies all EF migrations automatically. The DB file is per-user and is gitignored.

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
│       └── Services/              SalaryCalculator, AdvanceLedger,
│                                  AdvanceQueryExtensions
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

## Performance notes

The codebase had a deep performance pass; the changes are listed in the initial commit message. Highlights:

- Database migration runs on a background thread after the window is shown; viewmodels gate their loads on `DatabaseInitializer.ReadyTask`.
- Tab content is materialized lazily via implicit `DataTemplate`s — only the active tab's view is constructed.
- Advance balances are aggregated in SQL via `SumBalancesByEmployeeAsync` / `SumOutstandingAsync` instead of pulling rows into memory.
- Attendance and employee saves are O(1) round-trip patterns (single `ToDictionaryAsync` then mutate) instead of N+1 lookups.
- `RangeObservableCollection<T>.ReplaceAll` raises a single Reset event instead of N Adds for bulk row loads.
- The Advances rail uses `ICollectionView.Filter` so search keystrokes don't rebuild the underlying collection.
- PDF and Excel generation runs inside `Task.Run` to keep the UI thread responsive.

## ICICI debit account

The bulk-payment Excel hard-codes a debit account number in [`ExcelExportService.cs`](src/SalaryManager.App/Services/ExcelExportService.cs) (`DebitAccountNo`). Update that constant to match your bank account before exporting.

## License

MIT — see [LICENSE](LICENSE).
