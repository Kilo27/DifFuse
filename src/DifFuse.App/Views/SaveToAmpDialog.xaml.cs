using System.Windows;
using System.Windows.Controls;
using DifFuse.App.Services;
using DifFuse.App.ViewModels;

namespace DifFuse.App.Views;

public partial class SaveToAmpDialog : Window
{
    public SaveToAmpDialog(IReadOnlyList<PresetSlotViewModel> slots, int suggestedSlot, string suggestedName)
    {
        InitializeComponent();
        SlotList.ItemsSource = slots;
        SlotList.SelectedIndex = Math.Clamp(suggestedSlot, 0, Math.Max(0, slots.Count - 1));
        NameBox.Text = suggestedName.Length > 32 ? suggestedName[..32] : suggestedName;
        Loaded += (_, _) =>
        {
            SlotList.ScrollIntoView(SlotList.SelectedItem);
            NameBox.Focus();
            NameBox.SelectAll();
        };
        SourceInitialized += (_, _) => WindowEffects.UseDarkTitleBar(this);
        UpdateSaveButton();
    }

    public SaveToAmpChoice? Choice { get; private set; }

    private void UpdateSaveButton() =>
        SaveButton.IsEnabled = SlotList.SelectedItem is PresetSlotViewModel && !string.IsNullOrWhiteSpace(NameBox.Text);

    private void SlotList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSaveButton();

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateSaveButton();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SlotList.SelectedItem is not PresetSlotViewModel slot || string.IsNullOrWhiteSpace(NameBox.Text)) return;
        Choice = new SaveToAmpChoice(slot.Index, NameBox.Text.Trim());
        DialogResult = true;
    }
}
