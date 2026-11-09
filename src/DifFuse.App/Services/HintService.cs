using System.Windows;
using System.Windows.Input;
using DifFuse.App.ViewModels;

namespace DifFuse.App.Services;

/// <summary>
/// The line at the bottom of the window that says what the control under the mouse, or with keyboard
/// focus, does. Set <c>HintService.Title</c> and <c>HintService.Text</c> on any element.
/// </summary>
public sealed class HintService : ObservableObject
{
    private string _title = "";
    private string _text = "";
    private object? _owner;

    public static HintService Current { get; } = new();

    public static readonly DependencyProperty TitleProperty = DependencyProperty.RegisterAttached(
        "Title", typeof(string), typeof(HintService), new PropertyMetadata("", OnHintChanged));

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(HintService), new PropertyMetadata("", OnHintChanged));

    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
        "Hooked", typeof(bool), typeof(HintService), new PropertyMetadata(false));

    public static string GetTitle(DependencyObject d) => (string)d.GetValue(TitleProperty);

    public static void SetTitle(DependencyObject d, string value) => d.SetValue(TitleProperty, value);

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);

    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Text
    {
        get => _text;
        private set
        {
            if (SetProperty(ref _text, value)) OnPropertyChanged(nameof(HasHint));
        }
    }

    public bool HasHint => _text.Length > 0;

    private void Show(UIElement owner)
    {
        _owner = owner;
        Title = GetTitle(owner);
        Text = GetText(owner);
    }

    private void Hide(UIElement owner)
    {
        if (!ReferenceEquals(_owner, owner)) return;
        // Keep the hint of a control that still has keyboard focus.
        if (Keyboard.FocusedElement is DependencyObject focused && FindHintOwner(focused) is { } other && !ReferenceEquals(other, owner))
        {
            Show(other);
            return;
        }
        _owner = null;
        Title = "";
        Text = "";
    }

    private static UIElement? FindHintOwner(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is UIElement e && (bool)e.GetValue(HookedProperty)) return e;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    private static void OnHintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        if (!(bool)element.GetValue(HookedProperty))
        {
            element.SetValue(HookedProperty, true);
            element.MouseEnter += (_, _) => Current.Show(element);
            element.MouseLeave += (_, _) =>
            {
                if (!element.IsKeyboardFocusWithin) Current.Hide(element);
            };
            element.IsKeyboardFocusWithinChanged += (_, args) =>
            {
                if (args.NewValue is true) Current.Show(element);
                else if (!element.IsMouseOver) Current.Hide(element);
            };
        }
        if (ReferenceEquals(Current._owner, element)) Current.Show(element);
    }
}
