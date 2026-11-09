using System.Diagnostics;
using System.IO;
using System.Windows;
using DifFuse.App.ViewModels;
using DifFuse.App.Views;
using Microsoft.Win32;

namespace DifFuse.App.Services;

public sealed class DialogService : IDialogService
{
    private const string FuseFilter = "FUSE presets (*.fuse)|*.fuse|All files (*.*)|*.*";

    public Window? Owner { get; set; }

    private T Owned<T>(T window) where T : Window
    {
        if (Owner is { IsVisible: true }) window.Owner = Owner;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window;
    }

    public bool Confirm(string title, string message, string confirmText, bool destructive = false) =>
        Owned(new PromptDialog(title, message, confirmText, destructive: destructive)).ShowDialog() == true;

    public string? Prompt(string title, string label, string initial, int maxLength = 0)
    {
        var dialog = Owned(new PromptDialog(title, "", "OK", inputLabel: label, initialText: initial, maxLength: maxLength));
        return dialog.ShowDialog() == true ? dialog.InputText.Trim() : null;
    }

    public void ShowMessage(string title, string message) =>
        Owned(new PromptDialog(title, message, "OK", cancelText: null)).ShowDialog();

    public SaveToAmpChoice? AskSaveToAmp(IReadOnlyList<PresetSlotViewModel> slots, int suggestedSlot, string suggestedName)
    {
        var dialog = Owned(new SaveToAmpDialog(slots, suggestedSlot, suggestedName));
        return dialog.ShowDialog() == true ? dialog.Choice : null;
    }

    public IReadOnlyList<string>? PickFilesToImport()
    {
        var fuseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Fender", "FUSE");
        var dialog = new OpenFileDialog
        {
            Title = "Import FUSE presets",
            Filter = FuseFilter,
            Multiselect = true,
            InitialDirectory = Directory.Exists(fuseFolder) ? fuseFolder : null,
        };
        return Show(dialog) ? dialog.FileNames : null;
    }

    public string? PickExportPath(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export as FUSE preset",
            Filter = FuseFilter,
            DefaultExt = ".fuse",
            FileName = Core.Library.PresetLibrary.SafeFileName(suggestedName, "Preset") + ".fuse",
            OverwritePrompt = true,
        };
        return Show(dialog) ? dialog.FileName : null;
    }

    private bool Show(CommonDialog dialog) => (Owner is null ? dialog.ShowDialog() : dialog.ShowDialog(Owner)) == true;

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }
}
