using System.ComponentModel;
using System.Data;

namespace TableLens.Desktop.Views;

// DataRowView has overloaded indexers which are ambiguous to Avalonia bindings.
// A single integer indexer keeps dynamic columns predictable, including JSON keys.
public sealed class GridRow(DataRow row, Action? changed = null) : INotifyPropertyChanged, IEditableObject
{
    private object?[]? _before;
    public DataRow Row { get; } = row;
    public bool HasPendingChanges => _before is not null && Row.ItemArray.Where((v, i) => !Equals(v, _before[i])).Any();
    public object this[int index]
    {
        get => Row[index];
        set
        {
            Row[index] = value;
            PropertyChanged?.Invoke(this, new("Item[]")); changed?.Invoke();
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void BeginEdit() { if (_before is null) { _before = Row.ItemArray!; Row.BeginEdit(); } }
    public void EndEdit()
    {
        if (_before is null) return;
        _before = null; Row.EndEdit(); changed?.Invoke();
    }
    public void CancelEdit()
    {
        if (_before is null) return;
        Row.CancelEdit(); _before = null;
        PropertyChanged?.Invoke(this, new("Item[]")); changed?.Invoke();
    }
}
