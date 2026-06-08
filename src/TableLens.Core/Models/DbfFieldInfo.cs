namespace TableLens.Core.Models;

public sealed record DbfFieldInfo(
    int Ordinal,
    string Name,
    string DataColumnName,
    string DbfType,
    string DotNetType,
    int Length,
    int DecimalCount,
    bool IsIndexed,
    int WorkAreaId,
    string Characteristics);
