using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Tollkar.Infrastructure;

namespace Tollkar.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var library = TollkarInfrastructure.CreateLibraryService(
                AppDataPaths.LibraryDatabase);
            desktop.MainWindow = new MainWindow(library);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
