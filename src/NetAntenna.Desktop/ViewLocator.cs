using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using NetAntenna.Desktop.ViewModels;
using NetAntenna.Desktop.Views;

namespace NetAntenna.Desktop;

[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> _map = new()
    {
        { typeof(DashboardViewModel),        () => new DashboardView() },
        { typeof(ChannelManagerViewModel),   () => new ChannelManagerView() },
        { typeof(TowerMapViewModel),         () => new TowerMapView() },
        { typeof(SpectrumOverviewViewModel), () => new SpectrumOverviewView() },
        { typeof(SweeperViewModel),          () => new SweeperView() },
        { typeof(AimingAssistantViewModel),  () => new AimingAssistantView() },
        { typeof(LogsViewModel),             () => new LogsView() },
        { typeof(SettingsViewModel),         () => new SettingsView() },
    };

    public Control? Build(object? param)
    {
        if (param is null) return null;

        if (_map.TryGetValue(param.GetType(), out var factory))
            return factory();

        return new TextBlock { Text = "Not Found: " + param.GetType().FullName };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}

