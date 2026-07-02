using System.Data;
using System.Globalization;

namespace TableLens.Core.Filtering;

public enum FilterOperator
{
    Contains,
    Equals,
    StartsWith,
    EndsWith,
    GreaterThan,
    LessThan,
    IsEmpty,
    IsNotEmpty
}

public static class TableFilterBuilder
{
    public static string Build(DataTable table, string? columnName, FilterOperator filterOperator, string value)
    {
        ArgumentNullException.ThrowIfNull(table);

        var columns = string.IsNullOrEmpty(columnName)
            ? table.Columns.Cast<DataColumn>().ToArray()
            : new[] { table.Columns[columnName] ?? throw new ArgumentException("Колонку не знайдено.", nameof(columnName)) };

        var expressions = columns
            .Select(column => BuildForColumn(column, filterOperator, value))
            .Where(expression => !string.IsNullOrWhiteSpace(expression))
            .ToArray();

        return expressions.Length == 0 ? string.Empty : $"({string.Join(" OR ", expressions)})";
    }

    private static string BuildForColumn(DataColumn column, FilterOperator filterOperator, string value)
    {
        var columnRef = $"[{column.ColumnName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal)}]";

        if (filterOperator == FilterOperator.IsEmpty)
        {
            return $"({columnRef} IS NULL OR Convert({columnRef}, 'System.String') = '')";
        }

        if (filterOperator == FilterOperator.IsNotEmpty)
        {
            return $"({columnRef} IS NOT NULL AND Convert({columnRef}, 'System.String') <> '')";
        }

        if (filterOperator is FilterOperator.GreaterThan or FilterOperator.LessThan)
        {
            return BuildComparison(column, columnRef, filterOperator, value);
        }

        var escaped = EscapeLikeValue(value);
        var pattern = filterOperator switch
        {
            FilterOperator.Contains => $"%{escaped}%",
            FilterOperator.StartsWith => $"{escaped}%",
            FilterOperator.EndsWith => $"%{escaped}",
            FilterOperator.Equals => escaped,
            _ => escaped
        };

        var operation = filterOperator == FilterOperator.Equals ? "=" : "LIKE";
        return $"Convert({columnRef}, 'System.String') {operation} '{pattern}'";
    }

    private static string BuildComparison(
        DataColumn column,
        string columnRef,
        FilterOperator filterOperator,
        string value)
    {
        var operation = filterOperator == FilterOperator.GreaterThan ? ">" : "<";

        if (column.DataType == typeof(decimal) &&
            decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var decimalValue))
        {
            return $"{columnRef} {operation} {decimalValue.ToString(CultureInfo.InvariantCulture)}";
        }

        if (column.DataType == typeof(DateTime) &&
            DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dateValue))
        {
            return $"{columnRef} {operation} #{dateValue:MM/dd/yyyy HH:mm:ss}#";
        }

        if (column.DataType == typeof(string))
        {
            return $"{columnRef} {operation} '{EscapeLiteral(value)}'";
        }

        return string.Empty;
    }

    private static string EscapeLikeValue(string value) => EscapeLiteral(value)
        .Replace("[", "[[]", StringComparison.Ordinal)
        .Replace("%", "[%]", StringComparison.Ordinal)
        .Replace("*", "[*]", StringComparison.Ordinal);

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
