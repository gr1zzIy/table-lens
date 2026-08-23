using System.ComponentModel;
using Avalonia.Media;
using TableLens.Core.Models;

namespace TableLens.Desktop.Views;

public sealed class FileEntry(string path, ReadOptions options) : INotifyPropertyChanged
{
    private string _detail = System.IO.Path.GetDirectoryName(path) ?? "";
    private string? _problem;
    private bool _hasIssue;
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Extension => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    public ReadOptions Options { get; set; } = options;
    public string Detail => _detail;
    public string Tooltip => Path + (_problem is null ? "" : "\n" + _problem);
    public IBrush BadgeBackground => Extension == "DBF" ? Brush.Parse("#E0EBFF") : Extension == "JSON" ? Brush.Parse("#EEE4FF") : Brush.Parse("#DDF3EB");
    public IBrush BadgeForeground => Extension == "DBF" ? Brush.Parse("#315CBD") : Extension == "JSON" ? Brush.Parse("#7750AA") : Brush.Parse("#247A5D");
    public IBrush StatusBrush => _problem is not null ? Brush.Parse("#CA6065") : _hasIssue ? Brush.Parse("#C18B35") : Brush.Parse("#8190A6");
    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetStatus(string detail, string? problem = null, bool hasIssue = false)
    {
        _detail = detail; _problem = problem; _hasIssue = hasIssue;
        foreach (var name in new[] { nameof(Detail), nameof(Tooltip), nameof(StatusBrush) }) PropertyChanged?.Invoke(this, new(name));
    }
}
