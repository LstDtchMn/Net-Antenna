using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.IO;
using System.Linq;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NetAntenna.Core.Data;
using NetAntenna.Core.Services;
using NetAntenna.Desktop.ViewModels;
using NetAntenna.Desktop.Views;

namespace NetAntenna.Desktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            // Configure DI container
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            // Initialize database
            var db = Services.GetRequiredService<IDatabaseService>();
            db.InitializeAsync().GetAwaiter().GetResult();

            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Database — singleton, single connection with WAL mode
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetAntenna", "netantenna.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        services.AddSingleton<IDatabaseService>(new DatabaseService(dbPath));

        // HTTP client for HDHomeRun
        services.AddSingleton<HttpClient>(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(5) });
        services.AddSingleton<ITunerClient, TunerHttpClient>();

        // Device discovery
        services.AddSingleton<IDeviceDiscovery, DeviceDiscoveryService>();

        // Signal logger
        services.AddSingleton<ISignalLogger, SignalLoggerService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ChannelManagerViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}