using System.Windows;
using System.Windows.Media;
using DifFuse.App.Services;

namespace DifFuse.App.Views;

/// <summary>A dark message, confirmation or text-entry dialog.</summary>
public partial class PromptDialog : Window
{
    public PromptDialog(string title, string message, string confirmText, string? cancelText = "Cancel",
        string? inputLabel = null, string? initialText = null, int maxLength = 0, bool destructive = false)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Message.Text = inputLabel is null ? message : (string.IsNullOrEmpty(message) ? inputLabel : $"{message}\n{inputLabel}");
        ConfirmButton.Content = confirmText;

        if (cancelText is null)
        {
            CancelButton.Visibility = Visibility.Collapsed;
            ConfirmButton.IsCancel = true;
        }
        else
        {
            CancelButton.Content = cancelText;
        }

        if (destructive)
        {
            ConfirmButton.Style = (Style)FindResource("DangerButton");
        }

        if (inputLabel is not null)
        {
            Input.Visibility = Visibility.Visible;
            Input.Text = initialText ?? "";
            if (maxLength > 0) Input.MaxLength = maxLength;
            System.Windows.Automation.AutomationProperties.SetName(Input, inputLabel);
            Loaded += (_, _) =>
            {
                Input.Focus();
                Input.SelectAll();
            };
        }
        else
        {
            Loaded += (_, _) => (destructive ? CancelButton : ConfirmButton).Focus();
        }

        SourceInitialized += (_, _) => WindowEffects.UseDarkTitleBar(this);
    }

    public string InputText => Input.Text;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (Input.Visibility == Visibility.Visible && string.IsNullOrWhiteSpace(Input.Text))
        {
            Input.Focus();
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
