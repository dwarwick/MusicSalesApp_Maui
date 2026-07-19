using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MusicSalesApp.Maui.ViewModels;

/// <summary>
/// Observable collection optimized for replacing an entire UI list. A replacement
/// emits one reset notification instead of one notification per item.
/// </summary>
public sealed class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public ObservableRangeCollection()
    {
    }

    public ObservableRangeCollection(IEnumerable<T> items)
        : base(items)
    {
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var replacement = ReferenceEquals(items, this)
            ? items.ToArray()
            : items as IReadOnlyCollection<T> ?? items.ToArray();
        CheckReentrancy();

        Items.Clear();
        foreach (var item in replacement)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
