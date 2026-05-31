[CmdletBinding()]
param(
    [int[]]$EmployeeCounts = @(1000, 3000),

    [ValidateRange(1, 60)]
    [int]$Months = 12,

    [ValidateRange(1, 20)]
    [int]$Runs = 3,

    [string]$OutputDirectory = "",

    [switch]$SkipSeed,

    [switch]$SkipUiGrid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$appProject = Join-Path $repoRoot "src\SalaryManager.App\SalaryManager.App.csproj"
$seedScript = Join-Path $PSScriptRoot "Seed-MockPayrollData.ps1"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\perf"
}

if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$runnerRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("SalaryManagerPerfRunner-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $runnerRoot | Out-Null

try {
    $projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$appProject" />
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $runnerRoot "PerfRunner.csproj") -Value $projectXml -Encoding UTF8

    $program = @'
using System;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;
using SalaryManager.App.Services;
using SalaryManager.App.ViewModels;
using SalaryManager.App.Views;
using SalaryManager.Data;
using Syncfusion.Licensing;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: <databasePath> <perfLogPath> <runs> <includeUiGrid>");
    return 2;
}

var databasePath = Path.GetFullPath(args[0]);
var perfLogPath = Path.GetFullPath(args[1]);
var runs = int.Parse(args[2]);
var includeUiGrid = bool.Parse(args[3]);
var now = DateTime.Today;
var timeout = TimeSpan.FromSeconds(120);

Environment.SetEnvironmentVariable("SALARYMANAGER_DB_PATH", databasePath);
Environment.SetEnvironmentVariable("SALARYMANAGER_PERF_LOG_PATH", null);

Directory.CreateDirectory(Path.GetDirectoryName(perfLogPath)!);
if (File.Exists(perfLogPath))
    File.Delete(perfLogPath);

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
QuestPDF.Settings.UseEnvironmentFonts = true;
var syncfusionLicenseKey =
    Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY")
    ?? Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", EnvironmentVariableTarget.User)
    ?? Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", EnvironmentVariableTarget.Machine);
if (!string.IsNullOrWhiteSpace(syncfusionLicenseKey))
    SyncfusionLicenseProvider.RegisterLicense(syncfusionLicenseKey);

SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
if (includeUiGrid && Application.Current is null)
{
    var app = new SalaryManager.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    app.InitializeComponent();
}

using var provider = BuildServices(databasePath, Path.Combine(Path.GetDirectoryName(perfLogPath)!, "settings.json"));
var initializer = provider.GetRequiredService<DatabaseInitializer>();
var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
initializer.StartMigration(factory);
WaitForCompletion(initializer.ReadyTask, "database migration", timeout);

WarmUp(provider, now.Year, now.Month, includeUiGrid, timeout);

Environment.SetEnvironmentVariable("SALARYMANAGER_PERF_LOG_PATH", perfLogPath);

for (var run = 1; run <= runs; run++)
{
    var payroll = provider.GetRequiredService<MonthlyPayrollService>();
    var snapshot = WaitForResult(
        payroll.LoadAsync(new MonthlyPayrollRequest(now.Year, now.Month)),
        $"monthly payroll run {run}",
        timeout);

    var dashboard = provider.GetRequiredService<DashboardViewModel>();
    WaitForCompletion(dashboard.LoadCommand.ExecuteAsync(null), $"dashboard run {run}", timeout);

    var salaryRows = includeUiGrid
        ? MeasureSalarySheetWithGrid(provider, $"salary sheet grid run {run}", timeout)
        : MeasureSalarySheetWithoutGrid(provider, $"salary sheet run {run}", timeout);

    Console.WriteLine(
        $"run={run} payrollRows={snapshot.Rows.Count:N0} dashboardRows={dashboard.ActiveEmployees:N0} salaryRows={salaryRows:N0}");
}

PumpUntilIdle();
Application.Current?.Shutdown();

Console.WriteLine(perfLogPath);
return 0;
    }

static ServiceProvider BuildServices(string databasePath, string settingsPath)
{
    var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
    var services = new ServiceCollection();

    services.AddSingleton<HarnessSqlitePragmaConnectionInterceptor>();
    services.AddPooledDbContextFactory<AppDbContext>((sp, opts) =>
        opts.UseSqlite(connectionString)
            .AddInterceptors(sp.GetRequiredService<HarnessSqlitePragmaConnectionInterceptor>()));

    services.AddSingleton<DatabaseInitializer>();
    services.AddSingleton<PdfSlipService>();
    services.AddSingleton<ExcelExportService>();
    services.AddSingleton<ExcelImportService>();
    services.AddSingleton<MonthlyPayrollService>();
    services.AddSingleton<DialogService, QuietDialogService>();
    services.AddSingleton<BackupService>();
    services.AddSingleton(_ => new AppSettingsService(settingsPath));
    services.AddTransient<DashboardViewModel>();
    services.AddTransient<SalarySheetViewModel>();

    return services.BuildServiceProvider();
}

static void WarmUp(ServiceProvider provider, int year, int month, bool includeUiGrid, TimeSpan timeout)
{
    var payroll = provider.GetRequiredService<MonthlyPayrollService>();
    WaitForCompletion(payroll.LoadAsync(new MonthlyPayrollRequest(year, month)), "warmup monthly payroll", timeout);

    var dashboard = provider.GetRequiredService<DashboardViewModel>();
    WaitForCompletion(dashboard.LoadCommand.ExecuteAsync(null), "warmup dashboard", timeout);

    if (includeUiGrid)
        _ = MeasureSalarySheetWithGrid(provider, "warmup salary sheet grid", timeout);
    else
        _ = MeasureSalarySheetWithoutGrid(provider, "warmup salary sheet", timeout);
}

static int MeasureSalarySheetWithoutGrid(ServiceProvider provider, string name, TimeSpan timeout)
{
    var viewModel = provider.GetRequiredService<SalarySheetViewModel>();
    WaitForCompletion(viewModel.LoadCommand.ExecuteAsync(null), name, timeout);
    return viewModel.Rows.Count;
}

static int MeasureSalarySheetWithGrid(ServiceProvider provider, string name, TimeSpan timeout)
{
    var viewModel = provider.GetRequiredService<SalarySheetViewModel>();
    var view = new SalarySheetView { DataContext = viewModel };
    var window = new Window
    {
        Content = view,
        Width = 1440,
        Height = 900,
        Left = -32000,
        Top = -32000,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None
    };

    window.Show();
    try
    {
        Pump(DispatcherPriority.Loaded);
        Pump(DispatcherPriority.Background);

        var execution = viewModel.LoadCommand.ExecutionTask;
        if (execution is null)
            WaitForCompletion(viewModel.LoadCommand.ExecuteAsync(null), name, timeout);
        else
            WaitForCompletion(execution, name, timeout);

        PumpUntilIdle();
        return viewModel.Rows.Count;
    }
    finally
    {
        window.Close();
        PumpUntilIdle();
    }
}

static T WaitForResult<T>(Task<T> task, string name, TimeSpan timeout)
{
    WaitForCompletion((Task)task, name, timeout);
    return task.GetAwaiter().GetResult();
}

static void WaitForCompletion(Task task, string name, TimeSpan timeout)
{
    var stopwatch = Stopwatch.StartNew();
    while (!task.IsCompleted)
    {
        if (stopwatch.Elapsed > timeout)
            throw new TimeoutException($"{name} did not complete within {timeout.TotalSeconds:0} seconds.");

        Pump(DispatcherPriority.Background);
        Thread.Sleep(1);
    }

    task.GetAwaiter().GetResult();
}

static void PumpUntilIdle()
{
    Pump(DispatcherPriority.ContextIdle);
    Pump(DispatcherPriority.ApplicationIdle);
}

static void Pump(DispatcherPriority priority)
{
    var frame = new DispatcherFrame();
    Dispatcher.CurrentDispatcher.BeginInvoke(
        priority,
        new Action(() => frame.Continue = false));
    Dispatcher.PushFrame(frame);
}

sealed class QuietDialogService : DialogService
{
    public override bool Confirm(string message, string title = "Confirm") => true;
    public override bool ConfirmDestructive(string message, string title = "Confirm Delete") => true;
    public override bool ConfirmWarning(string message, string title = "Confirm") => true;
    public override void Info(string message, string title = "Information") { }
    public override void Error(string message, string title = "Error") => throw new InvalidOperationException(message);
    public override Task<bool> ConfirmAsync(string message, string title = "Confirm") => Task.FromResult(true);
    public override Task<bool> ConfirmDestructiveAsync(string message, string title = "Confirm Delete") => Task.FromResult(true);
    public override Task<bool> ConfirmWarningAsync(string message, string title = "Confirm", string confirmText = "Continue") => Task.FromResult(true);
    public override Task InfoAsync(string message, string title = "Information") => Task.CompletedTask;
    public override Task ErrorAsync(string message, string title = "Error") => throw new InvalidOperationException(message);
    public override string? AskSavePath(string filter, string defaultName) => null;
    public override string? AskOpenPath(string filter) => null;
    public override Task<decimal?> AskNonNegativeDecimalAsync(
        string title,
        string label,
        decimal defaultValue = 0m,
        string confirmText = "Apply") => Task.FromResult<decimal?>(null);
}

sealed class HarnessSqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Apply(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is not SqliteConnection)
            return;

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA synchronous=NORMAL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Apply(DbConnection connection)
    {
        if (connection is not SqliteConnection)
            return;

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA synchronous=NORMAL;";
        command.ExecuteNonQuery();
    }
}
}
'@
    Set-Content -LiteralPath (Join-Path $runnerRoot "Program.cs") -Value $program -Encoding UTF8

    dotnet restore (Join-Path $runnerRoot "PerfRunner.csproj") | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Performance runner restore failed with exit code $LASTEXITCODE."
    }

    $includeUiGrid = -not $SkipUiGrid.IsPresent

    foreach ($employeeCount in $EmployeeCounts) {
        $databasePath = Join-Path $OutputDirectory ("salary-mock-{0}.db" -f $employeeCount)
        $perfLogPath = Join-Path $OutputDirectory ("salary-perf-{0}.log" -f $employeeCount)

        if (-not $SkipSeed.IsPresent) {
            & $seedScript -Employees $employeeCount -Months $Months -DatabasePath $databasePath
            if ($LASTEXITCODE -ne 0) {
                throw "Mock seed failed for $employeeCount employees with exit code $LASTEXITCODE."
            }
        }

        Write-Host ""
        Write-Host ("Measuring {0:N0} employees..." -f $employeeCount)
        dotnet run --project (Join-Path $runnerRoot "PerfRunner.csproj") --no-restore -- $databasePath $perfLogPath $Runs $includeUiGrid
        if ($LASTEXITCODE -ne 0) {
            throw "Performance runner failed for $employeeCount employees with exit code $LASTEXITCODE."
        }

        $lines = @(Get-Content -LiteralPath $perfLogPath)
        $snapshotCandidates = @($lines | Where-Object { $_ -like "*snapshot-cache-candidate*" })
        $gridCandidates = @($lines | Where-Object { $_ -like "*syncfusion-grid-tuning-candidate*" })

        Write-Host ("Log: {0}" -f $perfLogPath)
        Write-Host ("Snapshot-cache candidate lines: {0}" -f $snapshotCandidates.Count)
        Write-Host ("Syncfusion-grid candidate lines: {0}" -f $gridCandidates.Count)
        Write-Host "Recent timings:"
        $lines | Select-Object -Last 12 | ForEach-Object { Write-Host ("  {0}" -f $_) }
    }
}
finally {
    if (Test-Path -LiteralPath $runnerRoot) {
        Remove-Item -LiteralPath $runnerRoot -Recurse -Force
    }
}
