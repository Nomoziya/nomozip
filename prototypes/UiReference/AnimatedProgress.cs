using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nomozip.DesignPreview;

public sealed class AnimatedProgress : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(AnimatedProgress), new PropertyMetadata(0d, Changed));
    public static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.Register(nameof(MotionEnabled), typeof(bool), typeof(AnimatedProgress), new PropertyMetadata(true, Changed));
    public static readonly DependencyProperty DisplayedValueProperty = DependencyProperty.Register(nameof(DisplayedValue), typeof(double), typeof(AnimatedProgress), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool MotionEnabled { get => (bool)GetValue(MotionEnabledProperty); set => SetValue(MotionEnabledProperty, value); }
    public double DisplayedValue { get => (double)GetValue(DisplayedValueProperty); set => SetValue(DisplayedValueProperty, value); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var bar = (AnimatedProgress)sender; var from = bar.DisplayedValue; var to = Math.Clamp(bar.Value, 0, 100);
        bar.BeginAnimation(DisplayedValueProperty, null); bar.DisplayedValue = to;
        if (bar.MotionEnabled && to > 0 && to < 100 && Math.Abs(from - to) > .01)
            bar.BeginAnimation(DisplayedValueProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(220))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    public void SnapToValue() { BeginAnimation(DisplayedValueProperty, null); DisplayedValue = Math.Clamp(Value, 0, 100); }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRoundedRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight), ActualHeight / 2, ActualHeight / 2);
        var width = ActualWidth * Math.Clamp(DisplayedValue, 0, 100) / 100;
        if (width > 0) context.DrawRoundedRectangle(Foreground, null, new Rect(0, 0, width, ActualHeight), ActualHeight / 2, ActualHeight / 2);
    }
}
