using System.Data;
using System.Globalization;

namespace TableLens.Core.Filtering;

/// <summary>
/// Перетворює короткий користувацький синтаксис фільтра у DataColumn expression.
/// </summary>
public static class ColumnFilterParser
{
    private static readonly string[] Operators = [">=", "<=", "==", "!=", ">", "<", "=", "!"];

    public static string Build(DataColumn column, string input)
    {
        ArgumentNullException.ThrowIfNull(column);

        var expression = input.Trim();
        if (expression.Length == 0)
        {
            return string.Empty;
        }

        var conditions = SplitOrConditions(expression);
        var conditionExpressions = conditions
            .Select(condition => BuildSingle(column, condition))
            .ToArray();

        return conditionExpressions.Length == 1
            ? conditionExpressions[0]
            : $"({string.Join(" OR ", conditionExpressions.Select(item => $"({item})"))})";
    }

    private static string BuildSingle(DataColumn column, string expression)
    {
        var filterOperator = Operators.FirstOrDefault(expression.StartsWith) ?? string.Empty;
        var rawOperand = filterOperator.Length == 0
            ? expression
            : expression[filterOperator.Length..].Trim();
        var operandIsQuoted = IsQuoted(rawOperand);
        var operand = Unquote(rawOperand);

        var columnReference = $"[{column.ColumnName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal)}]";

        if (filterOperator.Length == 0)
        {
            return Contains(columnReference, operand, negate: false);
        }

        if (operand.Length == 0)
        {
            return filterOperator switch
            {
                "=" or "==" or "!" => IsEmpty(columnReference),
                "!=" => IsNotEmpty(columnReference),
                _ => throw new FormatException("Після оператора потрібно вказати значення.")
            };
        }

        if (filterOperator == "!")
        {
            return Contains(columnReference, operand, negate: true);
        }

        if (!operandIsQuoted && IsNullKeyword(operand) && filterOperator is "=" or "==" or "!=")
        {
            return filterOperator == "!="
                ? IsNotEmpty(columnReference)
                : IsEmpty(columnReference);
        }

        var normalizedOperator = filterOperator switch
        {
            "==" => "=",
            "!=" => "<>",
            _ => filterOperator
        };

        return BuildTypedComparison(column, columnReference, normalizedOperator, operand);
    }

    private static IReadOnlyList<string> SplitOrConditions(string expression)
    {
        var conditions = new List<string>();
        var start = 0;
        char? quote = null;

        for (var index = 0; index < expression.Length; index++)
        {
            var current = expression[index];
            if (current is '\'' or '"')
            {
                if (quote == current)
                {
                    quote = null;
                }
                else if (quote is null)
                {
                    quote = current;
                }

                continue;
            }

            if (quote is not null || index == 0 || index + 2 >= expression.Length)
            {
                continue;
            }

            if (!char.IsWhiteSpace(expression[index - 1]) ||
                !expression.AsSpan(index, 2).Equals("or".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                !char.IsWhiteSpace(expression[index + 2]))
            {
                continue;
            }

            var condition = expression[start..index].Trim();
            if (condition.Length == 0)
            {
                throw new FormatException("Перед OR потрібно вказати умову.");
            }

            conditions.Add(condition);
            index += 2;
            while (index < expression.Length && char.IsWhiteSpace(expression[index]))
            {
                index++;
            }

            start = index;
            index--;
        }

        var lastCondition = expression[start..].Trim();
        if (lastCondition.Length == 0)
        {
            throw new FormatException("Після OR потрібно вказати умову.");
        }

        conditions.Add(lastCondition);
        return conditions;
    }

    private static string IsEmpty(string columnReference) =>
        $"({columnReference} IS NULL OR Convert({columnReference}, 'System.String') = '')";

    private static string IsNotEmpty(string columnReference) =>
        $"({columnReference} IS NOT NULL AND Convert({columnReference}, 'System.String') <> '')";

    private static string BuildTypedComparison(
        DataColumn column,
        string columnReference,
        string filterOperator,
        string operand)
    {
        // CSV stays text to preserve identifiers such as 000123. On a numeric
        // text column, comparison still uses numbers rather than lexical order.
        if (column.DataType == typeof(string) && filterOperator is ">" or "<" or ">=" or "<=" &&
            decimal.TryParse(operand, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericOperand) &&
            column.Table is not null && column.Table.Rows.Cast<DataRow>().Where(r => !r.IsNull(column) && r[column].ToString()!.Length > 0)
                .All(r => decimal.TryParse(r[column].ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
        {
            var numberExpression = $"IIF({columnReference} IS NULL OR {columnReference} = '', 0, Convert({columnReference}, 'System.Decimal'))";
            return $"({IsNotEmpty(columnReference)} AND {numberExpression} {filterOperator} {numericOperand.ToString(CultureInfo.InvariantCulture)})";
        }
        if (column.DataType == typeof(decimal))
        {
            if (!decimal.TryParse(operand, NumberStyles.Any, CultureInfo.CurrentCulture, out var value) &&
                !decimal.TryParse(operand, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                throw new FormatException($"«{operand}» не є числом.");
            }

            return $"{columnReference} {filterOperator} {value.ToString(CultureInfo.InvariantCulture)}";
        }

        if (column.DataType == typeof(DateTime))
        {
            if (!DateTime.TryParse(operand, CultureInfo.CurrentCulture, DateTimeStyles.None, out var value))
            {
                throw new FormatException($"«{operand}» не є датою.");
            }

            return $"{columnReference} {filterOperator} #{value:MM/dd/yyyy HH:mm:ss}#";
        }

        if (column.DataType == typeof(bool))
        {
            if (filterOperator is not "=" and not "<>")
            {
                throw new FormatException("Для логічного поля доступні лише == та !=.");
            }

            var value = ParseBoolean(operand);
            return $"{columnReference} {filterOperator} {value.ToString().ToLowerInvariant()}";
        }

        return $"Convert({columnReference}, 'System.String') {filterOperator} '{EscapeLiteral(operand)}'";
    }

    private static string Contains(string columnReference, string operand, bool negate)
    {
        if (operand.Length == 0)
        {
            throw new FormatException("Вкажіть текст для пошуку.");
        }

        var pattern = $"%{EscapeLikeValue(operand)}%";
        return negate
            ? $"({columnReference} IS NULL OR Convert({columnReference}, 'System.String') NOT LIKE '{pattern}')"
            : $"Convert({columnReference}, 'System.String') LIKE '{pattern}'";
    }

    private static bool ParseBoolean(string operand)
    {
        return operand.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "так" => true,
            "false" or "0" or "no" or "ні" => false,
            _ => throw new FormatException($"«{operand}» не є логічним значенням.")
        };
    }

    private static bool IsNullKeyword(string operand) => operand.Equals("null", StringComparison.OrdinalIgnoreCase)
        || operand.Equals("empty", StringComparison.OrdinalIgnoreCase)
        || operand.Equals("пусто", StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string value)
    {
        if (IsQuoted(value))
        {
            return value[1..^1];
        }

        return value;
    }

    private static bool IsQuoted(string value) => value.Length >= 2 &&
        ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''));

    private static string EscapeLikeValue(string value) => EscapeLiteral(value)
        .Replace("[", "[[]", StringComparison.Ordinal)
        .Replace("%", "[%]", StringComparison.Ordinal)
        .Replace("*", "[*]", StringComparison.Ordinal);

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
