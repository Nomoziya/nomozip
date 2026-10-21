using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace ArchivePassword.Desktop;

// Avoid repeated property-path reflection while WPF sorts large inventories.
public sealed class ArchiveContentComparer(IReadOnlyList<SortDescription> fields, CultureInfo culture, bool? showPath=null) : IComparer
{
    public ArchiveContentComparer(string member,ListSortDirection direction,CultureInfo culture) : this([new SortDescription(member,direction)],culture) { }
    public int Compare(object? x, object? y)
    {
        if (x is not ArchiveContentRow left || y is not ArchiveContentRow right) return 0;
        // Compare is called millions of times: enumerating IReadOnlyList here allocates per comparison.
        for(var index=0;index<fields.Count;index++)
        {
            var field=fields[index];
            var order = field.PropertyName switch
            {
                "Entry.Size" => left.Entry.Size.CompareTo(right.Entry.Size),
                nameof(ArchiveContentRow.Path) => culture.CompareInfo.Compare(left.Path, right.Path),
                _ => culture.CompareInfo.Compare(showPath is {} full?(full?left.Path:left.Name):left.DisplayName,showPath is {} fullRight?(fullRight?right.Path:right.Name):right.DisplayName)
            };
            if(order!=0)return field.Direction == ListSortDirection.Descending ? -Math.Sign(order) : order;
        }
        return 0;
    }
}
