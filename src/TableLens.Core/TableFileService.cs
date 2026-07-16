using System.Data;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CsvHelper;
using CsvHelper.Configuration;
using DotNetDBF;
using TableLens.Core.Dbf;
using TableLens.Core.Models;

namespace TableLens.Core;

/// <summary>One small entry point for file formats; no plugin framework or DI container.</summary>
public sealed class TableFileService
{
    static TableFileService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public TableDocument Read(string path, ReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        options = (options ?? new ReadOptions()).Clone();
        var before = Fingerprint(path);
        var document = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".dbf" => ReadDbf(path, options, cancellationToken),
            ".csv" or ".tsv" => ReadCsv(path, options, cancellationToken),
            ".json" => ReadJson(path, options, cancellationToken),
            _ => throw new NotSupportedException("Підтримуються DBF, CSV, TSV та JSON.")
        };
        cancellationToken.ThrowIfCancellationRequested();
        document.Fingerprint = Fingerprint(path);
        if (before != document.Fingerprint) throw new IOException("Файл змінився під час читання. Відкрийте його повторно.");
        document.Table.AcceptChanges();
        return document;
    }

    private static TableDocument ReadDbf(string path, ReadOptions options, CancellationToken token)
    {
        var encoding = StrictEncoding(options.DbfCodePage);
        var data = new DbfTableReader().Read(path, encoding, token);
        var result = new TableDocument { FilePath = path, Format = TableFormat.Dbf, Table = data.Table, Encoding = encoding, Options = options, Dbf = data };
        if (data.IsPartialRead || data.ReadWarningCount > 0)
        {
            result.ReadOnlyReason = "Частково прочитаний або пошкоджений DBF доступний лише для перегляду.";
            result.Warnings.AddRange(data.ReadWarnings.Select(w => $"Рядок {w.RecordNumber}, {w.FieldName}: {w.Message}"));
        }
        else if (data.Signature != DBFSignature.DBase3 || data.SourceFields.Any(f => f.DataType is not (NativeDbType.Char or NativeDbType.Numeric or NativeDbType.Float or NativeDbType.Date or NativeDbType.Logical)))
            result.ReadOnlyReason = "Memo/DBT, FoxPro та інші варіанти DBF доступні лише для перегляду. Редагування підтримує dBase III без memo.";
        if (data.Fields.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Fields.Count)
            result.ReadOnlyReason = "DBF із повторюваними назвами полів доступний лише для перегляду.";
        return result;
    }

    private static TableDocument ReadCsv(string path, ReadOptions options, CancellationToken token)
    {
        var encoding = DetectTextEncoding(path, options.TextCodePage);
        options.TextCodePage = encoding.CodePage;
        using var stream = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        var config = CsvConfig(options, path);
        using var csv = new CsvReader(stream, config);
        var table = NewTable(path);
        var result = new TableDocument { FilePath = path, Format = TableFormat.Csv, Table = table, Encoding = encoding, Options = options };
        if (!csv.Read()) return result;
        // Save the detected separator for a lossless subsequent save.
        options.Delimiter = csv.Parser.Delimiter;
        if (options.HasHeader) csv.ReadHeader();
        var headers = options.HasHeader ? csv.HeaderRecord! : Enumerable.Range(1, csv.Parser.Count).Select(i => $"Column{i}").ToArray();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            var name = string.IsNullOrWhiteSpace(header) ? $"Column{table.Columns.Count + 1}" : header;
            var unique = name;
            for (var n = 2; !used.Add(unique); n++) unique = $"{name}_{n}";
            table.Columns.Add(new DataColumn(unique, typeof(string)) { Caption = unique });
            if (unique != header)
            {
                result.ReadOnlyReason = "Порожні або повторювані CSV-заголовки нормалізовано для перегляду. Збережіть експорт в окремий файл.";
                result.Warnings.Add($"Заголовок «{header}» відображається як «{unique}».");
            }
        }
        if (!options.HasHeader) AddCsvRow(csv, table, token);
        while (csv.Read()) AddCsvRow(csv, table, token);
        return result;
    }

    private static void AddCsvRow(CsvReader csv, DataTable table, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (csv.Parser.Count != table.Columns.Count)
            throw new InvalidDataException($"CSV, рядок {csv.Parser.Row}: очікується {table.Columns.Count} полів, знайдено {csv.Parser.Count}. Перевірте роздільник у параметрах імпорту.");
        table.Rows.Add(Enumerable.Range(0, table.Columns.Count).Select(i => (object)(csv.GetField(i) ?? "")).ToArray());
    }

    private static CsvConfiguration CsvConfig(ReadOptions options, string path) => new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = options.HasHeader,
        Delimiter = options.Delimiter ?? (Path.GetExtension(path).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? "\t" : ","),
        DetectDelimiter = options.Delimiter is null && !Path.GetExtension(path).Equals(".tsv", StringComparison.OrdinalIgnoreCase),
        DetectDelimiterValues = [",", ";", "\t", "|"],
        IgnoreBlankLines = false,
        ExceptionMessagesContainRawData = false
    };

    private static TableDocument ReadJson(string path, ReadOptions options, CancellationToken token)
    {
        // JSON is UTF-8 according to RFC 8259; StreamReader also accepts a BOM.
        using var text = new StreamReader(path, DetectTextEncoding(path, 65001), true);
        using var parsed = JsonDocument.Parse(text.ReadToEnd(), new JsonDocumentOptions { MaxDepth = 128 });
        var root = parsed.RootElement;
        var layout = JsonLayout.ObjectArray;
        var arrayProperty = options.JsonArrayProperty;
        JsonObject? container = null;
        if (root.ValueKind == JsonValueKind.Object)
        {
            var candidates = root.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array && p.Value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.Object)).ToArray();
            if (arrayProperty is null && candidates.Length == 1) arrayProperty = candidates[0].Name;
            if (!string.IsNullOrEmpty(arrayProperty))
            {
                if (!root.TryGetProperty(arrayProperty, out var array) || array.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"JSON: не знайдено масив «{arrayProperty}».");
                layout = JsonLayout.WrappedArray;
                container = JsonNode.Parse(root.GetRawText())!.AsObject();
                root = array;
            }
            else layout = JsonLayout.SingleObject;
        }
        else if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("JSON має містити об'єкт або масив об'єктів.");

        var rows = layout == JsonLayout.SingleObject ? [root] : root.EnumerateArray().ToArray();
        if (rows.Any(r => r.ValueKind != JsonValueKind.Object)) throw new InvalidDataException("Табличний JSON має містити масив об'єктів; вкладені значення дозволені всередині полів.");
        var names = new List<string>();
        var nameSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            var inRow = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in row.EnumerateObject())
            {
                if (!inRow.Add(p.Name)) throw new InvalidDataException($"JSON містить повторюваний ключ «{p.Name}».");
                if (nameSet.Add(p.Name)) names.Add(p.Name);
            }
        }
        var table = NewTable(path);
        foreach (var name in names)
        {
            var values = rows.Where(r => r.TryGetProperty(name, out _)).Select(r => r.GetProperty(name)).Where(v => v.ValueKind != JsonValueKind.Null).ToArray();
            var type = values.Length > 0 && values.All(v => v.ValueKind == JsonValueKind.Number && TryExactDecimal(v, out _)) ? typeof(decimal)
                : values.Length > 0 && values.All(v => v.ValueKind is JsonValueKind.True or JsonValueKind.False) ? typeof(bool)
                : values.All(v => v.ValueKind == JsonValueKind.String) ? typeof(string) : typeof(object);
            // Keep JSON names verbatim, including the empty key. DataColumn needs a non-empty internal name.
            var internalName = name.Length == 0 ? "__empty_json_key__" : name;
            while (table.Columns.Contains(internalName)) internalName = "_" + internalName;
            var column = new DataColumn(internalName, type) { Caption = name };
            column.ExtendedProperties["JsonName"] = name;
            table.Columns.Add(column);
        }
        var result = new TableDocument { FilePath = path, Format = TableFormat.Json, Table = table, Encoding = StrictEncoding(65001), Options = options,
            JsonLayout = layout, JsonContainer = container, JsonArrayProperty = arrayProperty };
        foreach (var source in rows)
        {
            token.ThrowIfCancellationRequested();
            var row = table.NewRow();
            var missing = new HashSet<int>();
            for (var i = 0; i < names.Count; i++)
            {
                if (!source.TryGetProperty(names[i], out var value)) { missing.Add(i); continue; }
                row[i] = value.ValueKind switch
                {
                    JsonValueKind.Null => DBNull.Value,
                    JsonValueKind.String => value.GetString()!,
                    JsonValueKind.Number when TryExactDecimal(value, out var number) => number,
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => value.Clone()
                };
            }
            table.Rows.Add(row);
            result.MissingJsonProperties[row] = missing;
        }
        return result;
    }

    public SaveResult Save(TableDocument document, CancellationToken token = default)
    {
        if (!document.CanEdit) throw new InvalidOperationException(document.ReadOnlyReason);
        if (!document.IsNew && (!File.Exists(document.FilePath) || Fingerprint(document.FilePath) != document.Fingerprint))
            throw new IOException("Файл змінено іншою програмою. Збереження заблоковано. Експортуйте свої зміни в окремий файл або перечитайте оригінал.");
        var result = WriteSafely(document, document.FilePath, document.Format, null, token, checkSource: true);
        document.IsNew = false;
        document.Fingerprint = Fingerprint(document.FilePath);
        return result;
    }

    public SaveResult ExportRows(TableDocument document, string destination, TableFormat format, IReadOnlyList<DataRow>? rows = null, CancellationToken token = default)
    {
        if (rows is null) return Export(document, destination, format, token: token);
        var subset = document.Table.Clone();
        foreach (var row in rows)
        {
            if (!ReferenceEquals(row.Table, document.Table)) throw new ArgumentException("Рядок належить іншій таблиці.");
            subset.ImportRow(row);
        }
        return Export(document, destination, format, subset, token, rows);
    }

    public SaveResult Export(TableDocument document, string destination, TableFormat format, DataTable? subset = null, CancellationToken token = default, IReadOnlyList<DataRow>? jsonRows = null)
    {
        if (format == TableFormat.Dbf && document.Dbf is null) throw new NotSupportedException("Експорт у DBF доступний для DBF-джерел зі збереженням їх структури.");
        if (AppPaths.PathComparer.Equals(Path.GetFullPath(destination), document.FilePath))
            throw new IOException("Для експорту оберіть інший файл. Для оригіналу використовуйте «Зберегти».");
        return WriteSafely(document, Path.GetFullPath(destination), format, subset, token, checkSource: false, jsonRows);
    }

    private SaveResult WriteSafely(TableDocument document, string path, TableFormat format, DataTable? subset, CancellationToken token, bool checkSource, IReadOnlyList<DataRow>? jsonRows = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        string? backup = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var table = subset ?? document.Table;
            if (format == TableFormat.Dbf)
            {
                if (document.ReadOnlyReason is not null) throw new InvalidOperationException("Цей DBF можна експортувати лише у CSV/JSON.");
                var data = document.Dbf!;
                var originalTable = data.Table;
                data.Table = table;
                try { new DbfTableWriter().Save(data, document.Encoding, temporary, false); }
                finally { data.Table = originalTable; }
            }
            else if (format == TableFormat.Csv) WriteCsv(document, table, temporary, !checkSource);
            else WriteJson(document, table, temporary, jsonRows);
            token.ThrowIfCancellationRequested();
            if (checkSource && !document.IsNew && Fingerprint(path) != document.Fingerprint)
                throw new IOException("Файл змінився під час збереження. Оригінал не перезаписано.");
            if (document.IsNew && checkSource && File.Exists(path)) throw new IOException("Файл з такою назвою вже існує. Оберіть іншу назву.");
            if (File.Exists(path))
            {
                backup = $"{path}.{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.bak";
                File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
            }
            else File.Move(temporary, path);
            return new SaveResult(path, backup);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void WriteCsv(TableDocument document, DataTable table, string path, bool exporting)
    {
        var encoding = exporting ? new UTF8Encoding(false, true) : document.Encoding;
        var options = exporting ? new ReadOptions { Delimiter = ",", HasHeader = true } : document.Options;
        using var stream = new StreamWriter(path, false, encoding);
        using var csv = new CsvWriter(stream, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = options.Delimiter ?? ",", NewLine = "\n" });
        if (options.HasHeader)
        {
            foreach (DataColumn column in table.Columns) csv.WriteField(column.Caption);
            csv.NextRecord();
        }
        foreach (DataRow row in table.Rows)
        {
            foreach (var value in row.ItemArray) csv.WriteField(TableDocument.Display(value));
            csv.NextRecord();
        }
    }

    private static void WriteJson(TableDocument document, DataTable table, string path, IReadOnlyList<DataRow>? selectedRows = null)
    {
        var rows = new JsonArray();
        foreach (var row in selectedRows ?? table.Rows.Cast<DataRow>().ToArray())
        {
            var obj = new JsonObject();
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var value = row[i];
                if (value == DBNull.Value && document.MissingJsonProperties.TryGetValue(row, out var missing) && missing.Contains(i)) continue;
                var name = table.Columns[i].ExtendedProperties["JsonName"] as string ?? table.Columns[i].Caption;
                obj.Add(name, value == DBNull.Value ? null : value is JsonElement element ? JsonNode.Parse(element.GetRawText()) : JsonSerializer.SerializeToNode(value));
            }
            rows.Add(obj);
        }
        JsonNode output = rows;
        if (document.Format == TableFormat.Json && document.JsonLayout == JsonLayout.SingleObject)
        {
            if (rows.Count != 1) throw new InvalidOperationException("JSON-об'єкт має містити рівно один рядок. Для набору записів використовуйте масив об'єктів.");
            output = rows[0]!.DeepClone();
        }
        else if (document.Format == TableFormat.Json && document.JsonLayout == JsonLayout.WrappedArray)
        {
            var container = document.JsonContainer!.DeepClone().AsObject();
            container[document.JsonArrayProperty!] = rows;
            output = container;
        }
        var json = output.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(path, json + "\n", new UTF8Encoding(false, true));
        // Never replace an existing file with an invalid JSON document.
        using var verification = JsonDocument.Parse(File.ReadAllText(path));
    }

    public static Encoding StrictEncoding(int codePage) => Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    private static bool TryExactDecimal(JsonElement value, out decimal number)
    {
        // TryGetDecimal may successfully round very small/high-precision values.
        // Keep those as JsonElement rather than silently changing their value.
        return value.TryGetDecimal(out number) && CanonicalNumber(value.GetRawText()) == CanonicalNumber(number.ToString(CultureInfo.InvariantCulture));
    }
    private static string CanonicalNumber(string value)
    {
        var exponentIndex = value.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? value : value[..exponentIndex];
        var exponent = exponentIndex < 0 ? BigInteger.Zero : BigInteger.Parse(value[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        var point = mantissa.IndexOf('.');
        if (point >= 0) exponent -= mantissa.Length - point - 1;
        var digits = mantissa.TrimStart('-', '+').Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return "0";
        var shortened = digits.TrimEnd('0'); exponent += digits.Length - shortened.Length;
        return (mantissa.StartsWith('-') ? "-" : "") + shortened + "e" + exponent.ToString(CultureInfo.InvariantCulture);
    }
    private static Encoding DetectTextEncoding(string path, int fallback)
    {
        using var file = System.IO.File.OpenRead(path);
        Span<byte> bytes = stackalloc byte[4]; var count = file.Read(bytes);
        var codePage = count >= 4 && bytes.SequenceEqual(new byte[] { 0xFF, 0xFE, 0, 0 }) ? 12000
            : count >= 4 && bytes.SequenceEqual(new byte[] { 0, 0, 0xFE, 0xFF }) ? 12001
            : count >= 3 && bytes[..3].SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }) ? 65001
            : count >= 2 && bytes[..2].SequenceEqual(new byte[] { 0xFF, 0xFE }) ? 1200
            : count >= 2 && bytes[..2].SequenceEqual(new byte[] { 0xFE, 0xFF }) ? 1201 : fallback;
        return StrictEncoding(codePage);
    }
    public static string Fingerprint(string path) { using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read); return Convert.ToHexString(SHA256.HashData(file)); }
    private static DataTable NewTable(string path) => new(Path.GetFileNameWithoutExtension(path)) { Locale = CultureInfo.InvariantCulture, CaseSensitive = false };
}

public sealed record SaveResult(string FilePath, string? BackupPath);
