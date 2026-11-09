using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using DifFuse.App.Services;
using DifFuse.Core.Device;
using DifFuse.Core.Library;
using DifFuse.Core.Models;

namespace DifFuse.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly MustangConnection _connection;
    private readonly IDialogService _dialogs;
    private readonly Dispatcher _dispatcher;
    private MustangClient? _client;
    private DeviceInfo? _device;
    private ConnectionState _state = ConnectionState.Stopped;
    private PresetSlotViewModel? _selectedSlot;
    private int _currentSlot = -1;
    private string _presetName = Preset.Default.Name;
    private bool _isEdited;
    private string _statusText = "Looking for a Mustang amp…";
    private bool _suppressSlotLoad;
    private bool _busy;

    public MainViewModel(MustangConnection connection, PresetLibrary library, IDialogService dialogs, AppSettings settings, bool isSimulated)
    {
        _connection = connection;
        _dialogs = dialogs;
        _dispatcher = Dispatcher.CurrentDispatcher;
        IsSimulated = isSimulated;
        Settings = settings;

        Amp = new AmpUnitViewModel();
        Amp.Changed += OnAmpEdited;
        Effects = EffectFamilies.All.Select(f => new EffectUnitViewModel(f)).ToList();
        foreach (var unit in Effects) unit.Changed += OnEffectEdited;

        Library = new LibraryViewModel(this, library, dialogs, settings);

        SaveToAmpCommand = new AsyncCommand(SaveToAmpAsync, () => IsConnected && Slots.Count > 0 && !_busy);
        ReconnectCommand = new RelayCommand(Reconnect);
        LoadSlotCommand = new AsyncCommand(p => p is PresetSlotViewModel s ? LoadSlotAsync(s) : Task.CompletedTask, _ => IsConnected && !_busy);

        _connection.StateChanged += (_, _) => Post(UpdateConnectionState);
        _connection.Connected += (_, e) => Post(() => OnConnected(e));
        _connection.Disconnected += (_, _) => Post(OnDisconnected);
        _connection.AmpPresetChanged += (_, e) => Post(() => OnAmpPresetChanged(e));

        LoadIntoRack(Preset.Default);
    }

    public AppSettings Settings { get; }

    public bool IsSimulated { get; }

    public AmpUnitViewModel Amp { get; }

    public IReadOnlyList<EffectUnitViewModel> Effects { get; }

    public EffectUnitViewModel Stomp => Effects[0];
    public EffectUnitViewModel Modulation => Effects[1];
    public EffectUnitViewModel Delay => Effects[2];
    public EffectUnitViewModel Reverb => Effects[3];

    public LibraryViewModel Library { get; }

    public ObservableCollection<PresetSlotViewModel> Slots { get; } = new();

    public ObservableCollection<ChainItemViewModel> Chain { get; } = new();

    public ICommand SaveToAmpCommand { get; }

    public ICommand ReconnectCommand { get; }

    public ICommand LoadSlotCommand { get; }

    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionText));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsConnected => _state == ConnectionState.Connected && _client is not null;

    public string DeviceName => _device?.DisplayName ?? "No amp";

    public string ConnectionText => _state switch
    {
        ConnectionState.Connected => IsSimulated ? "Connected (simulated)" : "Connected",
        ConnectionState.Connecting => "Connecting…",
        ConnectionState.Searching => "Searching for the amp…",
        _ => "Offline",
    };

    public PresetSlotViewModel? SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (!SetProperty(ref _selectedSlot, value)) return;
            if (!_suppressSlotLoad && value is not null && value.Index != _currentSlot)
            {
                _ = LoadSlotAsync(value);
            }
        }
    }

    public int CurrentSlot
    {
        get => _currentSlot;
        private set
        {
            if (!SetProperty(ref _currentSlot, value)) return;
            foreach (var s in Slots) s.IsCurrent = s.Index == value;
            OnPropertyChanged(nameof(CurrentSlotText));
        }
    }

    public string CurrentSlotText => _currentSlot >= 0 ? _currentSlot.ToString("00") : "--";

    public string PresetName
    {
        get => _presetName;
        private set => SetProperty(ref _presetName, value);
    }

    /// <summary>True when the rack differs from the preset stored in the current slot.</summary>
    public bool IsEdited
    {
        get => _isEdited;
        private set => SetProperty(ref _isEdited, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public void Start() => _connection.Start();

    /// <summary>The sound currently shown in the rack.</summary>
    public Preset CurrentPreset() => new()
    {
        Name = PresetName,
        Amp = Amp.ToSettings(),
        Stomp = Stomp.ToSettings(),
        Modulation = Modulation.ToSettings(),
        Delay = Delay.ToSettings(),
        Reverb = Reverb.ToSettings(),
    };

    private void Post(Action action) => _dispatcher.BeginInvoke(action);

    // ---- Connection ----

    private void UpdateConnectionState()
    {
        _device = _connection.Device ?? (_connection.State == ConnectionState.Connected ? _device : null);
        State = _connection.State;
        OnPropertyChanged(nameof(DeviceName));
        if (_connection.State == ConnectionState.Searching && _connection.LastError is { } error && _client is null)
        {
            StatusText = $"Waiting for the amp. Last attempt: {error}";
        }
    }

    private void OnConnected(ConnectedEventArgs e)
    {
        _client = e.Client;
        _device = e.Client.Device;
        bool v2 = _device.SupportsV2Models;
        Amp.SupportsV2 = v2;
        foreach (var unit in Effects) unit.SupportsV2 = v2;

        _suppressSlotLoad = true;
        try
        {
            Slots.Clear();
            for (int i = 0; i < e.Initial.PresetNames.Count; i++)
            {
                Slots.Add(new PresetSlotViewModel(i, e.Initial.PresetNames[i]));
            }
            _currentSlot = -1;
            CurrentSlot = e.Initial.CurrentSlot;
            SelectedSlot = Slots.ElementAtOrDefault(e.Initial.CurrentSlot);
        }
        finally
        {
            _suppressSlotLoad = false;
        }

        LoadIntoRack(e.Initial.Current);
        IsEdited = false;
        State = _connection.State;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(DeviceName));
        StatusText = $"Connected to {_device.DisplayName}: {Slots.Count} presets. Playing {CurrentSlotText} {PresetName}.";
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnDisconnected()
    {
        _client = null;
        OnPropertyChanged(nameof(IsConnected));
        StatusText = "The amp was disconnected. DifFuse reconnects as soon as it is back.";
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnAmpPresetChanged(AmpPresetChangedEventArgs e)
    {
        LoadIntoRack(e.Preset);
        SelectSlotQuietly(e.Slot);
        IsEdited = false;
        StatusText = $"The amp switched to {CurrentSlotText} {e.Preset.Name}.";
    }

    private void Reconnect()
    {
        StatusText = "Reconnecting…";
        _connection.Reconnect();
    }

    private void SelectSlotQuietly(int slot)
    {
        _suppressSlotLoad = true;
        try
        {
            CurrentSlot = slot;
            SelectedSlot = Slots.ElementAtOrDefault(slot);
        }
        finally
        {
            _suppressSlotLoad = false;
        }
    }

    // ---- Amp presets ----

    public async Task LoadSlotAsync(PresetSlotViewModel slot)
    {
        var client = _client;
        if (client is null || _busy) return;

        if (IsEdited && !_dialogs.Confirm("Discard changes?",
                $"\"{PresetName}\" has changes that are not saved. Load {slot.Number} {slot.Name} anyway?", "Load preset"))
        {
            SelectSlotQuietly(_currentSlot);
            return;
        }

        _busy = true;
        try
        {
            var preset = await client.LoadSlotAsync(slot.Index);
            LoadIntoRack(preset);
            SelectSlotQuietly(slot.Index);
            slot.Name = preset.Name;
            IsEdited = false;
            StatusText = $"Loaded {slot.Number} {preset.Name}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load {slot.Number}: {ex.Message}";
            SelectSlotQuietly(_currentSlot);
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task SaveToAmpAsync()
    {
        var client = _client;
        if (client is null) return;

        var choice = _dialogs.AskSaveToAmp(Slots, Math.Max(0, _currentSlot), PresetName);
        if (choice is null) return;

        var target = Slots[choice.Slot];
        bool bypassed = Effects.Any(u => u.HasModel && !u.IsOn);
        var message = $"Replace preset {target.Number} \"{target.Name}\" on the amp with \"{choice.Name}\"?\n\n" +
                      "The preset in that slot now will be lost.";
        if (bypassed) message += "\n\nBypassed effects are stored as empty slots.";
        if (!_dialogs.Confirm("Save to amp", message, "Replace", destructive: true)) return;

        _busy = true;
        try
        {
            var saved = await client.SaveToSlotAsync(choice.Slot, choice.Name);
            target.Name = saved.Name.Length > 0 ? saved.Name : choice.Name;
            LoadIntoRack(saved with { Name = target.Name });
            SelectSlotQuietly(choice.Slot);
            IsEdited = false;
            StatusText = $"Saved \"{target.Name}\" to preset {target.Number} on the amp.";
        }
        catch (Exception ex)
        {
            StatusText = $"Saving failed: {ex.Message}";
            _dialogs.ShowMessage("Saving failed", ex.Message);
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Puts a preset in the rack and, when connected, sends it to the amp's live sound.</summary>
    public async Task ApplyPresetAsync(Preset preset, string source)
    {
        LoadIntoRack(preset);
        IsEdited = true;
        var client = _client;
        if (client is null)
        {
            StatusText = $"Loaded \"{preset.Name}\" from {source}. Connect the amp to hear it.";
            return;
        }
        try
        {
            await client.ApplyPresetAsync(CurrentPreset());
            StatusText = $"Playing \"{preset.Name}\" from {source}. Save it to the amp to keep it there.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not send \"{preset.Name}\" to the amp: {ex.Message}";
        }
    }

    public void RenameCurrent(string name)
    {
        PresetName = name;
    }

    // ---- Live edits ----

    private void OnAmpEdited()
    {
        IsEdited = true;
        _client?.QueueAmp(Amp.ToSettings());
    }

    private void OnEffectEdited(EffectUnitViewModel unit, EffectChange change)
    {
        if (change == EffectChange.Placement)
        {
            var before = CurrentPreset();
            // The unit already shows the new side; rebuild from its old slot so the move picks a free position.
            var moved = before.WithEffect(before.Effect(unit.Family) with { Slot = unit.Slot })
                .WithEffectPlacement(unit.Family, unit.IsPostAmp);
            unit.SetSlot(moved.Effect(unit.Family).Slot);
        }

        IsEdited = true;
        _client?.QueueEffect(unit.ToSettings());
        UpdateChain();
    }

    private void LoadIntoRack(Preset preset)
    {
        Amp.Load(preset.Amp);
        foreach (var unit in Effects) unit.Load(preset.Effect(unit.Family));
        PresetName = preset.Name;
        UpdateChain();
    }

    private void UpdateChain()
    {
        var ordered = Effects.OrderBy(u => u.Slot).ThenBy(u => u.Family).ToList();
        var pre = ordered.Where(u => !u.IsPostAmp);
        var post = ordered.Where(u => u.IsPostAmp);

        Chain.Clear();
        int position = 1;
        foreach (var unit in pre)
        {
            Chain.Add(ChainItem(unit));
            unit.ChainPosition = position++;
        }
        Chain.Add(new ChainItemViewModel("AMP", "AmpBrush", true, true, Amp.SelectedModel?.Name ?? "Amp"));
        position++;
        foreach (var unit in post)
        {
            Chain.Add(ChainItem(unit));
            unit.ChainPosition = position++;
        }
    }

    private static ChainItemViewModel ChainItem(EffectUnitViewModel unit)
    {
        string state = !unit.HasModel ? "empty" : unit.IsOn ? "on" : "bypassed";
        string label = unit.Family switch
        {
            EffectFamily.Stomp => "STOMP",
            EffectFamily.Modulation => "MOD",
            EffectFamily.Delay => "DELAY",
            _ => "REVERB",
        };
        return new ChainItemViewModel(label, unit.AccentKey, unit.IsActive, false,
            $"{unit.Family.DisplayName()}: {unit.SelectedModel.Name} ({state}), {(unit.IsPostAmp ? "after" : "before")} the amp");
    }

    public void Dispose() => _connection.Dispose();
}
