using System.IO;
using System.Windows;
using System.Windows.Threading;
using DifFuse.App.Services;
using DifFuse.App.ViewModels;
using DifFuse.App.Views;
using DifFuse.Core.Device;
using DifFuse.Core.Fake;
using DifFuse.Core.Library;
using DifFuse.Core.Transport;
using DifFuse.Hid;

namespace DifFuse.App;

/// <summary>Command-line options. <c>--simulate</c> uses the built-in fake amp,
/// <c>--trace</c> logs every USB packet, <c>--snapshot DIR</c> renders screenshots and exits.</summary>
public sealed record StartupOptions(bool Simulate, bool Trace, string? SnapshotDir, string? LibraryRoot)
{
    public static StartupOptions Parse(string[] args)
    {
        bool simulate = false, trace = Environment.GetEnvironmentVariable("DIFFUSE_TRACE") == "1";
        string? snapshot = null, library = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--simulate":
                case "--fake":
                    simulate = true;
                    break;
                case "--trace":
                    trace = true;
                    break;
                case "--snapshot" when i + 1 < args.Length:
                    snapshot = args[++i];
                    simulate = true;
                    break;
                case "--library" when i + 1 < args.Length:
                    library = args[++i];
                    break;
            }
        }
        return new StartupOptions(simulate, trace, snapshot, library);
    }
}

public partial class App : Application
{
    private MainViewModel? _viewModel;
    private PacketLog? _log;

    public static string LogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DifFuse", "logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var options = StartupOptions.Parse(e.Args);
        if (options.SnapshotDir is not null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = Snapshotter.RunAsync(this, options.SnapshotDir);
            return;
        }

        var settings = AppSettings.Load(AppSettings.DefaultPath);
        if (options.Trace)
        {
            _log = new PacketLog(filePath: Path.Combine(LogFolder, $"protocol-{DateTime.Now:yyyyMMdd-HHmmss}.log"));
        }

        IDeviceLocator locator = options.Simulate
            ? new FakeDeviceLocator { ReplyDelay = TimeSpan.FromMilliseconds(1) }
            : new WindowsHidLocator();
        var connection = new MustangConnection(locator, new ConnectionOptions(), _log);
        var library = new PresetLibrary(options.LibraryRoot ?? PresetLibrary.DefaultRoot);
        var dialogs = new DialogService();

        _viewModel = new MainViewModel(connection, library, dialogs, settings, options.Simulate);
        var window = new MainWindow(_viewModel, settings);
        dialogs.Owner = window;
        MainWindow = window;
        window.Show();
        _viewModel.Start();
        if (_log?.FilePath is { } logPath) _viewModel.StatusText = $"Logging USB traffic to {logPath}";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _log?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            File.AppendAllText(Path.Combine(LogFolder, "errors.log"), $"{DateTime.Now:O} {e.Exception}\r\n\r\n");
        }
        catch (IOException)
        {
        }

        if (_viewModel is not null)
        {
            _viewModel.StatusText = $"Something went wrong: {e.Exception.Message}";
            e.Handled = true;
        }
    }
}
