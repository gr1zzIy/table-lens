using DotNetDBF;

namespace TableLens.Core.Models;

public sealed record DbfFieldDefinition(
    string Name,
    NativeDbType Type,
    int Length,
    int DecimalCount,
    string? SourceName = null);
