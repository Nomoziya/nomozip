using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ArchivePassword.Desktop;

// Keep the bound collection/view (including user sorting), replacing contents in one notification.
public sealed class ResettableCollection<T> : ObservableCollection<T>
{
    public ResettableCollection() { }
    public ResettableCollection(IEnumerable<T> values) : base(values) { }
    public void ReplaceAll(IEnumerable<T> values)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var value in values) Items.Add(value);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
