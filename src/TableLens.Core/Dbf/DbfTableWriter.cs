using System.Data;
using System.Globalization;
using System.Text;
using TableLens.Core.Models;
using DotNetDBF;

namespace TableLens.Core.Dbf;

public sealed class DbfTableWriter
{
    private readonly DbfTableReader _reader = new();

    public DbfSaveResult Save(
        DbfTableData tableData,
        Encoding encoding,
        string destinationPath,
        bool createBackup)
    {
        ArgumentNullException.ThrowIfNull(tableData);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestinationPath) ??
                                   throw new IOException("Не вдалося визначити папку призначення.");
        Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            ValidateTable(tableData, encoding);
            WriteTemporaryFile(tableData, encoding, temporaryPath);
            ValidateTemporaryFile(temporaryPath, encoding, tableData.Table.Rows.Count);

            string? backupPath = null;
            if (createBackup && File.Exists(fullDestinationPath))
            {
                backupPath = CreateBackupPath(fullDestinationPath);
                File.Replace(temporaryPath, fullDestinationPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, fullDestinationPath, overwrite: true);
            }

            return new DbfSaveResult(fullDestinationPath, backupPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateTable(DbfTableData tableData, Encoding encoding)
    {
        for (var rowIndex = 0; rowIndex < tableData.Table.Rows.Count; rowIndex++)
        {
            var row = tableData.Table.Rows[rowIndex];
            for (var fieldIndex = 0; fieldIndex < tableData.SourceFields.Count; fieldIndex++)
            {
                var value = row[fieldIndex];
                if (value == DBNull.Value)
                {
                    continue;
                }

                var field = tableData.SourceFields[fieldIndex];
                if (field.DataType == NativeDbType.Char)
                {
                    var byteCount = encoding.GetByteCount(Convert.ToString(value) ?? string.Empty);
                    if (byteCount > field.FieldLength)
                    {
                        throw new InvalidDataException(
                            $"Рядок {rowIndex + 1}, поле {field.Name}: значення займає {byteCount} байт, " +
                            $"а довжина поля — {field.FieldLength}.");
                    }
                }
                else if (field.DataType is NativeDbType.Numeric or NativeDbType.Float)
                {
                    var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    if (decimal.Round(number, field.DecimalCount) != number)
                    {
                        throw new InvalidDataException(
                            $"Рядок {rowIndex + 1}, поле {field.Name}: дозволено знаків після коми — " +
                            $"{field.DecimalCount}.");
                    }

                    var formattedNumber = number.ToString($"F{field.DecimalCount}", CultureInfo.InvariantCulture);
                    if (encoding.GetByteCount(formattedNumber) > field.FieldLength)
                    {
                        throw new InvalidDataException(
                            $"Рядок {rowIndex + 1}, поле {field.Name}: число не вміщується у поле " +
                            $"довжиною {field.FieldLength}.");
                    }
                }
            }
        }
    }

    private static void WriteTemporaryFile(DbfTableData tableData, Encoding encoding, string temporaryPath)
    {
        using var stream = File.Open(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var writer = new DBFWriter(stream)
        {
            CharEncoding = encoding,
            Signature = tableData.Signature
        };

        // Match the header to the encoding actually written, including when
        // the user selected a different encoding from the source header.
        writer.LanguageDriver = encoding.CodePage switch
        {
            866 => 0x65,
            1251 => 0xC9,
            1252 => 0x03,
            _ => 0
        };

        writer.Fields = tableData.SourceFields.Select(CloneField).ToArray();

        foreach (DataRow row in tableData.Table.Rows)
        {
            var values = new object[tableData.SourceFields.Count];
            for (var fieldIndex = 0; fieldIndex < values.Length; fieldIndex++)
            {
                var value = row[fieldIndex];
                values[fieldIndex] = value == DBNull.Value ? null! : value;
            }

            writer.WriteRecord(values);
        }
    }

    private void ValidateTemporaryFile(string temporaryPath, Encoding encoding, int expectedRecordCount)
    {
        var validationResult = _reader.Read(temporaryPath, encoding);
        if (validationResult.IsPartialRead ||
            validationResult.ReadWarningCount > 0 ||
            validationResult.LoadedRecordCount != expectedRecordCount)
        {
            throw new IOException(
                $"Перевірка нового DBF не пройдена: очікувалось {expectedRecordCount:N0} записів, " +
                $"прочитано {validationResult.LoadedRecordCount:N0}.");
        }
    }

    private static DBFField CloneField(DBFField source) => new()
    {
        dataType = source.dataType,
        decimalCount = source.decimalCount,
        fieldLength = source.fieldLength,
        fieldName = (byte[])source.fieldName.Clone(),
        indexFieldFlag = source.indexFieldFlag,
        nameNullIndex = source.nameNullIndex,
        reserv1 = source.reserv1,
        reserv2 = source.reserv2,
        reserv3 = source.reserv3,
        reserv4 = (byte[])source.reserv4.Clone(),
        setFieldsFlag = source.setFieldsFlag,
        workAreaId = source.workAreaId
    };

    private static string CreateBackupPath(string filePath)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var candidate = $"{filePath}.{timestamp}.bak";
        return File.Exists(candidate)
            ? $"{filePath}.{timestamp}_{Guid.NewGuid():N}.bak"
            : candidate;
    }
}

public sealed record DbfSaveResult(string FilePath, string? BackupPath);
