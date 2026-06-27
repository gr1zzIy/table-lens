using TableLens.Core.Dbf;
using TableLens.Core.Models;

namespace TableLens.Core;

public sealed record FolderComparison(string File, string Status, string Details);

public static class SchemaComparison
{
    public static IReadOnlyList<FolderComparison> CompareFolders(string left, string right, DbfFieldCatalog catalog, int codePage, CancellationToken token)
    {
        var leftFiles = AppPaths.Discover([left]).Where(p => Path.GetExtension(p).Equals(".dbf", StringComparison.OrdinalIgnoreCase)).ToList();
        var unmatchedRight = AppPaths.Discover([right]).Where(p => Path.GetExtension(p).Equals(".dbf", StringComparison.OrdinalIgnoreCase)).ToList();
        var result = new List<FolderComparison>();
        var comparer = new DbfStructureComparer();
        var encoding = TableFileService.StrictEncoding(codePage);
        foreach (var file in leftFiles)
        {
            token.ThrowIfCancellationRequested();
            // Prefer exact relative paths, then unique filenames, then unique known masks.
            var relative = Path.GetRelativePath(left, file);
            var candidates = unmatchedRight.Where(r => string.Equals(Path.GetRelativePath(right, r), relative, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length == 0) candidates = unmatchedRight.Where(r => string.Equals(Path.GetFileName(r), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)).ToArray();
            var mask = catalog.FindMask(file);
            if (candidates.Length == 0 && mask is not null) candidates = unmatchedRight.Where(r => catalog.FindMask(r) == mask).ToArray();
            if (candidates.Length != 1)
            {
                result.Add(new(relative, candidates.Length == 0 ? "Лише ліворуч" : "Неоднозначно", candidates.Length == 0 ? "Відповідник не знайдено" : "Кілька можливих відповідників; порівняйте файли окремо"));
                continue;
            }
            var other = candidates[0];
            unmatchedRight.Remove(other);
            try
            {
                var differences = comparer.Compare(comparer.ReadStructure(file, encoding), comparer.ReadStructure(other, encoding)).Where(d => d.Status != "Однакове").ToArray();
                result.Add(new(relative, differences.Length == 0 ? "Однакове" : "Відрізняється", differences.Length == 0 ? "Структури однакові" : string.Join("; ", differences.Select(d => $"{d.FieldName}: {d.Differences}"))));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or FormatException) { result.Add(new(relative, "Помилка", ex.Message)); }
        }
        result.AddRange(unmatchedRight.Select(p => new FolderComparison(Path.GetRelativePath(right, p), "Лише праворуч", "Відповідник не знайдено")));
        return result;
    }

    public static IReadOnlyList<ColumnComparison> Compare(TableDocument left, TableDocument right)
    {
        var l = left.GetColumns();
        var r = right.GetColumns();
        return l.Select(a =>
        {
            var b = r.FirstOrDefault(c => c.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
            var details = b is null ? "Поле відсутнє праворуч" : string.Join(", ", new[] {
                a.Name != b.Name ? "регістр" : null, a.Ordinal != b.Ordinal ? "порядок" : null,
                a.Type != b.Type ? "тип" : null, a.Length != b.Length ? "довжина" : null, a.Decimals != b.Decimals ? "десяткові" : null }.Where(s => s is not null));
            return new ColumnComparison(a.Name, b is null ? "Лише ліворуч" : details.Length == 0 ? "Однакове" : "Відрізняється", a.Type, b?.Type ?? "—", details.Length == 0 ? "—" : details);
        }).Concat(r.Where(b => !l.Any(a => a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase))).Select(b => new ColumnComparison(b.Name, "Лише праворуч", "—", b.Type, "Поле відсутнє ліворуч"))).ToArray();
    }
}

public sealed record ColumnComparison(string Field, string Status, string LeftType, string RightType, string Differences);
