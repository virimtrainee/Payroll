$ErrorActionPreference = "Stop"

$key = [Environment]::GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY")
if ([string]::IsNullOrWhiteSpace($key)) {
    $key = [Environment]::GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", "User")
}
if ([string]::IsNullOrWhiteSpace($key)) {
    $key = [Environment]::GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", "Machine")
}
if ([string]::IsNullOrWhiteSpace($key)) {
    throw "SYNCFUSION_LICENSE_KEY was not found in process, User, or Machine environment variables."
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$targetPath = Join-Path $repoRoot "src\SalaryManager.App\Services\SyncfusionLicenseKey.local.cs"
$escapedKey = $key.Replace("\", "\\").Replace("""", "\""")

$content = @"
namespace SalaryManager.App.Services;

internal static partial class SyncfusionLicenseKey
{
    static partial void ProvideEmbeddedKey(ref string? key)
    {
        key = "$escapedKey";
    }
}
"@

Set-Content -LiteralPath $targetPath -Value $content -Encoding UTF8
Write-Host "Created local embedded Syncfusion license file: $targetPath"
