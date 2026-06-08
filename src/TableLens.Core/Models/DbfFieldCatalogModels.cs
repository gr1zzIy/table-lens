namespace TableLens.Core.Models;

public sealed record DbfExpectedField(string Name, string Description);

public sealed class DbfFieldCatalogConfig
{
    public List<DbfFieldCatalogSchema> Schemas { get; init; } = [];
}

public sealed class DbfFieldCatalogSchema
{
    public string FileMask { get; init; } = string.Empty;

    public bool AllowAdditionalFields { get; init; }

    public List<DbfExpectedField> Fields { get; init; } = [];
}

public sealed record DbfExpectedSchema(
    string FileMask,
    IReadOnlyList<DbfExpectedField> Fields,
    bool AllowAdditionalFields = false);

public sealed class DbfSchemaValidationResult
{
    public required string FileMask { get; init; }

    public required IReadOnlyList<string> MissingFields { get; init; }

    public required IReadOnlyList<string> UnexpectedFields { get; init; }

    public required IReadOnlyList<string> CaseMismatches { get; init; }

    public bool IsValid => MissingFields.Count == 0 && UnexpectedFields.Count == 0 && CaseMismatches.Count == 0;

    public int IssueCount => MissingFields.Count + UnexpectedFields.Count + CaseMismatches.Count;

    public string ToDetailedText()
    {
        var lines = new List<string> { $"Очікувана маска: {FileMask}" };
        if (MissingFields.Count > 0) lines.Add($"Відсутні поля: {string.Join(", ", MissingFields)}");
        if (UnexpectedFields.Count > 0) lines.Add($"Неочікувані поля: {string.Join(", ", UnexpectedFields)}");
        if (CaseMismatches.Count > 0) lines.Add($"Відрізняється регістр: {string.Join(", ", CaseMismatches)}");
        return string.Join(Environment.NewLine, lines);
    }
}
