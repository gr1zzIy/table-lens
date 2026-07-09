namespace TableLens.Core;

public static class AppPaths
{
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TableLens");
    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".dbf" or ".csv" or ".tsv" or ".json";

    public static IEnumerable<string> Discover(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(PathComparer);
        foreach (var path in paths)
        {
            var files = Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                : [path];
            foreach (var file in files)
                if (IsSupported(file) && seen.Add(Path.GetFullPath(file))) yield return Path.GetFullPath(file);
        }
    }
}
