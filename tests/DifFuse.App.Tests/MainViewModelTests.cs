using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using DifFuse.App.Services;
using DifFuse.App.ViewModels;
using DifFuse.Core.Device;
using DifFuse.Core.Fake;
using DifFuse.Core.Library;
using DifFuse.Core.Models;

namespace DifFuse.App.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private sealed class ScriptedDialogs : IDialogService
    {
        public SaveToAmpChoice? SaveChoice { get; set; }
        public bool ConfirmAnswer { get; set; }
        public string? PromptAnswer { get; set; }
        public List<string> Confirmations { get; } = new();

        public bool Confirm(string title, string message, string confirmText, bool destructive = false)
        {
            Confirmations.Add(message);
            return ConfirmAnswer;
        }

        public string? Prompt(string title, string label, string initial, int maxLength = 0) => PromptAnswer;
        public void ShowMessage(string title, string message) { }
        public SaveToAmpChoice? AskSaveToAmp(IReadOnlyList<PresetSlotViewModel> slots, int suggestedSlot, string suggestedName) => SaveChoice;
        public IReadOnlyList<string>? PickFilesToImport() => null;
        public string? PickExportPath(string suggestedName) => null;
        public void OpenFolder(string path) { }
    }

    private sealed class DeletingRecycler : IFileRecycler
    {
        public void Recycle(string path) => File.Delete(path);
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "DifFuseAppTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>Runs a test on an STA thread with a WPF dispatcher, like the real app.</summary>
    private void OnDispatcher(Func<FakeDeviceLocator, ScriptedDialogs, MainViewModel, Task> test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var locator = new FakeDeviceLocator();
            var dialogs = new ScriptedDialogs();
            var connection = new MustangConnection(locator, new ConnectionOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(50),
                RetryDelay = TimeSpan.FromMilliseconds(50),
                Client = new ClientOptions
                {
                    AckTimeout = TimeSpan.FromMilliseconds(200),
                    ReplyIdleTimeout = TimeSpan.FromMilliseconds(60),
                    LiveUpdateInterval = TimeSpan.FromMilliseconds(20),
                },
            });
            var library = new PresetLibrary(_root, new DeletingRecycler());
            var vm = new MainViewModel(connection, library, dialogs, new AppSettings(), isSimulated: true);

            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    vm.Start();
                    await WaitFor(() => vm.IsConnected);
                    await test(locator, dialogs, vm);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    vm.Dispose();
                    dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "Test timed out.");
        if (error is not null) ExceptionDispatchInfo.Throw(error);
    }

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Condition not met in time.");
            await Task.Delay(20);
        }
    }

    [Fact]
    public void Connecting_fills_the_slots_and_the_rack() => OnDispatcher(async (locator, _, vm) =>
    {
        Assert.Equal(24, vm.Slots.Count);
        Assert.Equal(0, vm.CurrentSlot);
        Assert.True(vm.Slots[0].IsCurrent);
        Assert.Equal(locator.Amp.Live.Name, vm.PresetName);
        Assert.Equal(locator.Amp.Live.Amp.Gain, vm.Amp.Gain.ByteValue);
        Assert.Equal("Mustang I/II (v2)", vm.DeviceName);
        Assert.False(vm.IsEdited);
        await Task.CompletedTask;
    });

    [Fact]
    public void Turning_a_knob_changes_the_amp() => OnDispatcher(async (locator, _, vm) =>
    {
        vm.Amp.Treble.Value = 17;
        await WaitFor(() => locator.Amp.Live.Amp.Treble == 17);
        Assert.True(vm.IsEdited);

        var reverb = vm.Reverb;
        reverb.Knobs[1].Value = 222;
        await WaitFor(() => locator.Amp.Live.Reverb.Knobs[1] == 222);
    });

    [Fact]
    public void Selecting_a_slot_loads_it() => OnDispatcher(async (locator, _, vm) =>
    {
        vm.SelectedSlot = vm.Slots[9];
        await WaitFor(() => vm.CurrentSlot == 9);
        Assert.Equal(9, locator.Amp.CurrentSlot);
        Assert.Equal(locator.Amp.Presets[9].Name, vm.PresetName);
        Assert.Equal(locator.Amp.Presets[9].Amp.Bass, vm.Amp.Bass.ByteValue);
    });

    [Fact]
    public void Save_to_amp_writes_nothing_unless_the_user_confirms() => OnDispatcher(async (locator, dialogs, vm) =>
    {
        dialogs.SaveChoice = new SaveToAmpChoice(12, "Kept Sound");
        dialogs.ConfirmAnswer = false;
        vm.SaveToAmpCommand.Execute(null);
        await Task.Delay(300);
        Assert.Equal(0, locator.Amp.SaveCount);
        Assert.Single(dialogs.Confirmations);
        Assert.Contains("12", dialogs.Confirmations[0]);

        dialogs.ConfirmAnswer = true;
        vm.Amp.Bass.Value = 3;
        vm.SaveToAmpCommand.Execute(null);
        await WaitFor(() => locator.Amp.SaveCount == 1);
        await WaitFor(() => vm.Slots[12].Name == "Kept Sound");
        Assert.Equal(3, locator.Amp.Presets[12].Amp.Bass);
        Assert.Equal(12, vm.CurrentSlot);
        Assert.False(vm.IsEdited);
    });

    [Fact]
    public void Cancelling_the_slot_picker_writes_nothing() => OnDispatcher(async (locator, dialogs, vm) =>
    {
        dialogs.SaveChoice = null;
        dialogs.ConfirmAnswer = true;
        vm.SaveToAmpCommand.Execute(null);
        await Task.Delay(300);
        Assert.Equal(0, locator.Amp.SaveCount);
        Assert.Empty(dialogs.Confirmations);
    });

    [Fact]
    public void Moving_an_effect_after_the_amp_updates_the_amp_and_the_chain() => OnDispatcher(async (locator, _, vm) =>
    {
        vm.Stomp.SelectedModel = vm.Stomp.Models.First(m => m.Id == 0x3c);
        vm.Stomp.IsPostAmp = true;
        await WaitFor(() => locator.Amp.Live.Stomp.ModelId == 0x3c && locator.Amp.Live.Stomp.IsPostAmp);

        var labels = vm.Chain.Select(c => c.Label).ToList();
        Assert.True(labels.IndexOf("STOMP") > labels.IndexOf("AMP"));
        Assert.Equal(4, new[] { vm.Stomp, vm.Modulation, vm.Delay, vm.Reverb }.Select(u => u.Slot).Distinct().Count());
    });

    [Fact]
    public void Library_save_and_load_round_trip_through_the_amp() => OnDispatcher(async (locator, dialogs, vm) =>
    {
        vm.Amp.Middle.Value = 42;
        dialogs.PromptAnswer = "Library Test";
        vm.Library.SaveCurrentCommand.Execute(null);
        Assert.Contains(vm.Library.Presets, p => p.Name == "Library Test");

        dialogs.ConfirmAnswer = true; // discard the edit when switching slots
        vm.SelectedSlot = vm.Slots[3];
        await WaitFor(() => vm.CurrentSlot == 3);
        Assert.NotEqual(42, vm.Amp.Middle.ByteValue);

        vm.Library.SelectedPreset = vm.Library.Presets.First(p => p.Name == "Library Test");
        await vm.Library.LoadCommand.ExecuteAsync();
        Assert.Equal(42, vm.Amp.Middle.ByteValue);
        await WaitFor(() => locator.Amp.Live.Amp.Middle == 42);
        Assert.Equal(0, locator.Amp.SaveCount);
    });

    [Fact]
    public void Unplugging_and_replugging_reconnects() => OnDispatcher(async (locator, _, vm) =>
    {
        locator.Unplug();
        await WaitFor(() => !vm.IsConnected);
        locator.Plug();
        await WaitFor(() => vm.IsConnected);
        Assert.Equal(24, vm.Slots.Count);
        Assert.Equal(2, locator.OpenCount);
    });
}
