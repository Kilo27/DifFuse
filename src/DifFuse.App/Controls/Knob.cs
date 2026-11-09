using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DifFuse.App.Controls;

/// <summary>
/// A rotary knob. Drag up or down (Shift for fine steps), use the mouse wheel or the arrow keys,
/// double-click to reset. Values are whole numbers between <see cref="RangeBase.Minimum"/> and
/// <see cref="RangeBase.Maximum"/>; small ranges act as stepped selectors.
/// </summary>
public class Knob : RangeBase
{
    private const double StartAngle = -135;
    private const double Sweep = 270;

    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(44.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Value-arc colour. Inherited, so a rack unit can set it once for all its knobs.</summary>
    public static readonly DependencyProperty ArcBrushProperty = DependencyProperty.RegisterAttached(
        nameof(ArcBrush), typeof(Brush), typeof(Knob),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0xF2, 0xA4, 0x3A)),
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>Knob diameter for a row of knobs, set on the ItemsControl that shows them.</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.RegisterAttached(
        "Size", typeof(double), typeof(Knob), new FrameworkPropertyMetadata(44.0));

    public static Brush GetArcBrush(DependencyObject d) => (Brush)d.GetValue(ArcBrushProperty);

    public static void SetArcBrush(DependencyObject d, Brush value) => d.SetValue(ArcBrushProperty, value);

    public static double GetSize(DependencyObject d) => (double)d.GetValue(SizeProperty);

    public static void SetSize(DependencyObject d, double value) => d.SetValue(SizeProperty, value);

    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(Knob), new PropertyMetadata(128.0));

    private static readonly Pen TrackPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x0B, 0x0C, 0x0E)), 3.2)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    });

    private static readonly Brush ShadowBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)));
    private static readonly Pen RimPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x11)), 1));
    private static readonly Brush BodyBrush = Freeze(new LinearGradientBrush(
        [new GradientStop(Color.FromRgb(0x5E, 0x62, 0x6A), 0), new GradientStop(Color.FromRgb(0x2A, 0x2C, 0x31), 0.55), new GradientStop(Color.FromRgb(0x1A, 0x1B, 0x1E), 1)],
        new Point(0.3, 0), new Point(0.7, 1)));
    private static readonly Brush CapBrush = Freeze(new RadialGradientBrush(
        [new GradientStop(Color.FromRgb(0x46, 0x49, 0x50), 0), new GradientStop(Color.FromRgb(0x24, 0x26, 0x2A), 1)])
    {
        GradientOrigin = new Point(0.4, 0.3),
    });
    private static readonly Pen PointerPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xF3, 0xEF, 0xE8)), 2.2)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    });
    private static readonly Brush TickBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6E, 0x72, 0x79)));
    private static readonly Pen FocusPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xF2, 0xA4, 0x3A)), 1.2)
    {
        DashStyle = new DashStyle([2, 2], 0),
    });

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

    protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsKeyboardFocusWithinChanged(e);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 4) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double arcRadius = size / 2 - 2.5;
        double bodyRadius = arcRadius - Math.Max(4.5, size * 0.11);
        double fraction = Math.Clamp((Value - Minimum) / Range, 0, 1);
        double angle = StartAngle + fraction * Sweep;

        dc.DrawGeometry(null, TrackPen, Arc(center, arcRadius, StartAngle, StartAngle + Sweep));
        if (fraction > 0.002)
        {
            var arcPen = new Pen(ArcBrush, 3.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, arcPen, Arc(center, arcRadius, StartAngle, angle));
        }

        if (IsStepped)
        {
            int steps = (int)Range;
            for (int i = 0; i <= steps; i++)
            {
                var p = PointAt(center, arcRadius + 0.1, StartAngle + Sweep * i / steps);
                dc.DrawEllipse(TickBrush, null, p, 1.1, 1.1);
            }
        }

        dc.DrawEllipse(ShadowBrush, null, new Point(center.X, center.Y + 1.6), bodyRadius + 0.5, bodyRadius + 0.5);
        dc.DrawEllipse(BodyBrush, RimPen, center, bodyRadius, bodyRadius);
        dc.DrawEllipse(CapBrush, null, center, bodyRadius * 0.7, bodyRadius * 0.7);
        dc.DrawLine(PointerPen, PointAt(center, bodyRadius * 0.28, angle), PointAt(center, bodyRadius * 0.92, angle));

        if (IsKeyboardFocused)
        {
            dc.DrawEllipse(null, FocusPen, center, arcRadius + 2, arcRadius + 2);
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
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
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

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private sealed class KnobAutomationPeer(Knob owner) : RangeBaseAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(Knob);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    }
}
