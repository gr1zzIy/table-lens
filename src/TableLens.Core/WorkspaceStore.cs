using System.Text.Json;
using TableLens.Core.Models;

namespace TableLens.Core;

public sealed class AppSettings
{
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = "uk";
    public string? LastProjectPath { get; set; }
    public List<string> RecentFiles { get; set; } = [];
    public List<string> RecentProjects { get; set; } = [];
    public ReadOptions Import { get; set; } = new();
    public Dictionary<string, List<ColumnLayout>> Columns { get; set; } = new(AppPaths.PathComparer);
}

public sealed record ColumnLayout(string Name, double Width, int Order);
public sealed record WorkspaceFile(string Path, ReadOptions Options);
public sealed class WorkspaceProject
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public List<WorkspaceFile> Files { get; set; } = [];
    // Compatibility with saved WinForms projects.
    public List<string>? FilePaths { get; set; }
}

public sealed class WorkspaceStore(string? dataDirectory = null)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string DirectoryPath { get; } = dataDirectory ?? AppPaths.DataDirectory;
    public string? LoadWarning { get; private set; }

    public AppSettings LoadSettings()
    {
        try
        {
            var path = Path.Combine(DirectoryPath, "settings.json");
            var settings = File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new() : new AppSettings();
            settings.Columns = new(settings.Columns ?? [], AppPaths.PathComparer);
            settings.Import ??= new();
            settings.RecentFiles ??= [];
            settings.RecentProjects ??= [];
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LoadWarning = $"Не вдалося прочитати налаштування: {ex.Message}. Використано стандартні.";
            return new();
        }
    }

    public void SaveSettings(AppSettings settings) => WriteJson(Path.Combine(DirectoryPath, "settings.json"), settings);

    public WorkspaceProject LoadProject(string path)
    {
        var project = JsonSerializer.Deserialize<WorkspaceProject>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Проєкт порожній.");
        if (project.Version != 1) throw new InvalidDataException($"Непідтримувана версія проєкту: {project.Version}.");
        project.Files ??= [];
        if (project.Files.Count == 0 && project.FilePaths is not null)
            project.Files = project.FilePaths.Select(p => new WorkspaceFile(p, new ReadOptions())).ToList();
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        project.Files = project.Files.Select(f => new WorkspaceFile(Path.GetFullPath(f.Path, directory), f.Options ?? new())).DistinctBy(f => f.Path, AppPaths.PathComparer).ToList();
        if (string.IsNullOrWhiteSpace(project.Name)) project.Name = Path.GetFileNameWithoutExtension(path);
        return project;
    }

    public void SaveProject(string path, WorkspaceProject project)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var portable = new WorkspaceProject { Name = project.Name,
            Files = project.Files.Select(f => new WorkspaceFile(Path.GetRelativePath(directory, f.Path), f.Options.Clone())).ToList() };
        WriteJson(path, portable);
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json)); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
