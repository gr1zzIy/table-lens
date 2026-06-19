using System.Text.Encodings.Web;
using System.Text.Json;
using TableLens.Core.Models;

namespace TableLens.Core.Dbf;

public sealed class DbfFieldCatalog
{
    public const string ConfigFileName = "dbf-field-catalog.json";

    private readonly string _configPath;
    private Dictionary<string, DbfExpectedSchema> _schemas;
    private IReadOnlyList<string> _masks;

    public DbfFieldCatalog(string? dataDirectory = null)
    {
        var configDirectory = dataDirectory ?? AppPaths.DataDirectory;
        _configPath = Path.Combine(configDirectory, ConfigFileName);
        EnsureUserConfigExists(configDirectory);
        _schemas = LoadSchemas(_configPath);
        _masks = CreateMaskList(_schemas.Keys);
    }

    private void EnsureUserConfigExists(string configDirectory)
    {
        if (File.Exists(_configPath)) return;

        var bundledConfigPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(bundledConfigPath))
            throw new FileNotFoundException("Не знайдено базовий файл конфігурації DBF.", bundledConfigPath);

        Directory.CreateDirectory(configDirectory);
        File.Copy(bundledConfigPath, _configPath, overwrite: false);
    }

    public DbfExpectedSchema? FindSchema(string filePath)
    {
        var mask = FindMask(filePath);
        return mask is not null && _schemas.TryGetValue(mask, out var schema) ? schema : null;
    }

    public string? FindMask(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        return _masks.FirstOrDefault(mask => fileName.Equals(mask, StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith(mask + "_", StringComparison.OrdinalIgnoreCase));
    }

    public DbfSchemaValidationResult? Validate(string filePath, IReadOnlyList<DbfFieldInfo> actualFields)
    {
        var schema = FindSchema(filePath);
        if (schema is null) return null;

        var unmatchedExpected = schema.Fields.Select((field, index) => (field, index)).ToList();
        var unmatchedActual = actualFields.Select((field, index) => (field, index)).ToList();

        foreach (var actual in actualFields)
        {
            var expectedIndex = unmatchedExpected.FindIndex(item => item.field.Name.Equals(actual.Name, StringComparison.Ordinal));
            if (expectedIndex < 0) continue;
            var actualIndex = unmatchedActual.FindIndex(item => item.field.Ordinal == actual.Ordinal);
            unmatchedExpected.RemoveAt(expectedIndex);
            if (actualIndex >= 0) unmatchedActual.RemoveAt(actualIndex);
        }

        var caseMismatches = new List<string>();
        for (var actualIndex = unmatchedActual.Count - 1; actualIndex >= 0; actualIndex--)
        {
            var actual = unmatchedActual[actualIndex];
            var expectedIndex = unmatchedExpected.FindIndex(item =>
                item.field.Name.Equals(actual.field.Name, StringComparison.OrdinalIgnoreCase));
            if (expectedIndex < 0) continue;
            caseMismatches.Add($"{actual.field.Name} → {unmatchedExpected[expectedIndex].field.Name}");
            unmatchedActual.RemoveAt(actualIndex);
            unmatchedExpected.RemoveAt(expectedIndex);
        }

        caseMismatches.Reverse();
        var missing = unmatchedExpected.Select(item => item.field.Name).ToArray();
        var unexpected = schema.AllowAdditionalFields ? [] : unmatchedActual.Select(item => item.field.Name).ToArray();

        return new DbfSchemaValidationResult
        {
            FileMask = schema.FileMask,
            MissingFields = missing,
            UnexpectedFields = unexpected,
            CaseMismatches = caseMismatches
        };
    }

    public string? GetDescription(string filePath, string fieldName, int? ordinal = null)
    {
        var schema = FindSchema(filePath);
        if (schema is null) return null;
        if (ordinal is > 0 && ordinal <= schema.Fields.Count &&
            schema.Fields[ordinal.Value - 1].Name.Equals(fieldName, StringComparison.Ordinal))
        {
            return schema.Fields[ordinal.Value - 1].Description;
        }
        var exact = schema.Fields.FirstOrDefault(field => field.Name.Equals(fieldName, StringComparison.Ordinal));
        if (exact is not null) return exact.Description;
        var caseInsensitive = schema.Fields.Where(field => field.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase)).ToArray();
        return caseInsensitive.Length == 1 ? caseInsensitive[0].Description : null;
    }

    public string ReplaceSchema(string filePath, IReadOnlyList<DbfFieldInfo> actualFields,
        IReadOnlyList<DbfExpectedField> editedFields)
    {
        if (actualFields.Count != editedFields.Count) throw new ArgumentException("Кількість полів та описів не збігається.");
        var mask = FindMask(filePath) ?? DeriveMask(filePath);
        var fields = actualFields.Select((field, index) =>
            new DbfExpectedField(field.Name, editedFields[index].Description.Trim())).ToArray();
        _schemas[mask] = new DbfExpectedSchema(mask, fields, AllowAdditionalFields: false);
        SaveSchemas();
        return mask;
    }

    public string UpsertFields(string filePath, IReadOnlyList<DbfFieldInfo> allActualFields,
        IReadOnlyList<DbfFieldInfo> selectedFields, IReadOnlyList<DbfExpectedField> editedFields)
    {
        if (selectedFields.Count != editedFields.Count) throw new ArgumentException("Кількість полів та описів не збігається.");
        var mask = FindMask(filePath) ?? DeriveMask(filePath);
        _schemas.TryGetValue(mask, out var current);
        var fields = current?.Fields.ToList() ?? [];

        for (var fieldIndex = 0; fieldIndex < selectedFields.Count; fieldIndex++)
        {
            var actual = selectedFields[fieldIndex];
            var description = editedFields[fieldIndex].Description.Trim();
            var occurrence = allActualFields.Count(field => field.Ordinal <= actual.Ordinal &&
                field.Name.Equals(actual.Name, StringComparison.Ordinal));
            var matchingIndexes = fields.Select((field, index) => (field, index))
                .Where(item => item.field.Name.Equals(actual.Name, StringComparison.Ordinal))
                .Select(item => item.index).ToArray();
            var index = matchingIndexes.Length >= occurrence ? matchingIndexes[occurrence - 1] : -1;
            if (index >= 0) fields[index] = new DbfExpectedField(actual.Name, description);
            else fields.Add(new DbfExpectedField(actual.Name, description));
        }

        _schemas[mask] = new DbfExpectedSchema(mask, fields, current?.AllowAdditionalFields ?? true);
        SaveSchemas();
        return mask;
    }

    public string RemoveFields(string filePath, IReadOnlyCollection<int> fieldIndexes)
    {
        var mask = FindMask(filePath) ?? throw new InvalidOperationException("Для таблиці не знайдено маску в конфігурації.");
        var schema = _schemas[mask];
        var indexes = fieldIndexes.Where(index => index >= 0 && index < schema.Fields.Count).ToHashSet();
        if (indexes.Count == 0) throw new InvalidOperationException("Не вибрано жодного поля для видалення.");

        var fields = schema.Fields.Where((_, index) => !indexes.Contains(index)).ToArray();
        _schemas[mask] = new DbfExpectedSchema(mask, fields, schema.AllowAdditionalFields);
        SaveSchemas();
        return mask;
    }

    private static Dictionary<string, DbfExpectedSchema> LoadSchemas(string configPath)
    {
        if (!File.Exists(configPath)) throw new FileNotFoundException($"Не знайдено конфігурацію DBF-полів: {configPath}", configPath);

        DbfFieldCatalogConfig config;
        try
        {
            var json = File.ReadAllText(configPath);
            config = JsonSerializer.Deserialize<DbfFieldCatalogConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? throw new InvalidDataException("Конфігурація DBF-полів порожня.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Некоректний JSON у файлі {configPath}: {exception.Message}", exception);
        }

        if (config.Schemas.Count == 0) throw new InvalidDataException($"У файлі {configPath} не задано жодної маски DBF.");

        var schemas = new Dictionary<string, DbfExpectedSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in config.Schemas)
        {
            var mask = item.FileMask.Trim();
            if (string.IsNullOrWhiteSpace(mask)) throw new InvalidDataException("У конфігурації DBF є порожня маска файлу.");
            if (schemas.ContainsKey(mask)) throw new InvalidDataException($"Маска {mask} повторюється у конфігурації DBF.");

            if (item.Fields.Any(field => string.IsNullOrWhiteSpace(field.Name)))
                throw new InvalidDataException($"Для маски {mask} вказано поле без назви.");

            var fields = item.Fields.Select(field => new DbfExpectedField(field.Name.Trim(), field.Description?.Trim() ?? string.Empty)).ToArray();
            schemas.Add(mask, new DbfExpectedSchema(mask, fields, item.AllowAdditionalFields));
        }
        return schemas;
    }

    private void SaveSchemas()
    {
        var config = new DbfFieldCatalogConfig
        {
            Schemas = _schemas.Values.OrderBy(schema => schema.FileMask, StringComparer.OrdinalIgnoreCase)
                .Select(schema => new DbfFieldCatalogSchema
                {
                    FileMask = schema.FileMask,
                    AllowAdditionalFields = schema.AllowAdditionalFields,
                    Fields = schema.Fields.ToList()
                }).ToList()
        };
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        var temporaryPath = _configPath + ".tmp";
        var backupPath = _configPath + ".bak";

        File.WriteAllText(temporaryPath, json + Environment.NewLine);
        try
        {
            File.Copy(_configPath, backupPath, overwrite: true);
            File.Move(temporaryPath, _configPath, overwrite: true);
        }
        catch
        {
            _schemas = LoadSchemas(_configPath);
            _masks = CreateMaskList(_schemas.Keys);
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
        _masks = CreateMaskList(_schemas.Keys);
    }

    private static IReadOnlyList<string> CreateMaskList(IEnumerable<string> masks) => masks
        .OrderByDescending(mask => mask.Length)
        .ThenBy(mask => mask, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string DeriveMask(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var separatorIndex = fileName.IndexOf('_');
        var mask = separatorIndex > 0 ? fileName[..separatorIndex] : fileName;
        if (string.IsNullOrWhiteSpace(mask)) throw new InvalidDataException("Не вдалося визначити маску DBF-файлу.");
        return mask;
    }
}
