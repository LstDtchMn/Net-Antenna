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
using Microsoft.Extensions.Logging;
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
        var memoryLogger = new MemoryLogProvider();
        services.AddSingleton<IMemoryLogSink>(memoryLogger);

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddProvider(memoryLogger);
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Phase 2 Core Services
        services.AddHttpClient<IFccDataService, FccDataService>();
        services.AddHttpClient<INwsWeatherService, NwsWeatherService>();
        services.AddSingleton<IRfPredictionEngine, RfPredictionEngine>();
        services.AddSingleton<IDatabaseService>(sp =>
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(folder, "NetAntenna", "netantenna.db");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new DatabaseService(path);
        });
        services.AddSingleton<IDeviceDiscovery, DeviceDiscoveryService>();
        services.AddTransient<ITunerClient, TunerHttpClient>();
        services.AddSingleton<ISignalLogger, SignalLoggerService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ChannelManagerViewModel>();
        services.AddTransient<TowerMapViewModel>();
        services.AddTransient<SpectrumOverviewViewModel>();
        services.AddTransient<AimingAssistantViewModel>();
        services.AddTransient<LogsViewModel>();
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