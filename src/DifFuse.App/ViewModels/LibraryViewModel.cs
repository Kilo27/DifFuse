using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using DifFuse.App.Services;
using DifFuse.Core.Library;

namespace DifFuse.App.ViewModels;

/// <summary>The PC preset library: banks (folders) of FUSE-format preset files.</summary>
public sealed class LibraryViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly PresetLibrary _library;
    private readonly IDialogService _dialogs;
    private readonly AppSettings _settings;
    private LibraryBank? _selectedBank;
    private LibraryEntry? _selectedPreset;

    public LibraryViewModel(MainViewModel main, PresetLibrary library, IDialogService dialogs, AppSettings settings)
    {
        _main = main;
        _library = library;
        _dialogs = dialogs;
        _settings = settings;

        NewBankCommand = new RelayCommand(NewBank);
        RenameBankCommand = new RelayCommand(RenameBank, () => SelectedBank is not null);
        DeleteBankCommand = new RelayCommand(DeleteBank, () => SelectedBank is not null);
        SaveCurrentCommand = new RelayCommand(SaveCurrent, () => SelectedBank is not null);
        LoadCommand = new AsyncCommand(LoadSelectedAsync, () => SelectedPreset is not null);
        RenamePresetCommand = new RelayCommand(RenamePreset, () => SelectedPreset is not null);
        DeletePresetCommand = new RelayCommand(DeletePreset, () => SelectedPreset is not null);
        ImportCommand = new RelayCommand(Import, () => SelectedBank is not null);
        ExportCurrentCommand = new RelayCommand(ExportCurrent);
        ExportSelectedCommand = new RelayCommand(ExportSelected, () => SelectedPreset is not null);
        OpenFolderCommand = new RelayCommand(() => _dialogs.OpenFolder(SelectedBank?.Path ?? _library.Root));

        RefreshBanks(_settings.LastBank);
    }

    public ObservableCollection<LibraryBank> Banks { get; } = new();

    public ObservableCollection<LibraryEntry> Presets { get; } = new();

    public string Root => _library.Root;

    public LibraryBank? SelectedBank
    {
        get => _selectedBank;
        set
        {
            if (!SetProperty(ref _selectedBank, value)) return;
            if (value is not null) _settings.LastBank = value.Name;
            RefreshPresets(null);
        }
    }

    public LibraryEntry? SelectedPreset
    {
        get => _selectedPreset;
        set => SetProperty(ref _selectedPreset, value);
    }

    public bool HasPresets => Presets.Count > 0;

    public ICommand NewBankCommand { get; }
    public ICommand RenameBankCommand { get; }
    public ICommand DeleteBankCommand { get; }
    public ICommand SaveCurrentCommand { get; }
    public AsyncCommand LoadCommand { get; }
    public ICommand RenamePresetCommand { get; }
    public ICommand DeletePresetCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportCurrentCommand { get; }
    public ICommand ExportSelectedCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public void RefreshBanks(string? select)
    {
        try
        {
            var banks = _library.GetBanks();
            Banks.Clear();
            foreach (var b in banks) Banks.Add(b);
            SelectedBank = Banks.FirstOrDefault(b => string.Equals(b.Name, select, StringComparison.OrdinalIgnoreCase)) ?? Banks.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _main.StatusText = $"Could not open the library at {_library.Root}: {ex.Message}";
        }
    }

    private void RefreshPresets(string? selectPath)
    {
        Presets.Clear();
        if (_selectedBank is not null)
        {
            foreach (var p in _library.GetPresets(_selectedBank)) Presets.Add(p);
        }
        SelectedPreset = Presets.FirstOrDefault(p => string.Equals(p.Path, selectPath, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(HasPresets));
    }

    private void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _main.StatusText = $"{what} failed: {ex.Message}";
            _dialogs.ShowMessage($"{what} failed", ex.Message);
        }
    }

    private void NewBank()
    {
        var name = _dialogs.Prompt("New bank", "Bank name", "New bank");
        if (string.IsNullOrWhiteSpace(name)) return;
        Try("Creating the bank", () =>
        {
            var bank = _library.CreateBank(name);
            RefreshBanks(bank.Name);
            _main.StatusText = $"Created bank \"{bank.Name}\".";
        });
    }

    private void RenameBank()
    {
        if (_selectedBank is not { } bank) return;
        var name = _dialogs.Prompt("Rename bank", "Bank name", bank.Name);
        if (string.IsNullOrWhiteSpace(name) || name == bank.Name) return;
        Try("Renaming the bank", () =>
        {
            var renamed = _library.RenameBank(bank, name);
            RefreshBanks(renamed.Name);
        });
    }

    private void DeleteBank()
    {
        if (_selectedBank is not { } bank) return;
        int count = _library.GetPresets(bank).Count;
        if (!_dialogs.Confirm("Delete bank",
                $"Move the bank \"{bank.Name}\" and its {count} preset{(count == 1 ? "" : "s")} to the Recycle Bin?", "Delete", destructive: true))
        {
            return;
        }
        Try("Deleting the bank", () =>
        {
            _library.DeleteBank(bank);
            RefreshBanks(null);
            _main.StatusText = $"Moved bank \"{bank.Name}\" to the Recycle Bin.";
        });
    }

    private void SaveCurrent()
    {
        if (_selectedBank is not { } bank) return;
        var name = _dialogs.Prompt("Save to library", $"Preset name (saved in {bank.Name})", _main.PresetName);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        bool overwrite = false;
        if (_library.Exists(bank, name))
        {
            if (!_dialogs.Confirm("Replace preset", $"\"{name}\" is already in {bank.Name}. Replace it?", "Replace", destructive: true)) return;
            overwrite = true;
        }

        Try("Saving the preset", () =>
        {
            var entry = _library.Save(bank, _main.CurrentPreset() with { Name = name }, _settings.Author, overwrite);
            _main.RenameCurrent(name);
            RefreshPresets(entry.Path);
            _main.StatusText = $"Saved \"{name}\" to the library bank {bank.Name}.";
        });
    }

    private async Task LoadSelectedAsync()
    {
        if (_selectedPreset is not { } entry) return;
        Core.Models.Preset preset;
        try
        {
            preset = _library.Load(entry);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _main.StatusText = $"Could not open {entry.Name}: {ex.Message}";
            return;
        }
        await _main.ApplyPresetAsync(preset, $"the library ({entry.BankName})");
    }

    private void RenamePreset()
    {
        if (_selectedPreset is not { } entry) return;
        var name = _dialogs.Prompt("Rename preset", "Preset name", entry.Name);
        if (string.IsNullOrWhiteSpace(name) || name == entry.Name) return;
        Try("Renaming the preset", () =>
        {
            var renamed = _library.Rename(entry, name.Trim());
            RefreshPresets(renamed.Path);
        });
    }

    private void DeletePreset()
    {
        if (_selectedPreset is not { } entry) return;
        if (!_dialogs.Confirm("Delete preset", $"Move \"{entry.Name}\" to the Recycle Bin?", "Delete", destructive: true)) return;
        Try("Deleting the preset", () =>
        {
            _library.Delete(entry);
            RefreshPresets(null);
            _main.StatusText = $"Moved \"{entry.Name}\" to the Recycle Bin.";
        });
    }

    private void Import()
    {
        if (_selectedBank is not { } bank) return;
        var files = _dialogs.PickFilesToImport();
        if (files is null || files.Count == 0) return;

        var results = _library.Import(bank, files);
        var last = results.LastOrDefault(r => r.Imported);
        RefreshPresets(last?.Entry?.Path);

        int imported = results.Count(r => r.Imported);
        _main.StatusText = $"Imported {imported} of {results.Count} file{(results.Count == 1 ? "" : "s")} into {bank.Name}.";
        if (imported != results.Count || results.Count > 1)
        {
            _dialogs.ShowMessage("Import", string.Join("\n", results.Select(r => r.Message)));
        }
    }

    private void ExportCurrent()
    {
        var path = _dialogs.PickExportPath(_main.PresetName);
        if (path is null) return;
        Try("Exporting", () =>
        {
            FuseFile.Save(path, _main.CurrentPreset(), _settings.Author);
            _main.StatusText = $"Exported \"{_main.PresetName}\" to {path}.";
        });
    }

    private void ExportSelected()
    {
        if (_selectedPreset is not { } entry) return;
        var path = _dialogs.PickExportPath(entry.Name);
        if (path is null) return;
        Try("Exporting", () =>
        {
            var doc = FuseFile.Load(entry.Path);
            FuseFile.Save(path, doc.Preset ?? throw new InvalidDataException("No amp settings."), doc.Author);
            _main.StatusText = $"Exported \"{entry.Name}\" to {path}.";
        });
    }
}
