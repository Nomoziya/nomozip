using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ArchivePassword.Desktop;

public partial class TaskProgressView : UserControl
{
    public TaskProgressView() { InitializeComponent(); }
}

public sealed class TaskProgressTrack : Control
{
    private bool? animationPreferenceForValidation;
    internal void SetAnimationPreferenceForValidation(bool? enabled)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") throw new InvalidOperationException("Isolated CI only.");
        animationPreferenceForValidation = enabled; Update();
    }
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(TaskProgressTrack), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(TaskProgressTrack), new PropertyMetadata(false, OnChanged));
    public bool IsRunning { get => (bool)GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    public static readonly DependencyProperty IsPausedProperty = DependencyProperty.Register(nameof(IsPaused), typeof(bool), typeof(TaskProgressTrack), new PropertyMetadata(false, OnChanged));
    public static readonly DependencyProperty DisplayedValueProperty = DependencyProperty.Register(nameof(DisplayedValue), typeof(double), typeof(TaskProgressTrack), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool IsPaused { get => (bool)GetValue(IsPausedProperty); set => SetValue(IsPausedProperty, value); }
    public double DisplayedValue { get => (double)GetValue(DisplayedValueProperty); set => SetValue(DisplayedValueProperty, value); }
    public TaskProgressTrack()
    {
        Loaded += (_, _) => { SystemParameters.StaticPropertyChanged += SystemPreferenceChanged; Update(); };
        Unloaded += (_, _) => { SystemParameters.StaticPropertyChanged -= SystemPreferenceChanged; BeginAnimation(DisplayedValueProperty, null); };
    }
    private void SystemPreferenceChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Update(); }
    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((TaskProgressTrack)sender).Update();
    private void Update()
    {
        var from = DisplayedValue; var target = Math.Clamp(double.IsFinite(Value) ? Value : 0, 0, 100);
        BeginAnimation(DisplayedValueProperty, null);
        if (IsPaused && target < 100) { DisplayedValue = from; return; }
        DisplayedValue = target;
        if (IsLoaded && IsRunning && (animationPreferenceForValidation ?? SystemParameters.ClientAreaAnimation) && target > 0 && target < 100 && Math.Abs(from - target) > .01)
            BeginAnimation(DisplayedValueProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context); context.DrawRoundedRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight), 3, 3);
        var width = ActualWidth * Math.Clamp(DisplayedValue, 0, 100) / 100;
        if (width > 0) context.DrawRoundedRectangle(Foreground, null, new Rect(0, 0, width, ActualHeight), 3, 3);
    }
}

public sealed class TaskProgressRing : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(TaskProgressRing), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context); var center = new Point(ActualWidth / 2, ActualHeight / 2); var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 1.5);
        var basePen = new Pen((Brush)FindResource("DividerBrush"), 1.6); context.DrawEllipse(null, basePen, center, radius, radius);
        var value = Math.Clamp(Value, 0, 100); if (value <= 0 || radius <= 0) return;
        var pen = new Pen(Foreground, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (value >= 100) { context.DrawEllipse(null, pen, center, radius, radius); return; }
        var angle = value / 100 * Math.PI * 2 - Math.PI / 2; var geometry = new StreamGeometry();
        using (var path = geometry.Open()) { path.BeginFigure(new Point(center.X, center.Y - radius), false, false); path.ArcTo(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)), new Size(radius, radius), 0, value > 50, SweepDirection.Clockwise, true, false); }
        context.DrawGeometry(null, pen, geometry);
    }
}
