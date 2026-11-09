using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DifFuse.App.Controls;

/// <summary>
/// A rotary knob for continuous amounts and for selectors with many positions. Drag up or down (Shift for fine
/// steps), use the mouse wheel or the arrow keys, double-click to reset. Values are whole numbers between
/// <see cref="RangeBase.Minimum"/> and <see cref="RangeBase.Maximum"/>; ranges of 24 or fewer steps click
/// between detents.
/// </summary>
/// <remarks>
/// Drawn as a transit interchange: a porcelain ring on the ground colour, the value arc in the unit's line
/// colour with station ticks every tenth, and a marker where the arc ends.
/// </remarks>
public class Knob : RangeBase
{
    private const double StartAngle = -135;
    private const double Sweep = 270;

    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(42.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Line colour of the value arc. Inherited, so a unit can set it once for all its controls.</summary>
    public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.RegisterAttached(
        nameof(ArcBrush), typeof(Brush), typeof(Knob),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0x0E)),
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static Brush GetArcBrush(DependencyObject d) => (Brush)d.GetValue(ArcBrushProperty);

    public static void SetArcBrush(DependencyObject d, Brush value) => d.SetValue(ArcBrushProperty, value);

    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(Knob), new PropertyMetadata(128.0));

    private static readonly Pen TrackPen = Ink.Freeze(Ink.RoundPen(Ink.TrackBrush, 3.4));
    private static readonly Pen RingPen = Ink.Freeze(new Pen(Ink.RingBrush, 1.7));
    private static readonly Pen RingHoverPen = Ink.Freeze(new Pen(Ink.PorcelainBrush, 2.2));
    private static readonly Pen PointerPen = Ink.Freeze(Ink.RoundPen(Ink.PorcelainBrush, 2.2));
    private static readonly Pen MarkerPen = Ink.Freeze(new Pen(Ink.PorcelainBrush, 1.5));
    private static readonly Pen FocusPen = Ink.Freeze(new Pen(Ink.PorcelainBrush, 1.3));

    private Point _dragStart;
    private double _dragStartValue;
    private bool _dragging;

    static Knob()
    {
        MaximumProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(255.0, FrameworkPropertyMetadataOptions.AffectsRender));
        ValueProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(0.0,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        SmallChangeProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(1.0));
        LargeChangeProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(16.0));
        FocusableProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(true));
        IsTabStopProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(true));
        CursorProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(Cursors.SizeNS));
        FocusVisualStyleProperty.OverrideMetadata(typeof(Knob), new FrameworkPropertyMetadata(null));
    }

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public Brush ArcBrush
    {
        get => (Brush)GetValue(ArcBrushProperty);
        set => SetValue(ArcBrushProperty, value);
    }

    /// <summary>The value a double-click resets to.</summary>
    public double DefaultValue
    {
        get => (double)GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    private double Range => Math.Max(1, Maximum - Minimum);

    private bool IsStepped => Range <= 24;

    protected override Size MeasureOverride(Size constraint) =>
        new(Math.Min(Diameter, constraint.Width), Math.Min(Diameter, constraint.Height));

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

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 8) return;

        // Hit-testing covers the whole square, not only the painted ring.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double arcRadius = size / 2 - 3;
        double bodyRadius = arcRadius - Math.Max(6, size * 0.16);
        double fraction = Math.Clamp((Value - Minimum) / Range, 0, 1);
        double angle = StartAngle + fraction * Sweep;
        var arcPen = Ink.RoundPen(ArcBrush, 3.4);

        dc.DrawGeometry(null, TrackPen, Arc(center, arcRadius, StartAngle, StartAngle + Sweep));
        if (fraction > 0.002) dc.DrawGeometry(null, arcPen, Arc(center, arcRadius, StartAngle, angle));

        if (IsStepped)
        {
            // One detent per position: gaps in the coloured part, dots on the bare track.
            int steps = (int)Range;
            for (int i = 0; i <= steps; i++)
            {
                double a = StartAngle + Sweep * i / steps;
                bool passed = a <= angle + 0.01;
                dc.DrawEllipse(passed ? Ink.GroundBrush : Ink.DetentBrush, null, PointAt(center, arcRadius, a), 1.5, 1.5);
            }
        }
        else
        {
            // Station ticks every tenth, cut into the line.
            for (int i = 1; i < 10; i++)
            {
                dc.DrawEllipse(Ink.PanelBrush, null, PointAt(center, arcRadius, StartAngle + Sweep * i / 10), 0.95, 0.95);
            }
        }

        // Where the line ends: a small station in the line colour.
        dc.DrawEllipse(ArcBrush, MarkerPen, PointAt(center, arcRadius, angle), 3.4, 3.4);

        bool hot = IsMouseOver || _dragging;
        dc.DrawEllipse(Ink.GroundBrush, hot ? RingHoverPen : RingPen, center, bodyRadius, bodyRadius);
        dc.DrawLine(PointerPen, PointAt(center, bodyRadius * 0.2, angle), PointAt(center, bodyRadius * 0.78, angle));

        if (IsKeyboardFocused)
        {
            dc.DrawEllipse(null, FocusPen, center, arcRadius + 3.6, arcRadius + 3.6);
        }
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        double rad = degrees * Math.PI / 180;
        return new Point(center.X + Math.Sin(rad) * radius, center.Y - Math.Cos(rad) * radius);
    }

    private static Geometry Arc(Point center, double radius, double from, double to)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(PointAt(center, radius, from), false, false);
            ctx.ArcTo(PointAt(center, radius, to), new Size(radius, radius), 0, to - from > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void SetUserValue(double value)
    {
        value = Math.Round(Math.Clamp(value, Minimum, Maximum));
        if (value != Value) SetCurrentValue(ValueProperty, value);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (e.ClickCount == 2)
        {
            SetUserValue(DefaultValue);
            e.Handled = true;
            return;
        }
        _dragStart = e.GetPosition(this);
        _dragStartValue = Value;
        _dragging = CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || !IsMouseCaptured) return;

        var position = e.GetPosition(this);
        double dy = _dragStart.Y - position.Y;
        double pixelsForFullRange = IsStepped ? Range * 16 : 200;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) pixelsForFullRange *= 4;
        SetUserValue(_dragStartValue + dy / pixelsForFullRange * Range);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsEnabled) return;
        double step = IsStepped || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 5;
        SetUserValue(Value + Math.Sign(e.Delta) * step);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double fine = 1;
        double coarse = IsStepped ? 1 : LargeChange;
        double? target = e.Key switch
        {
            Key.Up or Key.Right => Value + (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || IsStepped ? fine : 4),
            Key.Down or Key.Left => Value - (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || IsStepped ? fine : 4),
            Key.PageUp => Value + coarse,
            Key.PageDown => Value - coarse,
            Key.Home => Minimum,
            Key.End => Maximum,
            Key.Delete or Key.Back => DefaultValue,
            _ => null,
        };
        if (target is { } t)
        {
            SetUserValue(t);
            e.Handled = true;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new KnobAutomationPeer(this);

    private sealed class KnobAutomationPeer(Knob owner) : RangeBaseAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(Knob);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    }
}
