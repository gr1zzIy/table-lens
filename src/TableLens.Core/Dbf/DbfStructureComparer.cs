using System.Text;
using TableLens.Core.Models;
using DotNetDBF;

namespace TableLens.Core.Dbf;

public sealed class DbfStructureComparer
{
    public DbfStructureInfo ReadStructure(string filePath, Encoding encoding)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("DBF-файл не знайдено.", filePath);
        }

        DbfFileAccess.EnsureAvailable(filePath);
        using var reader = new DBFReader(filePath) { CharEncoding = encoding };
        var fields = reader.Fields.Select((field, index) => new DbfFieldInfo(
            index + 1,
            field.Name,
            field.Name,
            field.DataType.ToString(),
            field.Type.Name,
            field.FieldLength,
            field.DecimalCount,
            field.indexFieldFlag != 0,
            field.workAreaId,
            Describe(field))).ToArray();
        return new DbfStructureInfo(Path.GetFullPath(filePath), fields);
    }

    public IReadOnlyList<DbfFieldComparison> Compare(DbfStructureInfo left, DbfStructureInfo right)
    {
        var unmatchedLeft = left.Fields.ToList();
        var unmatchedRight = right.Fields.ToList();
        var matches = new Dictionary<int, DbfFieldInfo>();

        foreach (var leftField in left.Fields)
        {
            var rightIndex = unmatchedRight.FindIndex(field => field.Name.Equals(leftField.Name, StringComparison.Ordinal));
            if (rightIndex < 0) continue;
            matches[leftField.Ordinal] = unmatchedRight[rightIndex];
            unmatchedRight.RemoveAt(rightIndex);
            unmatchedLeft.Remove(leftField);
        }

        foreach (var leftField in unmatchedLeft.ToArray())
        {
            var rightIndex = unmatchedRight.FindIndex(field =>
                field.Name.Equals(leftField.Name, StringComparison.OrdinalIgnoreCase));
            if (rightIndex < 0) continue;
            matches[leftField.Ordinal] = unmatchedRight[rightIndex];
            unmatchedRight.RemoveAt(rightIndex);
            unmatchedLeft.Remove(leftField);
        }

        var matchedRightOrdinals = new HashSet<int>();
        var result = new List<DbfFieldComparison>();

        foreach (var leftField in left.Fields)
        {
            if (!matches.TryGetValue(leftField.Ordinal, out var rightField))
            {
                result.Add(CreateMissing(leftField, true));
                continue;
            }

            matchedRightOrdinals.Add(rightField.Ordinal);
            var differences = GetDifferences(leftField, rightField);
            result.Add(new DbfFieldComparison(
                differences.Count == 0 ? "Однакове" : "Відрізняється",
                leftField.Ordinal,
                rightField.Ordinal,
                leftField.Name,
                leftField.DbfType,
                rightField.DbfType,
                leftField.Length,
                rightField.Length,
                leftField.DecimalCount,
                rightField.DecimalCount,
                differences.Count == 0 ? "—" : string.Join(", ", differences)));
        }

        result.AddRange(right.Fields
            .Where(field => !matchedRightOrdinals.Contains(field.Ordinal))
            .Select(field => CreateMissing(field, false)));
        return result;
    }

    private static List<string> GetDifferences(DbfFieldInfo left, DbfFieldInfo right)
    {
        var differences = new List<string>();
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal)) differences.Add("регістр назви");
        if (!string.Equals(left.DbfType, right.DbfType, StringComparison.Ordinal)) differences.Add("тип");
        if (left.Length != right.Length) differences.Add("довжина");
        if (left.DecimalCount != right.DecimalCount) differences.Add("десяткові");
        if (left.Ordinal != right.Ordinal) differences.Add("порядок");
        if (left.IsIndexed != right.IsIndexed) differences.Add("ознака індексу");
        if (left.WorkAreaId != right.WorkAreaId) differences.Add("work area");
        return differences;
    }

    private static DbfFieldComparison CreateMissing(DbfFieldInfo field, bool onlyLeft) => new(
        onlyLeft ? "Лише ліворуч" : "Лише праворуч",
        onlyLeft ? field.Ordinal : null,
        onlyLeft ? null : field.Ordinal,
        field.Name,
        onlyLeft ? field.DbfType : "—",
        onlyLeft ? "—" : field.DbfType,
        onlyLeft ? field.Length : null,
        onlyLeft ? null : field.Length,
        onlyLeft ? field.DecimalCount : null,
        onlyLeft ? null : field.DecimalCount,
        onlyLeft ? "Поле відсутнє у правому файлі" : "Поле відсутнє у лівому файлі");

    private static string Describe(DBFField field)
    {
        var values = new List<string>();
        if (field.indexFieldFlag != 0) values.Add("індексоване");
        if (field.workAreaId != 0) values.Add($"work area {field.workAreaId}");
        return values.Count == 0 ? "—" : string.Join(", ", values);
    }
}
