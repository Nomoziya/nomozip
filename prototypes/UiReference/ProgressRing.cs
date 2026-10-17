using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nomozip.DesignPreview;

// Uses the same displayed percentage as the bar; this is determinate progress,
// not an unrelated looping spinner.
public sealed class ProgressRing : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    protected override void OnRender(DrawingContext context)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2); var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 2);
        var track = new Pen(Background, 3); var fill = new Pen(Foreground, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        context.DrawEllipse(null, track, center, radius, radius);
        var value = Math.Clamp(Value, 0, 100);
        if (value >= 100) { context.DrawEllipse(null, fill, center, radius, radius); return; }
        if (value <= 0) return;
        var angle = value * Math.PI / 50; var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(new Point(center.X, center.Y - radius), false, false);
            drawing.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)), new Size(radius, radius), 0, value > 50, SweepDirection.Clockwise, true, false);
        }
        context.DrawGeometry(null, fill, geometry);
    }
}
