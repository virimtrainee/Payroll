namespace SalaryManager.Data.Services;

public static class GroupNameNormalizer
{
    public static string Normalize(string name)
    {
        var parts = name.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).Trim().ToUpperInvariant();
    }
}
