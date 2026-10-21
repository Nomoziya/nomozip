using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace ArchivePassword.Desktop;

// Avoid repeated property-path reflection while WPF sorts large inventories.
public sealed class ArchiveContentComparer(IReadOnlyList<SortDescription> fields, CultureInfo culture) : IComparer
{
    public ArchiveContentComparer(string member,ListSortDirection direction,CultureInfo culture) : this([new SortDescription(member,direction)],culture) { }
    public int Compare(object? x, object? y)
    {
        if (x is not ArchiveContentRow left || y is not ArchiveContentRow right) return 0;
        foreach(var field in fields)
        {
            var order = field.PropertyName switch
            {
                "Entry.Size" => left.Entry.Size.CompareTo(right.Entry.Size),
                nameof(ArchiveContentRow.Path) => culture.CompareInfo.Compare(left.Path, right.Path),
                _ => culture.CompareInfo.Compare(left.DisplayName, right.DisplayName)
            };
            if(order!=0)return field.Direction == ListSortDirection.Descending ? -Math.Sign(order) : order;
        }
        return 0;
    }
}
