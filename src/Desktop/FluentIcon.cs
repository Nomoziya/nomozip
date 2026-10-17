using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArchivePassword.Desktop;

// Geometry is the pinned Microsoft Fluent Regular 20 asset, without an added stroke.
public sealed class FluentIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(FluentIcon), new FrameworkPropertyMetadata("Document", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (TryFindResource("Icon." + Kind) is not Geometry geometry) return;
        context.PushTransform(new ScaleTransform(ActualWidth / 20, ActualHeight / 20));
        context.DrawGeometry(Foreground, null, geometry); context.Pop();
    }
}
