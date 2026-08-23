using System.Collections.ObjectModel;
using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TableLens.Core;
using TableLens.Core.Dbf;
using TableLens.Core.Editing;
using TableLens.Core.Models;

namespace TableLens.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private readonly WorkspaceStore _store;
    private readonly AppSettings _settings;
    private readonly string[] _arguments;
    private readonly TableFileService _files = new();
    private readonly EditHistoryManager _history = new();
    private readonly ObservableCollection<FileEntry> _entries = [];
    private readonly Dictionary<string, string> _columnFilters = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DbfFieldCatalog? _catalog;
    private TableDocument? _document;
    private DataView? _view;
    private FileEntry? _currentEntry;
    private WorkspaceProject? _project;
    private string? _projectPath;
    private bool _projectDirty;
    private bool _editMode;
    private bool _selectionGuard;
    private bool _dialogPending;
    private bool _writing;
    private bool _allowClose;
    private bool _closingPending;
    private bool _started;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _scanCancellation;

    public MainWindow() : this(new WorkspaceStore(), new AppSettings(), []) { }
    public MainWindow(WorkspaceStore store, AppSettings settings, string[] arguments)
    {
        _store = store; _settings = settings; _arguments = arguments;
        InitializeComponent();
        try { _catalog = new DbfFieldCatalog(store.DirectoryPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException) { StatusText.Text = Lang.T("CatalogUnavailable") + " " + ex.Message; }
        FileList.ItemsSource = _entries;
        RecentList.ItemsSource = settings.RecentFiles.Where(File.Exists).Take(4).ToArray();
        RecentLabel.IsVisible = RecentList.ItemCount > 0;
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); ApplyFilters(); };
        _history.Changed += (_, _) => UpdateEditingState();
        DataGrid.PreparingCellForEdit += GridPreparingCell;
        DataGrid.CellEditEnded += GridCellEditEnded;
        Opened += async (_, _) => await Run(StartAsync);
        Closing += ClosingWindow;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    public TableDocument? ActiveDocument => _document;

    private async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        if (_arguments.Length > 0)
        {
            if (_arguments.Length == 1 && _arguments[0].EndsWith(".tablelens.json", StringComparison.OrdinalIgnoreCase)) await LoadProjectAsync(_arguments[0]);
            else await AddPathsAsync(_arguments, false);
        }
        else if (_settings.LastProjectPath is { } project && File.Exists(project)) await LoadProjectAsync(project);
        if (_store.LoadWarning is { } warning) StatusText.Text = warning;
    }

    private async Task Run(Func<Task> action)
    {
        if (_dialogPending || _writing) return;
        _dialogPending = true;
        try { await action(); }
        catch (OperationCanceledException) { StatusText.Text = Lang.T("Ready"); }
        catch (Exception ex) { await Dialogs.Error(this, ex); StatusText.Text = Lang.T("Error"); }
        finally { _dialogPending = false; }
    }

    private void UpdateEditingState()
    {
        var dirty = _history.HasChanges || _document?.IsNew == true || DataGrid.SelectedItem is GridRow { HasPendingChanges: true };
        if (_document is not null) DocumentTitle.Text = System.IO.Path.GetFileName(_document.FilePath) + (dirty ? "  •" : "");
        Title = "TableLens" + (_document is null ? "" : " — " + System.IO.Path.GetFileName(_document.FilePath)) + (dirty ? " *" : "");
        SaveButton.IsEnabled = dirty && !_writing;
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        EditToolbar.IsVisible = _editMode;
        DataGrid.IsReadOnly = !_editMode;
        ModeLabel.Text = Lang.T(_editMode ? "Editing" : "ReadOnly");
        EditModeButton.Content = Lang.T(_editMode ? "DisableEdit" : "EnableEdit");
        EditModeButton.IsEnabled = _document?.CanEdit == true;
        SchemaEditButton.IsEnabled = _editMode && _document?.Dbf is not null;
        CatalogButton.IsEnabled = _document?.Dbf is not null && _catalog is not null;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (_document is null) return;
        var count = _view?.Count ?? _document.Table.Rows.Count;
        TableSummary.Text = $"{count:N0} / {_document.Table.Rows.Count:N0}   ·   {_document.Table.Columns.Count} {Lang.T("FieldsCount").ToLowerInvariant()}   ·   {_document.FormatLabel}";
        StatusMeta.Text = $"{Lang.T("Rows")}: {count:N0}   ·   {Lang.T("FilterCount")}: {_columnFilters.Count(f => !string.IsNullOrWhiteSpace(f.Value))}";
    }

    private void HideDocument()
    {
        _document = null; _view = null; _currentEntry = null; _editMode = false;
        DataGrid.ItemsSource = null; DataGrid.Columns.Clear(); _columnFilters.Clear(); _history.Clear();
        DocumentPanel.IsVisible = false; WelcomePanel.IsVisible = true; BusyOverlay.IsVisible = false;
        Title = "TableLens"; StatusMeta.Text = "TableLens 1.0";
    }

    private void SetBusy(bool busy, string key = "Loading") { BusyOverlay.IsVisible = busy; BusyText.Text = Lang.T(key); CancelOperationButton.IsVisible = key == "Loading"; }
    private void CancelClick(object? sender, RoutedEventArgs e) { _loadCancellation?.Cancel(); _scanCancellation?.Cancel(); }
    private void ExitClick(object? sender, RoutedEventArgs e) => Close();
    private async void AboutClick(object? sender, RoutedEventArgs e) => await Run(() => Dialogs.About(this));
    private async void FilterHelpClick(object? sender, RoutedEventArgs e) => await Run(() => Dialogs.Message(this, Lang.T("Condition"), Lang.T("FilterHint") + "\n\ntext → contains\n==text → equals\n!text → does not contain\n!= → non-empty\n==null → empty\n>10 / >=10 / <10 / <=10\n==A or ==B\n\nCSV: numeric comparisons treat numeric-looking cells as numbers. Leading zeros stay in the source.\nCtrl+C: copy selected rows. Double-click a cell to edit; nested JSON uses Edit row."));
    private async void ShortcutsClick(object? sender, RoutedEventArgs e) => await Run(() => Dialogs.Message(this, Lang.T("Shortcuts"), "Ctrl / ⌘ + O — " + Lang.T("Open") + "\nCtrl / ⌘ + Shift + O — " + Lang.T("Add") + "\nCtrl / ⌘ + N — " + Lang.T("New") + "\nCtrl / ⌘ + S — " + Lang.T("Save") + "\nCtrl / ⌘ + E — " + Lang.T("Export") + "\nCtrl / ⌘ + F — " + Lang.T("Search") + "\nCtrl / ⌘ + Z / Y — Undo / Redo\nF5 — " + Lang.T("Reload") + "\nDelete — " + Lang.T("Remove") + " / " + Lang.T("DeleteRows")));
}
