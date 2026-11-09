using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DifFuse.App.Services;
using DifFuse.App.ViewModels;

namespace DifFuse.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;
    private readonly bool _persistPlacement;

    public MainWindow(MainViewModel viewModel, AppSettings settings, bool useBackdrop = true, bool persistPlacement = true)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        _persistPlacement = persistPlacement;
        DataContext = viewModel;
        SidebarColumn.Width = new GridLength(Math.Clamp(settings.SidebarWidth, 210, 440));
        RestorePlacement();

        SourceInitialized += (_, _) =>
        {
            WindowEffects.UseDarkTitleBar(this);
            if (useBackdrop) WindowEffects.TryUseMica(this);
        };
    }

    private void RestorePlacement()
    {
        if (!_persistPlacement) return;
        if (_settings.Width is double w && w > 0 && _settings.Height is double h && h > 0)
        {
            Width = Math.Max(w, MinWidth);
            Height = Math.Max(h, MinHeight);
        }
        if (_settings.Left is { } left && _settings.Top is { } top &&
            left > SystemParameters.VirtualScreenLeft - 50 && top > SystemParameters.VirtualScreenTop - 50 &&
            left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
            top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (_settings.Maximized) WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_persistPlacement) return;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.Left = bounds.Left;
        _settings.Top = bounds.Top;
        _settings.Width = bounds.Width;
        _settings.Height = bounds.Height;
        _settings.Maximized = WindowState == WindowState.Maximized;
        _settings.SidebarWidth = SidebarColumn.ActualWidth;
        _settings.Save(AppSettings.DefaultPath);
    }

    private void LibraryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: Core.Library.LibraryEntry })
        {
            _viewModel.Library.LoadCommand.Execute(null);
        }
    }

    private void LibraryList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.Library.LoadCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>A station on the route was clicked: bring its unit into view and light its outline briefly.</summary>
    private void Station_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChainItemViewModel station }) return;

        ContentControl? panel = AmpPanel;
        if (station.Family is { } family)
        {
            var unit = _viewModel.Effects.First(u => u.Family == family);
            panel = EffectList.ItemContainerGenerator.ContainerFromItem(unit) is DependencyObject container
                ? FindChild<ContentControl>(container)
                : null;
        }
        if (panel is null) return;

        panel.BringIntoView();
        if (panel.Template.FindName("Bd", panel) is Border border && TryFindResource(station.AccentKey) is SolidColorBrush ink &&
            TryFindResource("PanelLineColor") is Color rest)
        {
            var brush = new SolidColorBrush(ink.Color);
            border.BorderBrush = brush;
            var fade = new ColorAnimation(ink.Color, rest, TimeSpan.FromMilliseconds(1100))
            {
                BeginTime = TimeSpan.FromMilliseconds(250),
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut },
            };
            fade.Completed += (_, _) => border.ClearValue(Border.BorderBrushProperty);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, fade);
        }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
