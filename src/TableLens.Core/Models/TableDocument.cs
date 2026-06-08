using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNetDBF;

namespace TableLens.Core.Models;

public enum TableFormat { Dbf, Csv, Json }
public enum JsonLayout { ObjectArray, SingleObject, WrappedArray }

public sealed class ReadOptions
{
    public int DbfCodePage { get; set; } = 866;
    public int TextCodePage { get; set; } = 65001;
    public string? Delimiter { get; set; }
    public bool HasHeader { get; set; } = true;
    public string? JsonArrayProperty { get; set; }
    public ReadOptions Clone() => (ReadOptions)MemberwiseClone();
}

public sealed class TableDocument
{
    public required string FilePath { get; set; }
    public required TableFormat Format { get; init; }
    public required DataTable Table { get; set; }
    public required Encoding Encoding { get; init; }
    public required ReadOptions Options { get; init; }
    public string Fingerprint { get; set; } = "";
    public DbfTableData? Dbf { get; init; }
    public JsonLayout JsonLayout { get; init; }
    public JsonObject? JsonContainer { get; init; }
    public string? JsonArrayProperty { get; init; }
    public Dictionary<DataRow, HashSet<int>> MissingJsonProperties { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool IsNew { get; set; }
    public string? ReadOnlyReason { get; set; }
    public bool CanEdit => ReadOnlyReason is null;
    public string FormatLabel => Format == TableFormat.Dbf ? "DBF" : Format == TableFormat.Csv ? "CSV" : "JSON";

    public IReadOnlyList<ColumnInfo> GetColumns() => Table.Columns.Cast<DataColumn>().Select((c, i) =>
    {
        var dbf = Dbf?.Fields.ElementAtOrDefault(i);
        return new ColumnInfo(i + 1, c.Caption, dbf?.DbfType ?? c.DataType.Name, dbf?.Length, dbf?.DecimalCount,
            dbf?.Characteristics ?? (c.DataType == typeof(object) ? "Змішані або вкладені JSON-значення" : "—"));
    }).ToArray();

    public static string Display(object? value) => value switch
    {
        null or DBNull => "",
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        JsonElement element => element.GetRawText(),
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    public static object ParseCell(string text, DataColumn column, object? previous = null, bool nullValue = false)
    {
        if (nullValue) return DBNull.Value;
        if (column.DataType == typeof(string)) return text;
        if (text.Length == 0) return DBNull.Value;
        if (column.DataType == typeof(decimal))
        {
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number)) return number;
            throw new FormatException($"{column.Caption}: «{text}» не є числом.");
        }
        if (column.DataType == typeof(bool)) return text.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "так" => true,
            "false" or "0" or "ні" => false,
            _ => throw new FormatException($"{column.Caption}: введіть true або false.")
        };
        if (column.DataType == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.CurrentCulture);
        // Mixed JSON columns retain strings as strings; nested cells remain valid JSON.
        if (previous is JsonElement element)
        {
            using var parsed = JsonDocument.Parse(text);
            if (element.ValueKind == JsonValueKind.Number && parsed.RootElement.ValueKind != JsonValueKind.Number)
                throw new FormatException($"{column.Caption}: очікується JSON-число.");
            if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array && parsed.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                throw new FormatException($"{column.Caption}: очікується JSON-об'єкт або масив.");
            return parsed.RootElement.Clone();
        }
        if (previous is decimal) return ParseCell(text, new DataColumn(column.ColumnName, typeof(decimal)));
        if (previous is bool) return ParseCell(text, new DataColumn(column.ColumnName, typeof(bool)));
        return text;
    }
}

public sealed record ColumnInfo(int Ordinal, string Name, string Type, int? Length, int? Decimals, string Description);
