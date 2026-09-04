using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TableLens.Core;

namespace TableLens.Desktop.Views;

public sealed partial class MainWindow
{
    private void UpdateProjectLabel()
    {
        ProjectLabel.Text = _project is null ? Lang.T("LooseFiles") : _project.Name + (_projectDirty ? " •" : "");
        ProjectHint.Text = _project is null ? Lang.T("FreeMode") : _projectPath ?? Lang.T("SaveProject");
    }
    private async Task<bool> ConfirmProjectChangesAsync()
    {
        if (_project is null || !_projectDirty) return true;
        var choice = await Dialogs.Choice(this, Lang.T("UnsavedProject"), Lang.T("UnsavedProjectText"), "Save", "Discard", "Cancel");
        return choice == "Discard" || choice == "Save" && await SaveProjectAsync();
    }
    private async Task<bool> SaveProjectAsync()
    {
        if (_project is null)
        {
            var name = await Dialogs.Input(this, Lang.T("NewProject"), "My workspace");
            if (name is null) return false;
            _project = new() { Name = name }; _projectDirty = true;
        }
        if (_projectPath is null)
        {
            var safeName = string.Concat(_project.Name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var path = await PickSaveAsync(Lang.T("SaveProject"), safeName + ".tablelens.json", "json", ProjectFiles);
            if (path is null) { UpdateProjectLabel(); return false; }
            _projectPath = path;
        }
        _project.Files = _entries.Select(e => new WorkspaceFile(e.Path, e.Options.Clone())).ToList();
        _store.SaveProject(_projectPath, _project);
        _projectDirty = false; _settings.LastProjectPath = _projectPath;
        RememberProject(_projectPath); UpdateProjectLabel(); StatusText.Text = Lang.T("Saved") + ": " + _project.Name;
        return true;
    }
    private void RememberProject(string path)
    {
        _settings.RecentProjects.RemoveAll(p => AppPaths.PathComparer.Equals(path, p));
        _settings.RecentProjects.Insert(0, path); _settings.RecentProjects = _settings.RecentProjects.Take(12).ToList();
    }
    private async void NewProjectClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!await ConfirmTableChangesAsync() || !await ConfirmProjectChangesAsync()) return;
        var name = await Dialogs.Input(this, Lang.T("NewProject"));
        if (name is null) return;
        CaptureColumnLayout(); HideDocument(); _entries.Clear(); _project = new() { Name = name }; _projectPath = null; _projectDirty = true;
        _settings.LastProjectPath = null; RefreshFileList(); UpdateProjectLabel();
    });
    private async void OpenProjectClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = Lang.T("OpenProject"), AllowMultiple = false, FileTypeFilter = [ProjectFiles] });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) await LoadProjectAsync(path);
    });
    private async Task LoadProjectAsync(string path)
    {
        var project = _store.LoadProject(path);
        if (!await ConfirmTableChangesAsync() || !await ConfirmProjectChangesAsync()) return;
        CaptureColumnLayout(); _loadCancellation?.Cancel(); _scanCancellation?.Cancel(); HideDocument();
        _selectionGuard = true; _entries.Clear();
        foreach (var file in project.Files) _entries.Add(new(file.Path, file.Options));
        _selectionGuard = false;
        _project = project; _projectPath = path; _projectDirty = false; _settings.LastProjectPath = path;
        RememberProject(path); RefreshFileList(); UpdateProjectLabel(); StartSchemaScan();
        if (_entries.FirstOrDefault(e => File.Exists(e.Path)) is { } first) await SelectEntryAsync(first);
    }
    private async void SaveProjectClick(object? sender, RoutedEventArgs e) => await Run(async () => { await SaveProjectAsync(); });
    private async void CloseProjectClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_project is null || !await ConfirmTableChangesAsync() || !await ConfirmProjectChangesAsync()) return;
        CaptureColumnLayout(); _loadCancellation?.Cancel(); _scanCancellation?.Cancel(); HideDocument(); _entries.Clear();
        _project = null; _projectPath = null; _projectDirty = false; _settings.LastProjectPath = null; RefreshFileList(); UpdateProjectLabel();
    });

    private async void SettingsClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!await ConfirmTableChangesAsync()) return;
        var chosen = await Dialogs.Settings(this, _settings, _currentEntry?.Options ?? _settings.Import);
        if (chosen is null) return;
        _settings.Theme = chosen.Theme; _settings.Language = chosen.Language; _settings.Import = chosen.Import;
        App.ApplyTheme(_settings.Theme); CaptureColumnLayout(); _store.SaveSettings(_settings);
        if (_currentEntry is { } entry) { entry.Options = chosen.Import.Clone(); _projectDirty |= _project is not null; UpdateProjectLabel(); await SelectEntryAsync(entry, true); }
    });

    private async void ClosingWindow(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closingPending || _dialogPending || _writing) { StatusText.Text = Lang.T("Working"); return; }
        _closingPending = true;
        try
        {
            if (!await ConfirmTableChangesAsync() || !await ConfirmProjectChangesAsync()) return;
            CaptureColumnLayout(); _store.SaveSettings(_settings);
            _loadCancellation?.Cancel(); _scanCancellation?.Cancel(); _filterTimer.Stop();
            _allowClose = true; Close();
        }
        catch (Exception ex) { await Dialogs.Error(this, ex); }
        finally { _closingPending = false; }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var command = (e.KeyModifiers & (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) != 0;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        // Text controls keep their native editing shortcuts, including undo and delete.
        if (e.Source is TextBox && !(command && e.Key is Key.O or Key.N or Key.S or Key.E or Key.F)) return;
        if (command)
        {
            switch (e.Key)
            {
                case Key.C: if (!DataGrid.IsKeyboardFocusWithin) return; CopyRowsClick(this, new()); break;
                case Key.O: if (shift) AddFilesClick(this, new()); else OpenFilesClick(this, new()); break;
                case Key.N: NewTableClick(this, new()); break;
                case Key.S: SaveClick(this, new()); break;
                case Key.E: ExportClick(this, new()); break;
                case Key.F: SearchBox.Focus(); SearchBox.SelectAll(); break;
                case Key.Z: if (shift) RedoClick(this, new()); else UndoClick(this, new()); break;
                case Key.Y: RedoClick(this, new()); break;
                default: return;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.F5) { ReloadClick(this, new()); e.Handled = true; }
        else if (e.Key == Key.Delete && e.Source is not TextBox)
        {
            if (FileList.IsKeyboardFocusWithin) RemoveFileClick(this, new());
            else if (DataGrid.IsKeyboardFocusWithin && _editMode) DeleteRowsClick(this, new());
            else return;
            e.Handled = true;
        }
    }
}
