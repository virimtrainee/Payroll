namespace SalaryManager.App.Services;

internal static partial class SyncfusionLicenseKey
{
    public static string? Value
    {
        get
        {
            string? key = null;
            ProvideEmbeddedKey(ref key);
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
    }

    static partial void ProvideEmbeddedKey(ref string? key);
}
