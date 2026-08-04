using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using TableLens.Core;
using TableLens.Desktop.Views;

namespace TableLens.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new WorkspaceStore();
            var settings = store.LoadSettings();
            Lang.Language = settings.Language;
            ApplyTheme(settings.Theme);
            desktop.MainWindow = new MainWindow(store, settings, desktop.Args ?? []);
        }
        base.OnFrameworkInitializationCompleted();
    }
    public static void ApplyTheme(string theme)
    {
        if (Current is not null) Current.RequestedThemeVariant = theme switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
    }
}
