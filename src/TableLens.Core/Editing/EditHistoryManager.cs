using System.Data;

namespace TableLens.Core.Editing;

public sealed class EditHistoryManager
{
    private readonly List<IEditAction> _actions = new();
    private int _position;

    public event EventHandler? Changed;

    public bool CanUndo => _position > 0;

    public bool CanRedo => _position < _actions.Count;

    public bool HasChanges => _position > 0;

    public bool IsApplying { get; private set; }

    public int AppliedCount => _position;

    public IReadOnlyList<EditHistoryEntry> Entries => _actions
        .Select((action, index) => new EditHistoryEntry(
            action.Timestamp,
            action.Description,
            index < _position))
        .ToArray();

    public void Execute(IEditAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Apply(action.Redo);
        RecordApplied(action);
    }

    public void RecordApplied(IEditAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsApplying)
        {
            return;
        }

        if (_position < _actions.Count)
        {
            _actions.RemoveRange(_position, _actions.Count - _position);
        }

        _actions.Add(action);
        _position++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        var action = _actions[_position - 1];
        Apply(action.Undo);
        _position--;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        var action = _actions[_position];
        Apply(action.Redo);
        _position++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UndoAll()
    {
        while (CanUndo)
        {
            Undo();
        }
    }

    public void Clear()
    {
        _actions.Clear();
        _position = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(Action operation)
    {
        IsApplying = true;
        try
        {
            operation();
        }
        finally
        {
            IsApplying = false;
        }
    }
}

public sealed record EditHistoryEntry(DateTime Timestamp, string Description, bool IsApplied);

public interface IEditAction
{
    DateTime Timestamp { get; }

    string Description { get; }

    void Undo();

    void Redo();
}

public sealed class DelegateEditAction : IEditAction
{
    private readonly Action _undo;
    private readonly Action _redo;

    public DelegateEditAction(string description, Action undo, Action redo)
    {
        Description = description;
        _undo = undo;
        _redo = redo;
        Timestamp = DateTime.Now;
    }

    public DateTime Timestamp { get; }
    public string Description { get; }
    public void Undo() => _undo();
    public void Redo() => _redo();
}

public sealed class CellValueEditAction : IEditAction
{
    private readonly DataRow _row;
    private readonly DataColumn _column;
    private readonly object _oldValue;
    private readonly object _newValue;

    public CellValueEditAction(
        DataRow row,
        DataColumn column,
        object oldValue,
        object newValue,
        int displayedRowNumber)
    {
        _row = row;
        _column = column;
        _oldValue = oldValue;
        _newValue = newValue;
        Timestamp = DateTime.Now;
        Description =
            $"Рядок {displayedRowNumber}, поле {column.Caption}: " +
            $"«{FormatValue(oldValue)}» → «{FormatValue(newValue)}»";
    }

    public DateTime Timestamp { get; }

    public string Description { get; }

    public void Undo() => _row[_column] = _oldValue;

    public void Redo() => _row[_column] = _newValue;

    private static string FormatValue(object value) => value == DBNull.Value
        ? "<порожньо>"
        : value.ToString() ?? string.Empty;
}

public sealed class AddRowEditAction : IEditAction
{
    private readonly DataTable _table;
    private readonly DataRow _row;
    private readonly int _position;
    private object[] _values;

    public AddRowEditAction(DataTable table, DataRow row, int position)
    {
        _table = table;
        _row = row;
        _position = position;
        _values = row.ItemArray!;
        Timestamp = DateTime.Now;
        Description = $"Додано рядок {_position + 1}";
    }

    public DateTime Timestamp { get; }

    public string Description { get; }

    public void Undo()
    {
        if (_table.Rows.IndexOf(_row) >= 0)
        {
            // Rows.Remove clears the record buffer. Keep values for redo.
            _values = _row.ItemArray!;
            _table.Rows.Remove(_row);
        }
    }

    public void Redo()
    {
        if (_row.RowState == DataRowState.Detached)
        {
            _row.ItemArray = _values;
            _table.Rows.InsertAt(_row, Math.Min(_position, _table.Rows.Count));
        }
    }
}

public sealed class DeleteRowsEditAction : IEditAction
{
    private readonly DataTable _table;
    private readonly IReadOnlyList<RowPosition> _rows;

    public DeleteRowsEditAction(DataTable table, IEnumerable<DataRow> rows)
    {
        _table = table;
        _rows = rows
            .Select(row => new RowPosition(row, table.Rows.IndexOf(row), row.ItemArray!))
            .Where(item => item.Position >= 0)
            .OrderBy(item => item.Position)
            .ToArray();
        Timestamp = DateTime.Now;
        Description = _rows.Count == 1
            ? $"Видалено рядок {_rows[0].Position + 1}"
            : $"Видалено рядків: {_rows.Count}";
    }

    public DateTime Timestamp { get; }

    public string Description { get; }

    public void Undo()
    {
        foreach (var item in _rows)
        {
            if (item.Row.RowState == DataRowState.Detached)
            {
                item.Row.ItemArray = item.Values;
                _table.Rows.InsertAt(item.Row, Math.Min(item.Position, _table.Rows.Count));
            }
        }
    }

    public void Redo()
    {
        foreach (var item in _rows.OrderByDescending(item => _table.Rows.IndexOf(item.Row)))
        {
            if (_table.Rows.IndexOf(item.Row) >= 0)
            {
                _table.Rows.Remove(item.Row);
            }
        }
    }

    private sealed record RowPosition(DataRow Row, int Position, object[] Values);
}
