namespace SalaryManager.Data.Validation;

public sealed record ValidationIssue(
    string Field,
    string Message,
    string? Code = null,
    int? RowNumber = null)
{
    public string ToDisplayString()
    {
        var prefix = RowNumber is int rowNumber ? $"Row {rowNumber}: " : string.Empty;
        return string.IsNullOrWhiteSpace(Field)
            ? prefix + Message
            : $"{prefix}{Field}: {Message}";
    }
}

public sealed class ValidationResult
{
    public static ValidationResult Success { get; } = new([]);
    public static ValidationResult Valid => Success;

    public ValidationResult(IEnumerable<ValidationIssue> issues)
    {
        Issues = issues.ToList();
    }

    public IReadOnlyList<ValidationIssue> Issues { get; }
    public IReadOnlyList<ValidationIssue> Errors => Issues;

    public bool IsValid => Issues.Count == 0;

    public string ToMessage()
        => ToDisplayString();

    public string ToDisplayString()
        => string.Join(Environment.NewLine, Issues.Select(i => i.ToDisplayString()));

    public static ValidationResult From(params ValidationIssue[] issues) => new(issues);

    public static ValidationResult FromErrors(IEnumerable<ValidationIssue> issues) => new(issues);
}
