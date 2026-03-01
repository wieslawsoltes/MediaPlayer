using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MediaPlayer.Controls;
using MediaPlayer.Controls.Workflows;
using MediaPlayer.Demo.ViewModels;
using MediaPlayer.Native.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MediaPlayer.Demo;

public partial class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            _services = serviceCollection.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });

            desktop.Exit += OnDesktopExit;
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var nativeOptions = MediaPlayerNativeOptions.FromEnvironment();
        MediaPlayerNativeRuntime.Configure(nativeOptions);

        services.AddSingleton(nativeOptions.Clone());
        services.AddMediaPlayerWorkflows(options =>
        {
            options.NativeProviderMode = nativeOptions.ProviderMode;
        });
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit -= OnDesktopExit;
        }

        _services?.Dispose();
        _services = null;
    }
}
