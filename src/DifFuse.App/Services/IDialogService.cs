using DifFuse.App.ViewModels;

namespace DifFuse.App.Services;

/// <summary>Everything the view models need to ask the user. Swappable for tests.</summary>
public interface IDialogService
{
    /// <summary>Asks a yes/no question. <paramref name="destructive"/> styles the confirm button as a warning.</summary>
    bool Confirm(string title, string message, string confirmText, bool destructive = false);

    /// <summary>Asks for a line of text. Returns null when cancelled.</summary>
    string? Prompt(string title, string label, string initial, int maxLength = 0);

    void ShowMessage(string title, string message);

    /// <summary>Lets the user pick the amp slot and name to save under. Returns null when cancelled.</summary>
    SaveToAmpChoice? AskSaveToAmp(IReadOnlyList<PresetSlotViewModel> slots, int suggestedSlot, string suggestedName);

    IReadOnlyList<string>? PickFilesToImport();

    string? PickExportPath(string suggestedName);

    void OpenFolder(string path);
}
