using System.Diagnostics;
using System.IO;

namespace SalaryManager.App.Services;

internal static class PerformanceTrace
{
    public const string LogPathEnvironmentVariable = "SALARYMANAGER_PERF_LOG_PATH";

    private const double PayrollCacheCandidateMs = 250;
    private const double DashboardPayrollCandidateMs = 180;
    private const double GridTuneCandidateMs = 200;
    private const int GridTuneCandidateRows = 500;
    private static readonly object FileLock = new();

    public static void MonthlyPayrollLoad(
        int year,
        int month,
        int rowCount,
        TimeSpan total,
        TimeSpan employeeQuery,
        TimeSpan attendanceQuery,
        TimeSpan advanceQuery,
        TimeSpan rowBuild)
    {
        Write(
            "MonthlyPayroll.Load",
            $"period={year:0000}-{month:00} rows={rowCount} total={Ms(total)} " +
            $"employees={Ms(employeeQuery)} attendance={Ms(attendanceQuery)} " +
            $"advances={Ms(advanceQuery)} rowsBuild={Ms(rowBuild)} " +
            $"decision={CacheDecision(total)}");
    }

    public static void DashboardLoad(
        int year,
        int month,
        int rowCount,
        TimeSpan total,
        TimeSpan payroll,
        TimeSpan database)
    {
        Write(
            "Dashboard.Load",
            $"period={year:0000}-{month:00} rows={rowCount} total={Ms(total)} " +
            $"payroll={Ms(payroll)} database={Ms(database)} " +
            $"decision={DashboardDecision(payroll)}");
    }

    public static void SalarySheetLoad(
        int year,
        int month,
        int rowCount,
        TimeSpan total,
        TimeSpan payroll,
        TimeSpan attendance,
        TimeSpan rowBuild,
        TimeSpan rowsReplace)
    {
        Write(
            "SalarySheet.Load",
            $"period={year:0000}-{month:00} rows={rowCount} total={Ms(total)} " +
            $"payroll={Ms(payroll)} attendance={Ms(attendance)} " +
            $"rowsBuild={Ms(rowBuild)} rowsReplace={Ms(rowsReplace)} " +
            $"cacheDecision={CacheDecision(payroll)} gridDecision={GridDecision(rowCount, rowsReplace)}");
    }

    public static void SalaryGridRenderIdle(int rowCount, TimeSpan elapsed)
    {
        Write(
            "SalaryGrid.RenderIdle",
            $"rows={rowCount} elapsed={Ms(elapsed)} decision={GridDecision(rowCount, elapsed)}");
    }

    private static string CacheDecision(TimeSpan elapsed)
        => elapsed.TotalMilliseconds >= PayrollCacheCandidateMs
            ? "snapshot-cache-candidate"
            : "no-cache-needed-yet";

    private static string DashboardDecision(TimeSpan payroll)
        => payroll.TotalMilliseconds >= DashboardPayrollCandidateMs
            ? "snapshot-cache-candidate"
            : "no-cache-needed-yet";

    private static string GridDecision(int rowCount, TimeSpan elapsed)
        => rowCount >= GridTuneCandidateRows || elapsed.TotalMilliseconds >= GridTuneCandidateMs
            ? "syncfusion-grid-tuning-candidate"
            : "no-grid-tuning-needed-yet";

    private static string Ms(TimeSpan elapsed)
        => $"{elapsed.TotalMilliseconds:0.0}ms";

    private static void Write(string name, string message)
    {
        var line = $"[SalaryManager.Perf] {name} {message}";
        Trace.WriteLine(line);

        var logPath = Environment.GetEnvironmentVariable(LogPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(logPath))
            return;

        lock (FileLock)
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(logPath));
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(
                fullPath,
                $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
        }
    }
}
