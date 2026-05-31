[CmdletBinding()]
param(
    [ValidateRange(1, 100000)]
    [int]$Employees = 500,

    [ValidateRange(1, 60)]
    [int]$Months = 6,

    [string]$DatabasePath = (Join-Path $PSScriptRoot "..\artifacts\perf\salary-mock.db"),

    [switch]$KeepExisting
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$dataProject = Join-Path $repoRoot "src\SalaryManager.Data\SalaryManager.Data.csproj"

if (-not [System.IO.Path]::IsPathRooted($DatabasePath)) {
    $DatabasePath = Join-Path $repoRoot $DatabasePath
}

$DatabasePath = [System.IO.Path]::GetFullPath($DatabasePath)
$databaseDirectory = Split-Path -Parent $DatabasePath
New-Item -ItemType Directory -Force -Path $databaseDirectory | Out-Null

if (-not $KeepExisting) {
    foreach ($path in @($DatabasePath, "$DatabasePath-wal", "$DatabasePath-shm")) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
}

$runnerRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("SalaryManagerMockSeeder-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $runnerRoot | Out-Null

try {
    $projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$dataProject" />
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $runnerRoot "MockSeeder.csproj") -Value $projectXml -Encoding UTF8

    $program = @'
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SalaryManager.Data;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;

var databasePath = args[0];
var employeeCount = int.Parse(args[1]);
var monthCount = int.Parse(args[2]);

Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath
}.ToString();

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connectionString)
    .Options;

using var db = new AppDbContext(options);
db.Database.Migrate();
db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");

ClearExistingData(db);
SeedMockData(db, employeeCount, monthCount);

Console.WriteLine($"Seeded {employeeCount:N0} employees across {monthCount:N0} month(s).");
Console.WriteLine(databasePath);

static void ClearExistingData(AppDbContext db)
{
    db.EmployeeGroupMemberships.RemoveRange(db.EmployeeGroupMemberships);
    db.AttendanceRecords.RemoveRange(db.AttendanceRecords);
    db.Advances.RemoveRange(db.Advances);
    db.SalaryRevisions.RemoveRange(db.SalaryRevisions);
    db.Employees.RemoveRange(db.Employees);
    db.EmployeeGroups.RemoveRange(db.EmployeeGroups);
    db.SaveChanges();
}

static void SeedMockData(AppDbContext db, int employeeCount, int monthCount)
{
    var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    var groups = new[]
    {
        new EmployeeGroup { Id = 1, Name = "Cash" },
        new EmployeeGroup { Id = 2, Name = "Factory" },
        new EmployeeGroup { Id = 3, Name = "Night" },
        new EmployeeGroup { Id = 4, Name = "Office" },
    };
    db.EmployeeGroups.AddRange(groups);

    var employees = new List<Employee>(employeeCount);
    var memberships = new List<EmployeeGroupMembership>(employeeCount * 2);
    for (var i = 1; i <= employeeCount; i++)
    {
        var paymentMode = i % 11 == 0
            ? PaymentMode.Cash
            : i % 3 == 0
                ? PaymentMode.IciciBank
                : PaymentMode.OtherBank;
        var baseSalary = 12000m + (i % 80) * 425m;
        var employee = new Employee
        {
            Id = i,
            Name = $"Employee {i:00000}",
            BaseSalary = baseSalary,
            IsActive = i % 19 != 0,
            AccountNumber = paymentMode == PaymentMode.Cash ? null : (100000000000 + i).ToString(),
            IfscCode = paymentMode == PaymentMode.Cash ? null : "ICIC0MOCK01",
            AadharNumber = (400000000000 + i).ToString(),
            UanNumber = (900000000000 + i).ToString(),
            InsuranceNumber = $"INS{i:000000}",
            PhoneNumber = (9000000000L + i).ToString(),
            PaymentMode = paymentMode,
            JoiningDate = currentMonth.AddMonths(-(i % 36)).AddDays(i % 24),
            CreatedAt = currentMonth.AddMonths(-36).AddDays(i % 28)
        };
        employees.Add(employee);

        if (paymentMode == PaymentMode.Cash || i % 17 == 0)
            memberships.Add(new EmployeeGroupMembership { EmployeeId = i, EmployeeGroupId = 1 });
        memberships.Add(new EmployeeGroupMembership { EmployeeId = i, EmployeeGroupId = 2 + (i % 3) });
    }
    db.Employees.AddRange(employees);
    db.EmployeeGroupMemberships.AddRange(memberships);

    var attendance = new List<AttendanceRecord>(employeeCount * monthCount);
    var advances = new List<Advance>(employeeCount * monthCount);
    var revisions = new List<SalaryRevision>(Math.Max(1, employeeCount / 8));

    foreach (var employee in employees)
    {
        for (var monthOffset = 0; monthOffset < monthCount; monthOffset++)
        {
            var period = currentMonth.AddMonths(-monthOffset);
            var daysAbsent = (employee.Id + monthOffset) % 5;
            var salaryPaid = SalaryCalculator.CalculateDefaultSalaryPaid(
                employee.BaseSalary,
                period.Year,
                period.Month,
                daysAbsent);
            var usesTds = employee.PaymentMode != PaymentMode.Cash
                          && salaryPaid > SalaryCalculator.TdsThreshold
                          && employee.Id % 17 != 0;
            var hasOverride = monthOffset == 0 && employee.Id % 23 == 0;
            var effectiveSalaryPaid = hasOverride
                ? SalaryCalculator.RoundSalaryPaid(salaryPaid + 750m)
                : salaryPaid;

            attendance.Add(new AttendanceRecord
            {
                EmployeeId = employee.Id,
                Year = period.Year,
                Month = period.Month,
                DaysAbsent = daysAbsent,
                BaseSalaryOverride = hasOverride ? effectiveSalaryPaid : null,
                EsicDeduction = usesTds ? 0m : 75m + employee.Id % 125,
                PfDeduction = usesTds ? 0m : 125m + employee.Id % 175,
                TdsDeduction = usesTds ? SalaryCalculator.CalculateDefaultTds(effectiveSalaryPaid) : 0m,
                IsTdsManualOverride = usesTds && employee.Id % 29 == 0,
                NetSalaryOverride = null
            });

            if (employee.Id % 3 == 0)
            {
                advances.Add(new Advance
                {
                    EmployeeId = employee.Id,
                    Date = period.AddDays(2 + employee.Id % 10),
                    Amount = 500m + (employee.Id % 20) * 100m,
                    EntryType = AdvanceEntryType.Given,
                    Note = "Mock advance"
                });
            }

            if (employee.Id % 5 == 0)
            {
                advances.Add(new Advance
                {
                    EmployeeId = employee.Id,
                    Date = period,
                    Amount = 250m + (employee.Id % 8) * 50m,
                    EntryType = AdvanceEntryType.Deducted,
                    SourceKey = AdvanceSourceKeys.Salary(period.Year, period.Month),
                    Note = "Mock salary deduction"
                });
            }
        }

        if (employee.Id % 10 == 0)
        {
            revisions.Add(new SalaryRevision
            {
                EmployeeId = employee.Id,
                OldSalary = employee.BaseSalary - 500m,
                NewSalary = employee.BaseSalary,
                ChangedAt = currentMonth.AddDays(employee.Id % 27),
                Note = "Mock annual revision"
            });
        }
    }

    db.AttendanceRecords.AddRange(attendance);
    db.Advances.AddRange(advances);
    db.SalaryRevisions.AddRange(revisions);
    db.SaveChanges();
}
'@
    Set-Content -LiteralPath (Join-Path $runnerRoot "Program.cs") -Value $program -Encoding UTF8

    dotnet run --project (Join-Path $runnerRoot "MockSeeder.csproj") -- $DatabasePath $Employees $Months
    if ($LASTEXITCODE -ne 0) {
        throw "Mock data seeder failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $runnerRoot) {
        Remove-Item -LiteralPath $runnerRoot -Recurse -Force
    }
}

Write-Host ""
Write-Host "Use this mock database for a performance run:"
Write-Host ("  `$env:SALARYMANAGER_DB_PATH = `"{0}`"" -f $DatabasePath)
$perfLogPath = Join-Path $databaseDirectory "salary-perf.log"
Write-Host ("  `$env:SALARYMANAGER_PERF_LOG_PATH = `"{0}`"" -f $perfLogPath)
Write-Host "  dotnet run --project src\SalaryManager.App"
Write-Host ("  Get-Content `"{0}`" -Tail 40" -f $perfLogPath)
