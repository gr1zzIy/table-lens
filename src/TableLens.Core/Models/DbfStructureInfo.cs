namespace TableLens.Core.Models;

public sealed record DbfStructureInfo(string FilePath, IReadOnlyList<DbfFieldInfo> Fields);

public sealed record DbfFieldComparison(
    string Status,
    int? LeftOrdinal,
    int? RightOrdinal,
    string FieldName,
    string LeftType,
    string RightType,
    int? LeftLength,
    int? RightLength,
    int? LeftDecimalCount,
    int? RightDecimalCount,
    string Differences);
