using System.Windows.Markup;
using ArchivePassword.Core;

namespace ArchivePassword.Desktop;

[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(Key);
}
