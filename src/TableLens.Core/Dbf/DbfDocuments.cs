using System.Text.RegularExpressions;
using DotNetDBF;
using TableLens.Core.Models;

namespace TableLens.Core.Dbf;

public static partial class DbfDocuments
{
    public static TableDocument Create(string path, IReadOnlyList<DbfFieldDefinition> definitions, int codePage)
    {
        Validate(definitions);
        var schema = new DbfSchemaService().CreateEmpty(path, definitions);
        var dbf = new DbfTableData { FilePath = path, Fields = schema.Fields, SourceFields = schema.SourceFields, Table = schema.Table,
            DeclaredRecordCount = 0, FileSize = 0, LastWriteTimeUtc = DateTime.MinValue, Signature = DBFSignature.DBase3, LanguageDriver = 0,
            ReadWarningCount = 0, ReadWarnings = [], IsPartialRead = false };
        return new TableDocument { FilePath = path, Format = TableFormat.Dbf, Table = dbf.Table, Encoding = TableFileService.StrictEncoding(codePage),
            Options = new ReadOptions { DbfCodePage = codePage }, Dbf = dbf, IsNew = true };
    }

    public static void Validate(IReadOnlyList<DbfFieldDefinition> definitions)
    {
        if (definitions.Count == 0) throw new InvalidDataException("Додайте хоча б одне поле.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in definitions)
        {
            if (!FieldName().IsMatch(field.Name)) throw new InvalidDataException($"{field.Name}: назва має містити 1–10 латинських літер, цифр або _, перший символ — літера чи _.");
            if (!names.Add(field.Name)) throw new InvalidDataException($"Поле {field.Name} повторюється.");
            if (field.Type is not (NativeDbType.Char or NativeDbType.Numeric or NativeDbType.Float or NativeDbType.Date or NativeDbType.Logical)) throw new InvalidDataException("Непідтримуваний DBF-тип.");
            if (field.Type == NativeDbType.Char && field.Length is < 1 or > 254) throw new InvalidDataException($"{field.Name}: довжина тексту — 1–254 байти.");
            if (field.Type is NativeDbType.Numeric or NativeDbType.Float && (field.Length is < 1 or > 20 || field.DecimalCount < 0 || field.DecimalCount > 15 || (field.DecimalCount > 0 && field.DecimalCount + 2 > field.Length)))
                throw new InvalidDataException($"{field.Name}: перевірте довжину числа та кількість десяткових знаків.");
        }
        if (definitions.Count > 2046 || definitions.Sum(f => f.Type == NativeDbType.Date ? 8 : f.Type == NativeDbType.Logical ? 1 : f.Length) >= 65535)
            throw new InvalidDataException("Структура перевищує обмеження dBase III.");
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,9}$")]
    private static partial Regex FieldName();
}
