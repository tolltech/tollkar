using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Tollkar.Infrastructure;

namespace Tollkar.App;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = TollkarInfrastructure.CreateServices(
                AppDataPaths.LibraryDatabase);
            desktop.MainWindow = new MainWindow(services.Library, services.PlaybackQueue);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
