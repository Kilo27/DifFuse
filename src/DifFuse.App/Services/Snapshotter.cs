using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DifFuse.App.ViewModels;
using DifFuse.App.Views;
using DifFuse.Core.Device;
using DifFuse.Core.Fake;
using DifFuse.Core.Library;
using DifFuse.Core.Models;

namespace DifFuse.App.Services;

/// <summary>
/// Renders the app, connected to the simulated amp, into PNG files at several window sizes.
/// Run with <c>DifFuse.exe --snapshot DIR</c>. Nothing touches the real library or settings.
/// </summary>
internal static class Snapshotter
{
    private sealed class ScratchRecycler : IFileRecycler
    {
        public void Recycle(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else File.Delete(path);
        }
    }

    private sealed class SilentDialogs : IDialogService
    {
        public bool Confirm(string title, string message, string confirmText, bool destructive = false) => false;
        public string? Prompt(string title, string label, string initial, int maxLength = 0) => null;
        public void ShowMessage(string title, string message) { }
        public SaveToAmpChoice? AskSaveToAmp(IReadOnlyList<PresetSlotViewModel> slots, int suggestedSlot, string suggestedName) => null;
        public IReadOnlyList<string>? PickFilesToImport() => null;
        public string? PickExportPath(string suggestedName) => null;
        public void OpenFolder(string path) { }
    }

    public static async Task RunAsync(Application app, string outputDir)
    {
        var log = new List<string>();
        try
        {
            Directory.CreateDirectory(outputDir);
            var libraryRoot = Path.Combine(Path.GetTempPath(), "DifFuseSnapshot", Guid.NewGuid().ToString("N"));
            var library = new PresetLibrary(libraryRoot, new ScratchRecycler());
            var live = library.CreateBank("Live Set");
            foreach (var preset in FakeMustangAmp.DemoPresets(24).Skip(2).Take(9)) library.Save(live, preset);
            library.CreateBank("Imported from FUSE");

            var locator = new FakeDeviceLocator { ReplyDelay = TimeSpan.FromMilliseconds(1) };
            var connection = new MustangConnection(locator, new ConnectionOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(100),
                Client = new ClientOptions { ReplyIdleTimeout = TimeSpan.FromMilliseconds(80) },
            });
            var settings = new AppSettings { LastBank = "Live Set" };
            var vm = new MainViewModel(connection, library, new SilentDialogs(), settings, isSimulated: true);
            var window = new MainWindow(vm, settings, useBackdrop: false, persistPlacement: false)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                ShowActivated = false,
                ShowInTaskbar = false,
            };
            app.MainWindow = window;
            window.Show();
            vm.Start();

            await WaitUntil(() => vm.IsConnected, TimeSpan.FromSeconds(10));
            log.Add($"connected={vm.IsConnected} slots={vm.Slots.Count}");

            // A preset with all four units in use, one bypassed, one moved in front of the amp.
            await vm.LoadSlotAsync(vm.Slots[6]);
            vm.Delay.SelectedModel = vm.Delay.Models.First(m => m.Id == 0x2b); // six-knob Tape Delay
            vm.Delay.IsOn = false;
            vm.Modulation.IsPostAmp = false;
            vm.Amp.NoiseGate = NoiseGates.Custom;
            await Idle();

            foreach (var (w, h) in new[] { (980.0, 600.0), (1320.0, 860.0), (1600.0, 1000.0) })
            {
                window.Width = w;
                window.Height = h;
                await Idle();
                Save(Render((FrameworkElement)window.Content), Path.Combine(outputDir, $"main-{w:0}x{h:0}.png"));
            }

            // Whole rack without scrolling, at the minimum width.
            window.Width = 980;
            window.Height = 600;
            await Idle();
            var rack = (FrameworkElement)window.FindName("RackPanel");
            Save(RenderDetached(rack, rack.ActualWidth), Path.Combine(outputDir, "rack-full-980.png"));

            // The other control kinds: stops (Type, Mode), stepped knobs (Pitch, Key, Scale), a gate that is not Custom.
            vm.Stomp.SelectedModel = vm.Stomp.Models.First(m => m.Id == 0x88); // Simple Comp
            vm.Modulation.SelectedModel = vm.Modulation.Models.First(m => m.Id == 0x101f); // Diatonic Pitch
            vm.Delay.SelectedModel = vm.Delay.Models.First(m => m.Id == 0x44); // Multitap Delay
            vm.Delay.IsOn = true;
            vm.Amp.NoiseGate = 2;
            window.Width = 1320;
            window.Height = 860;
            await Idle();
            Save(Render((FrameworkElement)window.Content), Path.Combine(outputDir, "controls-1320x860.png"));

            // Library tab.
            var tabs = (TabControl)window.FindName("SidebarTabs");
            tabs.SelectedIndex = 1;
            vm.Library.SelectedPreset = vm.Library.Presets.Skip(2).FirstOrDefault();
            window.Width = 1320;
            window.Height = 860;
            await Idle();
            Save(Render((FrameworkElement)window.Content), Path.Combine(outputDir, "library-1320x860.png"));

            // Disconnected state.
            locator.Unplug();
            await WaitUntil(() => !vm.IsConnected, TimeSpan.FromSeconds(5));
            tabs.SelectedIndex = 0;
            await Idle();
            Save(Render((FrameworkElement)window.Content), Path.Combine(outputDir, "disconnected-1320x860.png"));
            locator.Plug();
            await WaitUntil(() => vm.IsConnected, TimeSpan.FromSeconds(10));
            log.Add($"reconnected={vm.IsConnected}");

            // Dialogs.
            await RenderWindow(new SaveToAmpDialog(vm.Slots, 6, vm.PresetName), Path.Combine(outputDir, "dialog-save-to-amp.png"));
            await RenderWindow(new PromptDialog("Save to amp",
                    "Replace preset 06 \"Chime Sixties\" on the amp with \"Chime Sixties\"?\n\nThe preset in that slot now will be lost.\n\nBypassed effects are stored as empty slots.",
                    "Replace", destructive: true),
                Path.Combine(outputDir, "dialog-confirm.png"));

            vm.Dispose();
            try
            {
                Directory.Delete(libraryRoot, true);
            }
            catch (IOException)
            {
            }
            log.Add("done");
        }
        catch (Exception ex)
        {
            log.Add("ERROR " + ex);
        }
        finally
        {
            File.WriteAllLines(Path.Combine(outputDir, "snapshot-log.txt"), log);
            app.Shutdown();
        }
    }

    private static async Task RenderWindow(Window dialog, string path)
    {
        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = -32000;
        dialog.Top = -32000;
        dialog.ShowActivated = false;
        dialog.Show();
        await Idle();
        Save(Render((FrameworkElement)VisualTreeHelper.GetChild(dialog, 0), dialog.Background), path);
        dialog.Close();
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(50);
        await Idle();
    }

    private static async Task Idle()
    {
        await Task.Delay(120);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static BitmapSource Render(FrameworkElement element, Brush? background = null)
    {
        double w = element.ActualWidth, h = element.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background ?? (Brush)Application.Current.FindResource("WindowBgBrush"), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w, h));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w), (int)Math.Ceiling(h), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    /// <summary>Renders an element at its full desired height (as if nothing scrolled).</summary>
    private static BitmapSource RenderDetached(FrameworkElement element, double width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        var size = element.DesiredSize;
        element.Arrange(new Rect(new Point(0, 0), new Size(width, size.Height)));
        element.UpdateLayout();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((Brush)Application.Current.FindResource("ChassisBrush"), null, new Rect(0, 0, width, size.Height));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, width, size.Height));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        element.InvalidateMeasure();
        return bitmap;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
