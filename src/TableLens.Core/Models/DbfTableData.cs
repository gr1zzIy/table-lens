using System.Data;
using DotNetDBF;

namespace TableLens.Core.Models;

public sealed class DbfTableData
{
    public required string FilePath { get; init; }

    public required IReadOnlyList<DbfFieldInfo> Fields { get; set; }

    public required IReadOnlyList<DBFField> SourceFields { get; set; }

    public required DataTable Table { get; set; }

    public required int DeclaredRecordCount { get; set; }

    public required long FileSize { get; set; }

    public required DateTime LastWriteTimeUtc { get; set; }

    public required byte Signature { get; init; }

    public required byte LanguageDriver { get; init; }

    public required int ReadWarningCount { get; init; }

    public required IReadOnlyList<DbfReadWarning> ReadWarnings { get; init; }

    public required bool IsPartialRead { get; init; }

    public string? PartialReadMessage { get; init; }

    public DbfSchemaValidationResult? SchemaValidation { get; set; }

    public int LoadedRecordCount => Table.Rows.Count;
}
