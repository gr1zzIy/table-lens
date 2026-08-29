using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TableLens.Core;
using TableLens.Core.Models;

namespace TableLens.Desktop.Views;

public sealed partial class MainWindow
{
    private static readonly FilePickerFileType SupportedFiles = new("DBF / CSV / TSV / JSON") { Patterns = ["*.dbf", "*.csv", "*.tsv", "*.json", "*.DBF", "*.CSV", "*.TSV", "*.JSON"] };
    private static readonly FilePickerFileType ProjectFiles = new("TableLens project") { Patterns = ["*.tablelens.json", "*.json"] };

    private async Task<string[]> PickFilesAsync(string title, bool multiple = true)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = title, AllowMultiple = multiple, FileTypeFilter = [SupportedFiles, FilePickerFileTypes.All] });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
    }
    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = title, AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task<string?> PickSaveAsync(string title, string name, string extension, FilePickerFileType? type = null)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = title, SuggestedFileName = name, DefaultExtension = extension,
            FileTypeChoices = [type ?? new(extension.ToUpperInvariant()) { Patterns = ["*." + extension] }], ShowOverwritePrompt = true });
        return file?.TryGetLocalPath();
    }

    private async void OpenFilesClick(object? sender, RoutedEventArgs e) => await Run(async () => { var paths = await PickFilesAsync(Lang.T("Open")); if (paths.Length > 0) await AddPathsAsync(paths, true); });
    private async void AddFilesClick(object? sender, RoutedEventArgs e) => await Run(async () => { var paths = await PickFilesAsync(Lang.T("Add")); if (paths.Length > 0) await AddPathsAsync(paths, false); });
    private async void OpenFolderClick(object? sender, RoutedEventArgs e) => await Run(async () => { var path = await PickFolderAsync(Lang.T("Folder")); if (path is not null) await AddPathsAsync([path], true); });
    private async void SamplesClick(object? sender, RoutedEventArgs e) => await Run(() => AddPathsAsync([System.IO.Path.Combine(AppContext.BaseDirectory, "samples")], false));
    private async void RecentFileClick(object? sender, RoutedEventArgs e) => await Run(async () => { if (sender is Button { Content: string path }) await AddPathsAsync([path], false); });

    // Public to support a meaningful headless smoke test through the real window.
    public async Task AddPathsAsync(IEnumerable<string> paths, bool replace)
    {
        var discovered = await Task.Run(() => AppPaths.Discover(paths).OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray());
        if (discovered.Length == 0) { StatusText.Text = "DBF / CSV / TSV / JSON: " + Lang.T("FilesEmpty"); return; }
        if (replace && !await ConfirmTableChangesAsync()) return;
        if (replace)
        {
            CaptureColumnLayout(); _loadCancellation?.Cancel(); _scanCancellation?.Cancel(); HideDocument();
            _selectionGuard = true; _entries.Clear(); FileList.SelectedItem = null; _selectionGuard = false;
        }
        foreach (var path in discovered)
            if (!_entries.Any(f => AppPaths.PathComparer.Equals(f.Path, path))) _entries.Add(new(path, _settings.Import.Clone()));
        if (_project is not null) _projectDirty = true;
        UpdateProjectLabel(); RefreshFileList(); StartSchemaScan();
        if (_document is null)
        {
            var first = _entries.FirstOrDefault();
            if (first is not null) { SetSelection(first); await SelectEntryAsync(first); }
        }
        StatusText.Text = $"{_entries.Count} files · {Lang.T("Ready")}";
    }

    private void SetSelection(FileEntry? entry) { _selectionGuard = true; FileList.SelectedItem = entry; _selectionGuard = false; }
    private async void FileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_selectionGuard || FileList.SelectedItem is not FileEntry entry) return;
        if (_dialogPending || _writing) { SetSelection(_currentEntry); return; }
        await Run(() => SelectEntryAsync(entry));
    }

    private async Task SelectEntryAsync(FileEntry entry, bool force = false)
    {
        if (!force && ReferenceEquals(entry, _currentEntry) && (_document is not null || _loadCancellation is not null)) return;
        if (!await ConfirmTableChangesAsync()) { SetSelection(_currentEntry); return; }
        CaptureColumnLayout();
        _loadCancellation?.Cancel();
        var cancellation = _loadCancellation = new();
        _currentEntry = entry;
        SetSelection(entry); SetBusy(true); StatusText.Text = Lang.T("Loading");
        try
        {
            var document = await Task.Run(() => _files.Read(entry.Path, entry.Options, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(_loadCancellation, cancellation)) return;
            BindDocument(document);
            entry.Options = document.Options.Clone();
            _settings.RecentFiles.RemoveAll(p => AppPaths.PathComparer.Equals(p, entry.Path));
            _settings.RecentFiles.Insert(0, entry.Path); _settings.RecentFiles = _settings.RecentFiles.Take(12).ToList();
            var validation = document.Dbf is null ? null : _catalog?.Validate(entry.Path, document.Dbf.Fields);
            entry.SetStatus($"{document.Table.Rows.Count:N0} {Lang.T("Rows").ToLowerInvariant()} · {document.FormatLabel}", hasIssue: validation is { IsValid: false } || document.Warnings.Count > 0);
            StatusText.Text = Lang.T("Ready");
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _currentEntry = _document is null ? null : _entries.FirstOrDefault(f => AppPaths.PathComparer.Equals(f.Path, _document.FilePath));
                SetSelection(_currentEntry); StatusText.Text = Lang.T("Ready");
            }
        }
        catch (Exception ex)
        {
            entry.SetStatus(File.Exists(entry.Path) ? Lang.T("Error") : Lang.T("Missing"), ex.Message);
            await Dialogs.Error(this, ex);
            SetSelection(_document is null ? null : _entries.FirstOrDefault(f => AppPaths.PathComparer.Equals(f.Path, _document.FilePath)));
            _currentEntry = FileList.SelectedItem as FileEntry;
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation)) { _loadCancellation = null; SetBusy(false); }
            cancellation.Dispose();
        }
    }

    private async void ReloadClick(object? sender, RoutedEventArgs e) => await Run(async () => { if (_currentEntry is { } entry) await SelectEntryAsync(entry, true); StartSchemaScan(); });
    private async void RemoveFileClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (FileList.SelectedItem is not FileEntry entry || !await ConfirmTableChangesAsync()) return;
        CaptureColumnLayout(); HideDocument(); _selectionGuard = true; _entries.Remove(entry); FileList.SelectedItem = null; _selectionGuard = false;
        _projectDirty |= _project is not null; RefreshFileList(); UpdateProjectLabel();
        if (_entries.FirstOrDefault() is { } next) await SelectEntryAsync(next);
    });
    private async void ClearFilesClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!await ConfirmTableChangesAsync()) return;
        CaptureColumnLayout(); _loadCancellation?.Cancel(); _scanCancellation?.Cancel(); HideDocument();
        _selectionGuard = true; _entries.Clear(); FileList.SelectedItem = null; _selectionGuard = false;
        _projectDirty |= _project is not null; RefreshFileList(); UpdateProjectLabel();
    });
    private void FileSearchChanged(object? sender, TextChangedEventArgs e) { if (FileList is not null) RefreshFileList(); }
    private void RefreshFileList()
    {
        var selected = _currentEntry;
        _selectionGuard = true;
        var query = FileSearch.Text?.Trim() ?? "";
        FileList.ItemsSource = query.Length == 0 ? _entries : _entries.Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        FileList.SelectedItem = selected;
        _selectionGuard = false;
    }

    private async void StartSchemaScan()
    {
        _scanCancellation?.Cancel();
        var cancellation = _scanCancellation = new();
        var entries = _entries.ToArray();
        try
        {
            foreach (var entry in entries)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!File.Exists(entry.Path)) { entry.SetStatus(Lang.T("Missing"), Lang.T("Missing")); continue; }
                if (System.IO.Path.GetExtension(entry.Path).Equals(".dbf", StringComparison.OrdinalIgnoreCase) && _catalog is not null)
                {
                    try
                    {
                        var structure = await Task.Run(() => new TableLens.Core.Dbf.DbfStructureComparer().ReadStructure(entry.Path, TableFileService.StrictEncoding(entry.Options.DbfCodePage)), cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        var result = _catalog.Validate(entry.Path, structure.Fields);
                        entry.SetStatus(result is null ? "DBF · " + Lang.T("NoReference") : result.IsValid ? "DBF · schema OK" : $"DBF · {result.IssueCount} schema issues", hasIssue: result is null or { IsValid: false });
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { entry.SetStatus(Lang.T("Error"), ex.Message); }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_scanCancellation, cancellation)) _scanCancellation = null; cancellation.Dispose(); }
    }

    private void DragOver(object? sender, DragEventArgs e) { e.DragEffects = e.DataTransfer.TryGetFiles() is not null ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void Drop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        e.Handled = true; if (paths.Length > 0) await Run(() => AddPathsAsync(paths, false));
    }
}
