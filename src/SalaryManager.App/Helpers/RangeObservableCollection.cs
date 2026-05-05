using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SalaryManager.App.Helpers;

/// Raises a single Reset event when replacing the contents in bulk, instead of
/// N separate Add events. Worth the difference for ItemsControls (DataGrid,
/// ListBox) that re-measure incrementally on each Add.
public class RangeObservableCollection<T> : ObservableCollection<T>
{
    public RangeObservableCollection() { }
    public RangeObservableCollection(IEnumerable<T> items) : base(items) { }

    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
