using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DifFuse.App.Controls;

/// <summary>
/// Picks one of a few named positions, drawn as stops along a line: Sag (Soft, Normal, Hard), the noise gate,
/// an effect's Shape or Mode. Click a stop or drag along the line; Left/Right, Home and End work too.
/// When the stops are too close for every name to fit, only the chosen name is shown.
/// </summary>
public class StopSelector : RangeBase
{
    private const double LineY = 9;
    private const double LabelSize = 11.5;
    private const double SidePad = 4;

    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
        nameof(Labels), typeof(IEnumerable), typeof(StopSelector),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Pen LinePen = Ink.Freeze(Ink.RoundPen(Ink.TrackBrush, 2.4));
    private static readonly Pen StopPen = Ink.Freeze(new Pen(Ink.DetentBrush, 1.4));
    private static readonly Pen StopHoverPen = Ink.Freeze(new Pen(Ink.PorcelainBrush, 1.6));
    private static readonly Pen SelectedPen = Ink.Freeze(new Pen(Ink.PorcelainBrush, 2.2));
    private static readonly Pen FocusPen = Ink.Freeze(new Pen(Ink.PorcelainBrush, 1.2));

    private int _hover = -1;
    private bool _dragging;

    static StopSelector()
    {
        MaximumProperty.OverrideMetadata(typeof(StopSelector), new FrameworkPropertyMetadata(2.0,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
        ValueProperty.OverrideMetadata(typeof(StopSelector), new FrameworkPropertyMetadata(0.0,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        FocusableProperty.OverrideMetadata(typeof(StopSelector), new FrameworkPropertyMetadata(true));
        IsTabStopProperty.OverrideMetadata(typeof(StopSelector), new FrameworkPropertyMetadata(true));
        CursorProperty.OverrideMetadata(typeof(StopSelector), new FrameworkPropertyMetadata(Cursors.Hand));
        FocusVisualStyleProperty.OverrideMetadata(typeof(StopSelector), new FrameworkPropertyMetadata(null));
    }

    /// <summary>Names of the positions, first to last.</summary>
    public IEnumerable? Labels
    {
        get => (IEnumerable?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    private int Count => Math.Max(2, (int)Math.Round(Maximum - Minimum) + 1);

    private int Selected => Math.Clamp((int)Math.Round(Value - Minimum), 0, Count - 1);

    private string LabelAt(int i)
    {
        if (Labels is IList list && i < list.Count) return list[i]?.ToString() ?? "";
        return (i + 1).ToString(CultureInfo.CurrentCulture);
    }

    private FormattedText Text(string text, bool bold, Brush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? Ink.LabelBold : Ink.Label, LabelSize, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private double WidestLabel()
    {
        double widest = 0;
        for (int i = 0; i < Count; i++) widest = Math.Max(widest, Text(LabelAt(i), true, Ink.PorcelainBrush).Width);
        return widest;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        double wanted = Count * (WidestLabel() + 10);
        double width = double.IsInfinity(constraint.Width) ? wanted : Math.Min(wanted, constraint.Width);
        return new Size(width, Math.Min(36, constraint.Height));
    }

    private (double Left, double Step) Geometry()
    {
        double half = Math.Min(WidestLabel() / 2 + 1, ActualWidth / (2 * Count));
        double left = Math.Max(SidePad + 6, half);
        double right = ActualWidth - left;
        return (left, (right - left) / (Count - 1));
    }

    private double StopX(int i)
    {
        var (left, step) = Geometry();
        return left + i * step;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 10) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        int count = Count, selected = Selected;
        var (left, step) = Geometry();
        dc.DrawLine(LinePen, new Point(left, LineY), new Point(left + step * (count - 1), LineY));

        var ink = Knob.GetArcBrush(this);
        for (int i = 0; i < count; i++)
        {
            var p = new Point(left + step * i, LineY);
            if (i == selected)
            {
                dc.DrawEllipse(ink, SelectedPen, p, 6, 6);
                if (IsKeyboardFocused) dc.DrawEllipse(null, FocusPen, p, 9.5, 9.5);
            }
            else
            {
                dc.DrawEllipse(Ink.GroundBrush, i == _hover ? StopHoverPen : StopPen, p, 3.6, 3.6);
            }
        }

        bool allFit = step >= WidestLabel() + 6;
        for (int i = 0; i < count; i++)
        {
            if (!allFit && i != selected) continue; // crowded: only the chosen name, so it stays readable
            bool isSelected = i == selected;
            var text = Text(LabelAt(i), isSelected, isSelected || i == _hover ? Ink.PorcelainBrush : Ink.MutedBrush);
            double x = Math.Clamp(left + step * i - text.Width / 2, 0, Math.Max(0, ActualWidth - text.Width));
            dc.DrawText(text, new Point(x, LineY + 10));
        }
    }

    private int StopNear(double x)
    {
        var (left, step) = Geometry();
        return Math.Clamp((int)Math.Round((x - left) / Math.Max(1, step)), 0, Count - 1);
    }

    private void Pick(int index)
    {
        double value = Minimum + Math.Clamp(index, 0, Count - 1);
        if (value != Value) SetCurrentValue(ValueProperty, value);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        Pick(StopNear(e.GetPosition(this).X));
        _dragging = CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int near = StopNear(e.GetPosition(this).X);
        if (_dragging && IsMouseCaptured) Pick(near);
        if (near != _hover)
        {
            _hover = near;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int? target = e.Key switch
        {
            Key.Right or Key.Up => Selected + 1,
            Key.Left or Key.Down => Selected - 1,
            Key.Home => 0,
            Key.End => Count - 1,
            _ => null,
        };
        if (target is { } t)
        {
            Pick(t);
            e.Handled = true;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new StopSelectorAutomationPeer(this);

    private sealed class StopSelectorAutomationPeer(StopSelector owner) : RangeBaseAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(StopSelector);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetItemStatusCore() => owner.LabelAt(owner.Selected);
    }
}
