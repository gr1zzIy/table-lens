using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DotNetDBF;
using TableLens.Core;
using TableLens.Core.Dbf;
using TableLens.Core.Editing;
using TableLens.Core.Filtering;
using TableLens.Core.Models;
using Xunit;

namespace TableLens.Tests;

public sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tablelens-tests-" + Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Path);
    public string File(string name, string content) { var path = System.IO.Path.Combine(Path, name); System.IO.File.WriteAllText(path, content, new UTF8Encoding(false)); return path; }
    public void Dispose() => Directory.Delete(Path, true);
}

public sealed class FileTests
{
    private readonly TableFileService _files = new();

    [Fact]
    public void CsvQuotesNewlinesUnicodeLeadingZerosAndDelimiterSurviveSave()
    {
        using var temp = new TestDirectory();
        var path = temp.File("sample.csv", "CODE;NAME;NOTE\n000123;Олена;\"first; line\nsecond \"\"quoted\"\" line\"\n000124;Тарас;\n");
        var doc = _files.Read(path, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(";", doc.Options.Delimiter);
        Assert.Equal("000123", doc.Table.Rows[0][0]);
        Assert.Equal("first; line\nsecond \"quoted\" line", doc.Table.Rows[0][2]);
        doc.Table.Rows[1][1] = "Юлія";
        var result = _files.Save(doc, token: TestContext.Current.CancellationToken);
        Assert.True(System.IO.File.Exists(result.BackupPath));
        var read = _files.Read(path, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("000123", read.Table.Rows[0][0]);
        Assert.Equal("Юлія", read.Table.Rows[1][1]);
        Assert.Equal(doc.Table.Rows[0][2], read.Table.Rows[0][2]);
    }

    [Fact]
    public void CsvBomSelectsTheActualEncodingAndKeepsUnicodeOnSave()
    {
        using var temp = new TestDirectory();
        var path = System.IO.Path.Combine(temp.Path, "utf16.csv");
        System.IO.File.WriteAllText(path, "ID,NAME\n001,Олена\n", Encoding.Unicode);
        var doc = _files.Read(path, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1200, doc.Encoding.CodePage);
        Assert.Equal("Олена", doc.Table.Rows[0][1]);
        _files.Save(doc, token: TestContext.Current.CancellationToken);
        Assert.Equal("Олена", _files.Read(path, cancellationToken: TestContext.Current.CancellationToken).Table.Rows[0][1]);
    }

    [Fact]
    public void CancelledReadOfAnEmptyJsonArrayStillThrows()
    {
        using var temp = new TestDirectory();
        using var token = new CancellationTokenSource(); token.Cancel();
        Assert.Throws<OperationCanceledException>(() => _files.Read(temp.File("empty.json", "[]"), cancellationToken: token.Token));
    }

    [Fact]
    public void HeaderlessTsvKeepsFirstRecordAndNoHeaderOnSave()
    {
        using var temp = new TestDirectory();
        var path = temp.File("no-header.tsv", "001\tОлена\n002\tAndrii\n");
        var doc = _files.Read(path, new() { HasHeader = false }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, doc.Table.Rows.Count);
        Assert.Equal("Column1", doc.Table.Columns[0].ColumnName);
        _files.Save(doc, token: TestContext.Current.CancellationToken);
        Assert.StartsWith("001\t", System.IO.File.ReadAllText(path));
        Assert.Equal(2, _files.Read(path, new() { HasHeader = false }, cancellationToken: TestContext.Current.CancellationToken).Table.Rows.Count);
    }

    [Fact]
    public void MalformedCsvFailsWithoutModifyingTheOriginal()
    {
        using var temp = new TestDirectory();
        var path = temp.File("bad.csv", "id,name\n1,Alice,extra\n");
        var original = System.IO.File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(() => _files.Read(path, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(original, System.IO.File.ReadAllBytes(path));
    }

    [Fact]
    public void DuplicateHeadersRemainReadOnlyAndCanBeExported()
    {
        using var temp = new TestDirectory();
        var doc = _files.Read(temp.File("duplicate.csv", "id,id\n1,2\n"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(doc.CanEdit);
        Assert.Throws<InvalidOperationException>(() => _files.Save(doc, token: TestContext.Current.CancellationToken));
        var output = System.IO.Path.Combine(temp.Path, "export.csv");
        _files.Export(doc, output, TableFormat.Csv, token: TestContext.Current.CancellationToken);
        Assert.True(_files.Read(output, cancellationToken: TestContext.Current.CancellationToken).CanEdit);
    }

    [Fact]
    public void WrappedJsonPreservesMetadataTypesNestingMissingAndNullProperties()
    {
        using var temp = new TestDirectory();
        var path = temp.File("records.json", """{"meta":{"source":"example","version":2},"records":[{"id":1,"name":"A","owner":{"name":"Олена"},"tags":["one"],"nullable":null},{"id":2,"name":"B","owner":null,"tags":[]}]}""");
        var doc = _files.Read(path, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(JsonLayout.WrappedArray, doc.JsonLayout);
        Assert.Equal(typeof(decimal), doc.Table.Columns["id"]!.DataType);
        doc.Table.Rows[0]["name"] = "Updated";
        _files.Save(doc, token: TestContext.Current.CancellationToken);
        using var read = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal(2, read.RootElement.GetProperty("meta").GetProperty("version").GetInt32());
        var rows = read.RootElement.GetProperty("records");
        Assert.Equal("Updated", rows[0].GetProperty("name").GetString());
        Assert.Equal("Олена", rows[0].GetProperty("owner").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("nullable").ValueKind);
        Assert.False(rows[1].TryGetProperty("nullable", out _));
        var filtered = System.IO.Path.Combine(temp.Path, "filtered.json");
        _files.ExportRows(doc, filtered, TableFormat.Json, [doc.Table.Rows[1]], token: TestContext.Current.CancellationToken);
        using var export = JsonDocument.Parse(System.IO.File.ReadAllText(filtered));
        Assert.Equal(1, export.RootElement.GetProperty("records").GetArrayLength());
        Assert.False(export.RootElement.GetProperty("records")[0].TryGetProperty("nullable", out _));
    }

    [Fact]
    public void MixedJsonAndEmptyOrCaseSensitiveNamesRoundTrip()
    {
        using var temp = new TestDirectory();
        var path = temp.File("mixed.json", """[{"":1,"a":true,"A":"0001","mixed":1e100},{"":2,"a":false,"A":"0002","mixed":"1"}]""");
        var doc = _files.Read(path, cancellationToken: TestContext.Current.CancellationToken);
        _files.Save(doc, token: TestContext.Current.CancellationToken);
        using var read = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal(1, read.RootElement[0].GetProperty("").GetInt32());
        Assert.True(read.RootElement[0].GetProperty("a").GetBoolean());
        Assert.Equal("0001", read.RootElement[0].GetProperty("A").GetString());
        Assert.Equal(JsonValueKind.Number, read.RootElement[0].GetProperty("mixed").ValueKind);
        Assert.Equal(JsonValueKind.String, read.RootElement[1].GetProperty("mixed").ValueKind);
    }

    [Fact]
    public void JsonNumbersOutsideExactDecimalPrecisionKeepTheirOriginalValue()
    {
        using var temp = new TestDirectory();
        var path = temp.File("precision.json", """[{"tiny":1e-100,"precise":0.123456789012345678901234567890123456789,"normal":1.2300}]""");
        var doc = _files.Read(path, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(typeof(object), doc.Table.Columns["tiny"]!.DataType);
        Assert.Equal(typeof(object), doc.Table.Columns["precise"]!.DataType);
        Assert.Equal(typeof(decimal), doc.Table.Columns["normal"]!.DataType);
        _files.Save(doc, token: TestContext.Current.CancellationToken);
        using var read = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal("1e-100", read.RootElement[0].GetProperty("tiny").GetRawText());
        Assert.Equal("0.123456789012345678901234567890123456789", read.RootElement[0].GetProperty("precise").GetRawText());
        var changed = TableDocument.ParseCell("2e-100", doc.Table.Columns["tiny"]!, doc.Table.Rows[0]["tiny"]);
        Assert.Equal(JsonValueKind.Number, ((JsonElement)changed).ValueKind);
    }

    [Fact]
    public void SingleObjectJsonRetainsItsShape()
    {
        using var temp = new TestDirectory();
        var path = temp.File("object.json", """{"id":1,"name":"A","nested":{"enabled":true}}""");
        var doc = _files.Read(path, cancellationToken: TestContext.Current.CancellationToken);
        doc.Table.Rows[0]["name"] = "B";
        _files.Save(doc, token: TestContext.Current.CancellationToken);
        using var read = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal(JsonValueKind.Object, read.RootElement.ValueKind);
        Assert.Equal("B", read.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void ExplicitJsonArrayPropertyResolvesMultipleArrays()
    {
        using var temp = new TestDirectory();
        var path = temp.File("multi.json", """{"metadata":[{"key":"value"}],"data":[{"id":1},{"id":2}]}""");
        var doc = _files.Read(path, new() { JsonArrayProperty = "data" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, doc.Table.Rows.Count);
        _files.Save(doc, token: TestContext.Current.CancellationToken);
        using var read = JsonDocument.Parse(System.IO.File.ReadAllText(path));
        Assert.Equal(1, read.RootElement.GetProperty("metadata").GetArrayLength());
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[1,2,3]")]
    [InlineData("[{\"id\":1,\"id\":2}]")]
    public void UnsupportedOrMalformedJsonFailsSafely(string content)
    {
        using var temp = new TestDirectory();
        var path = temp.File("bad.json", content);
        Assert.ThrowsAny<Exception>(() => _files.Read(path, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(content, System.IO.File.ReadAllText(path));
    }

    [Fact]
    public void ExternalChangesBlockSaveEvenWhenLengthAndTimestampAreUnchanged()
    {
        using var temp = new TestDirectory();
        var path = temp.File("records.csv", "ID,NAME\n1,Alice\n");
        var doc = _files.Read(path, cancellationToken: TestContext.Current.CancellationToken);
        var timestamp = System.IO.File.GetLastWriteTimeUtc(path);
        System.IO.File.WriteAllText(path, "ID,NAME\n1,Bobby\n");
        System.IO.File.SetLastWriteTimeUtc(path, timestamp);
        Assert.Throws<IOException>(() => _files.Save(doc, token: TestContext.Current.CancellationToken));
        Assert.Contains("Bobby", System.IO.File.ReadAllText(path));
    }

    [Fact]
    public void ExportRefusesToOverwriteTheOpenSource()
    {
        using var temp = new TestDirectory();
        var path = temp.File("records.csv", "ID\n1\n");
        var doc = _files.Read(path, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Throws<IOException>(() => _files.Export(doc, path, TableFormat.Csv, token: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DbfUnicodeNumericDateAndLogicalFieldsRoundTripWithBackup()
    {
        using var temp = new TestDirectory();
        var path = System.IO.Path.Combine(temp.Path, "sample.dbf");
        var doc = DbfDocuments.Create(path, [new("NAME", NativeDbType.Char, 40, 0, null), new("AMOUNT", NativeDbType.Numeric, 12, 2, null),
            new("DATE", NativeDbType.Date, 8, 0, null), new("ACTIVE", NativeDbType.Logical, 1, 0, null)], 1251);
        doc.Table.Rows.Add("Олена", 1234.56m, new DateTime(2026, 9, 1), true);
        _files.Save(doc, token: TestContext.Current.CancellationToken);
        var read = _files.Read(path, new() { DbfCodePage = 1251 }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Олена", read.Table.Rows[0][0]);
        Assert.Equal(1234.56m, read.Table.Rows[0][1]);
        Assert.Equal(true, read.Table.Rows[0][3]);
        read.Table.Rows[0][1] = 12.34m;
        var result = _files.Save(read, token: TestContext.Current.CancellationToken);
        Assert.True(System.IO.File.Exists(result.BackupPath));
        Assert.Equal(12.34m, _files.Read(path, new() { DbfCodePage = 1251 }, cancellationToken: TestContext.Current.CancellationToken).Table.Rows[0][1]);
    }

    [Fact]
    public void DbfStaleHeaderCountDoesNotHidePhysicalRecords()
    {
        using var temp = new TestDirectory();
        var path = System.IO.Path.Combine(temp.Path, "sample.dbf");
        var doc = DbfDocuments.Create(path, [new("ID", NativeDbType.Numeric, 6, 0, null)], 866);
        doc.Table.Rows.Add(1m); doc.Table.Rows.Add(2m); _files.Save(doc, token: TestContext.Current.CancellationToken);
        using (var stream = System.IO.File.OpenWrite(path)) { stream.Position = 4; stream.Write(BitConverter.GetBytes(0)); }
        var read = _files.Read(path, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, read.Table.Rows.Count);
        Assert.Equal(0, read.Dbf!.DeclaredRecordCount);
    }

    [Fact]
    public void DbfInvalidNumericValueReturnsReadOnlyPartialData()
    {
        using var temp = new TestDirectory();
        var path = System.IO.Path.Combine(temp.Path, "sample.dbf");
        var doc = DbfDocuments.Create(path, [new("ID", NativeDbType.Numeric, 6, 0, null)], 866);
        doc.Table.Rows.Add(1m); doc.Table.Rows.Add(2m); _files.Save(doc, token: TestContext.Current.CancellationToken);
        using (var stream = System.IO.File.Open(path, FileMode.Open, FileAccess.ReadWrite))
        {
            var bytes = new byte[12]; stream.ReadExactly(bytes); var header = BitConverter.ToUInt16(bytes, 8); var record = BitConverter.ToUInt16(bytes, 10);
            stream.Position = header + record + 1; stream.Write(Encoding.ASCII.GetBytes("******"));
        }
        var read = _files.Read(path, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(read.CanEdit);
        Assert.Single(read.Table.Rows.Cast<DataRow>());
        Assert.Throws<InvalidOperationException>(() => _files.Save(read, token: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DbfOverflowAndUnrepresentableTextNeverTruncateTheOriginal()
    {
        using var temp = new TestDirectory();
        var path = System.IO.Path.Combine(temp.Path, "sample.dbf");
        var doc = DbfDocuments.Create(path, [new("NAME", NativeDbType.Char, 4, 0, null)], 1251);
        doc.Table.Rows.Add("Test"); _files.Save(doc, token: TestContext.Current.CancellationToken); var original = System.IO.File.ReadAllBytes(path);
        doc.Table.Rows[0][0] = "Too long";
        Assert.Throws<InvalidDataException>(() => _files.Save(doc, token: TestContext.Current.CancellationToken));
        Assert.Equal(original, System.IO.File.ReadAllBytes(path));
        doc.Table.Rows[0][0] = "😀";
        Assert.Throws<EncoderFallbackException>(() => _files.Save(doc, token: TestContext.Current.CancellationToken));
        Assert.Equal(original, System.IO.File.ReadAllBytes(path));
    }

    [Fact]
    public void ReadCancellationIsNeverReportedAsSuccess()
    {
        using var temp = new TestDirectory();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var path = temp.File("sample.csv", "ID\n1\n2\n");
        Assert.Throws<OperationCanceledException>(() => _files.Read(path, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void NumericTextFilterComparesNumbersAndPreservesIdentifiers()
    {
        using var temp = new TestDirectory();
        var doc = _files.Read(temp.File("numbers.csv", "CODE,AMOUNT\n001,2\n002,11\n003,\n004,0\n"), cancellationToken: TestContext.Current.CancellationToken);
        var view = new DataView(doc.Table) { RowFilter = ColumnFilterParser.Build(doc.Table.Columns["AMOUNT"]!, ">10") };
        Assert.Single(view.Cast<DataRowView>());
        Assert.Equal("002", view[0]["CODE"]);
        view.RowFilter = ColumnFilterParser.Build(doc.Table.Columns["AMOUNT"]!, "<10");
        Assert.Equal(2, view.Count);
        view.RowFilter = ColumnFilterParser.Build(doc.Table.Columns["AMOUNT"]!, "!=");
        Assert.Equal(3, view.Count);
    }

    [Theory]
    [InlineData("!=", 3)]
    [InlineData("==null", 2)]
    [InlineData("==\"null\"", 1)]
    [InlineData("==A or ==B", 2)]
    [InlineData("!A", 4)]
    public void FiltersHandleNullLiteralsNegationAndOr(string expression, int count)
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture }; table.Columns.Add("VALUE", typeof(string));
        foreach (var value in new object[] { DBNull.Value, "", "A", "B", "null" }) table.Rows.Add(value);
        Assert.Equal(count, new DataView(table) { RowFilter = ColumnFilterParser.Build(table.Columns[0], expression) }.Count);
    }

    [Fact]
    public void FilterEscapesSpecialCharactersInColumnNamesAndText()
    {
        var table = new DataTable(); table.Columns.Add("a]b\\c", typeof(string)); table.Rows.Add("O'Brien [%]*"); table.Rows.Add("other");
        var view = new DataView(table) { RowFilter = ColumnFilterParser.Build(table.Columns[0], "O'Brien [%]*") };
        Assert.Single(view.Cast<DataRowView>());
    }

    [Fact]
    public void HistoryRestoresDeletedAndAddedRowsAndDropsTheRedoBranch()
    {
        var table = new DataTable(); table.Columns.Add("ID", typeof(decimal)); table.Rows.Add(1m); table.Rows.Add(2m); table.AcceptChanges();
        var history = new EditHistoryManager();
        history.Execute(new DeleteRowsEditAction(table, [table.Rows[0]])); Assert.Equal(2m, table.Rows[0][0]);
        history.Undo(); Assert.Equal(1m, table.Rows[0][0]);
        history.Redo(); Assert.Single(table.Rows.Cast<DataRow>()); history.Undo();
        var row = table.NewRow(); row[0] = 3m;
        history.Execute(new AddRowEditAction(table, row, 2)); Assert.False(history.CanRedo);
        history.Undo(); Assert.Equal(2, table.Rows.Count); history.Redo(); Assert.Equal(3m, table.Rows[2][0]);
    }

    [Fact]
    public void ProjectsArePortableAndImportLegacyWinFormsFileLists()
    {
        using var temp = new TestDirectory();
        var source = temp.File("table.csv", "ID\n1\n");
        var projectPath = System.IO.Path.Combine(temp.Path, "project.tablelens.json");
        var store = new WorkspaceStore(temp.Path);
        store.SaveProject(projectPath, new() { Name = "Test", Files = [new(source, new() { Delimiter = ";" })] });
        Assert.DoesNotContain(temp.Path.Replace("\\", "\\\\"), System.IO.File.ReadAllText(projectPath));
        var project = store.LoadProject(projectPath);
        Assert.Equal(source, project.Files[0].Path); Assert.Equal(";", project.Files[0].Options.Delimiter);
        var legacyPath = temp.File("legacy.json", JsonSerializer.Serialize(new { Name = "Legacy", FilePaths = new[] { source } }));
        Assert.Equal(source, store.LoadProject(legacyPath).Files[0].Path);
    }

    [Fact]
    public void RecursiveDiscoveryIsCaseAwareAndIncludesAllFormats()
    {
        using var temp = new TestDirectory();
        var child = System.IO.Path.Combine(temp.Path, "nested"); Directory.CreateDirectory(child);
        temp.File("root.csv", "id\n1\n"); System.IO.File.WriteAllText(System.IO.Path.Combine(child, "UPPER.JSON"), "[]"); temp.File("notes.txt", "ignore");
        Assert.Equal(2, AppPaths.Discover([temp.Path]).Count());
    }

    [Fact]
    public void SchemaTransformsRenameFieldsWithoutDroppingRows()
    {
        using var temp = new TestDirectory();
        var doc = DbfDocuments.Create(System.IO.Path.Combine(temp.Path, "schema.dbf"), [new("NAME", NativeDbType.Char, 40, 0, null)], 866);
        doc.Table.Rows.Add("Test");
        var schema = new DbfSchemaService().Transform(doc.Dbf!, [new("TITLE", NativeDbType.Char, 40, 0, "NAME"), new("COUNT", NativeDbType.Numeric, 8, 0, null)], doc.Encoding);
        Assert.Equal("Test", schema.Table.Rows[0]["TITLE"]);
        Assert.True(schema.Table.Rows[0].IsNull("COUNT"));
    }
}
