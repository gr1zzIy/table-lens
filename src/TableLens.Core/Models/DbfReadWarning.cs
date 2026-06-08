namespace TableLens.Core.Models;

public sealed record DbfReadWarning(
    int RecordNumber,
    string FieldName,
    string RawValue,
    string Message);
