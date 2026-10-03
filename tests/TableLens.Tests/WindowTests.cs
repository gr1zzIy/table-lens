using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TableLens.Core;
using TableLens.Desktop;
using TableLens.Desktop.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TableLens.Tests.HeadlessApp))]

namespace TableLens.Tests;

public static class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .WithInterFont()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class WindowTests
{
    [AvaloniaFact]
    public async Task RealWindowDisplaysCsvFiltersAndExportsItsView()
    {
        using var temp = new TestDirectory();

        Lang.Language = "en";
        App.ApplyTheme("Light");

        var path = temp.File(
            "customers.csv",
            "CODE,NAME,AMOUNT\n0001,Olena,100\n0002,Andrii,200\n");

        var window = new MainWindow(
            new WorkspaceStore(System.IO.Path.Combine(temp.Path, "settings")),
            new AppSettings(),
            []);

        window.Show();

        await window.AddPathsAsync([path], false)
            .WaitAsync(TimeSpan.FromSeconds(15));

        Assert.NotNull(window.ActiveDocument);

        var grid = window.FindControl<DataGrid>("DataGrid")!;

        Assert.Equal(3, grid.Columns.Count);

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(
            grid.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "0001");

        var search = window.FindControl<TextBox>("SearchBox")!;

        search.Text = "Andrii";
        await Task.Delay(350);
        Dispatcher.UIThread.RunJobs();

        Assert.Single((GridRow[])grid.ItemsSource!);

        search.Text = "";
        await Task.Delay(350);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, ((GridRow[])grid.ItemsSource!).Length);

        window.Close();
    }

    [AvaloniaFact]
    public async Task InlineEditAndUndoWorkThroughTheActualGrid()
    {
        using var temp = new TestDirectory();

        var path = temp.File(
            "editable.csv",
            "ID,NAME\n001,Olena\n");

        var window = new MainWindow(
            new WorkspaceStore(System.IO.Path.Combine(temp.Path, "settings")),
            new AppSettings(),
            [])
        {
            Width = 1200,
            Height = 800
        };

        window.Show();

        await window.AddPathsAsync([path], false)
            .WaitAsync(TimeSpan.FromSeconds(15));

        FlushUi(window);

        var grid = window.FindControl<DataGrid>("DataGrid")!;

        window.FindControl<Button>("EditModeButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        FlushUi(window);

        // Commit an actual DataGrid edit.
        SelectEditableCell(window, grid);

        var editor = BeginEditAndGetEditor(window, grid);

        SetEditorText(editor, "Updated");
        FlushUi(window);

        CommitGridEdit(window, grid);

        Assert.Equal("Updated", window.ActiveDocument!.Table.Rows[0][1]);
        Assert.True(window.FindControl<Button>("SaveButton")!.IsEnabled);

        // Undo the committed edit.
        window.FindControl<Button>("UndoButton")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        FlushUi(window);

        Assert.Equal("Olena", window.ActiveDocument.Table.Rows[0][1]);

        // Start another edit and cancel it.
        SelectEditableCell(window, grid);

        editor = BeginEditAndGetEditor(window, grid);

        SetEditorText(editor, "Discard me");
        FlushUi(window);

        Assert.Equal("Discard me", editor.Text);

        CancelGridEdit(window, grid);

        Assert.Equal("Olena", window.ActiveDocument.Table.Rows[0][1]);
        Assert.False(window.FindControl<Button>("SaveButton")!.IsEnabled);

        window.Close();
    }

    [AvaloniaFact]
    public async Task NestedJsonSearchAndSchemaSwitchUseTheActualWindow()
    {
        using var temp = new TestDirectory();

        var path = temp.File(
            "nested.json",
            """[{"id":1,"owner":{"name":"Olena"}},{"id":2,"owner":{"name":"Andrii"}}]""");

        var window = new MainWindow(
            new WorkspaceStore(System.IO.Path.Combine(temp.Path, "settings")),
            new AppSettings(),
            []);

        window.Show();

        await window.AddPathsAsync([path], false)
            .WaitAsync(TimeSpan.FromSeconds(15));

        window.FindControl<TextBox>("SearchBox")!.Text = "Olena";

        await Task.Delay(350);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(
            (GridRow[])window.FindControl<DataGrid>("DataGrid")!.ItemsSource!);

        window.FindControl<TabControl>("TableTabs")!.SelectedIndex = 1;
        window.UpdateLayout();

        Assert.Equal(
            2,
            window.FindControl<DataGrid>("SchemaGrid")!
                .ItemsSource
                .Cast<object>()
                .Count());

        window.Close();
    }

    [AvaloniaFact]
    public async Task CaptureRealLightAndDarkScreenshotsWhenRequested()
    {
        var output = Environment.GetEnvironmentVariable("TABLELENS_SCREENSHOT_DIR");

        if (string.IsNullOrEmpty(output))
            return;

        Directory.CreateDirectory(output);

        using var temp = new TestDirectory();

        Lang.Language = "en";
        App.ApplyTheme("Light");

        var window = new MainWindow(
            new WorkspaceStore(System.IO.Path.Combine(temp.Path, "settings")),
            new AppSettings(),
            []);

        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Capture(
            window,
            System.IO.Path.Combine(output, "welcome.png"));

        await window.AddPathsAsync(
                [System.IO.Path.Combine(AppContext.BaseDirectory, "samples", "customers.csv")],
                false)
            .WaitAsync(TimeSpan.FromSeconds(15));

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Capture(
            window,
            System.IO.Path.Combine(output, "tablelens-light.png"));

        App.ApplyTheme("Dark");

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Capture(
            window,
            System.IO.Path.Combine(output, "tablelens-dark.png"));

        Lang.Language = "uk";

        window.Close();
    }

    private static void Capture(MainWindow window, string path)
    {
        using var image = window.CaptureRenderedFrame();

        Assert.NotNull(image);

        image.Save(
            path,
            Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void SelectEditableCell(MainWindow window, DataGrid grid)
    {
        grid.SelectedIndex = 0;
        grid.CurrentColumn = grid.Columns[1];
        grid.Focus();

        FlushUi(window);

        Assert.NotNull(grid.SelectedItem);
        Assert.NotNull(grid.CurrentColumn);
    }

    private static TextBox BeginEditAndGetEditor(MainWindow window, DataGrid grid)
    {
        TextBox? editor = null;

        void OnPreparingCellForEdit(
            object? sender,
            DataGridPreparingCellForEditEventArgs e)
        {
            if (e.Column == grid.CurrentColumn &&
                e.EditingElement is TextBox textBox)
            {
                editor = textBox;
            }
        }

        grid.PreparingCellForEdit += OnPreparingCellForEdit;

        try
        {
            var item = grid.SelectedItem
                ?? throw new InvalidOperationException(
                    "DataGrid has no selected item.");

            var column = grid.CurrentColumn
                ?? throw new InvalidOperationException(
                    "DataGrid has no current column.");

            grid.ScrollIntoView(item, column);
            FlushUi(window);

            Assert.True(grid.BeginEdit());

            FlushUi(window);

            if (editor != null)
                return editor;

            if (column.GetCellContent(item) is TextBox textBox)
                return textBox;

            throw new InvalidOperationException(
                "DataGrid editing element was not created after BeginEdit().");
        }
        finally
        {
            grid.PreparingCellForEdit -= OnPreparingCellForEdit;
        }
    }

    private static void SetEditorText(TextBox editor, string text)
    {
        var binding = BindingOperations.GetBindingExpressionBase(
            editor,
            TextBox.TextProperty)
            ?? throw new InvalidOperationException(
                "TextBox.Text has no binding.");

        editor.SetCurrentValue(TextBox.TextProperty, text);
        binding.UpdateSource();
    }

    private static void CommitGridEdit(MainWindow window, DataGrid grid)
    {
        Assert.True(
            grid.CommitEdit(DataGridEditingUnit.Cell, true),
            "DataGrid failed to commit the edited cell.");

        FlushUi(window);

        Assert.True(
            grid.CommitEdit(DataGridEditingUnit.Row, true),
            "DataGrid failed to commit the edited row.");

        FlushUi(window);
    }

    private static void CancelGridEdit(MainWindow window, DataGrid grid)
    {
        grid.CancelEdit(DataGridEditingUnit.Cell);
        FlushUi(window);

        grid.CancelEdit(DataGridEditingUnit.Row);
        FlushUi(window);
    }

    private static void FlushUi(MainWindow window)
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
