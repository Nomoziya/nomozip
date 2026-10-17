using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nomozip.DesignPreview;

// Every functional icon uses the pinned Fluent Regular 20 geometry dictionary.
public sealed class Symbol : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Symbol), new FrameworkPropertyMetadata("Document", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (TryFindResource("Icon." + Kind) is not Geometry geometry) return;
        drawingContext.PushTransform(new ScaleTransform(ActualWidth / 20, ActualHeight / 20));
        drawingContext.DrawGeometry(Foreground, null, geometry);
        drawingContext.Pop();
    }
}
