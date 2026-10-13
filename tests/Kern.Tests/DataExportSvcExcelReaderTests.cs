using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ShouTech.App.Services;
using Xunit;

namespace Kern.Tests;

public sealed class DataExportSvcExcelReaderTests
{
    [Fact]
    public void ReadExcelSheet_ResolvesWorksheetRelationshipAndParsesCellTypes()
    {
        var path = CreateWorkbook(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="s"><v>0</v></c>
                  <c r="B1" t="inlineStr"><is><t>name</t></is></c>
                  <c r="C1" t="s"><v>2</v></c>
                  <c r="D1" t="s"><v>3</v></c>
                </row>
                <row r="2">
                  <c r="A2" t="s"><v>1</v></c>
                  <c r="B2" t="inlineStr"><is><t>Product A</t></is></c>
                  <c r="C2" t="b"><v>1</v></c>
                  <c r="D2" s="1"><v>45292</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        try
        {
            var sheet = ReadSheet(path);
            var headers = (IReadOnlyList<string>)GetProperty(sheet, "Headers");
            var rows = (IReadOnlyList<IReadOnlyList<string?>>)GetProperty(sheet, "Rows");

            Assert.Equal(new[] { "code", "name", "active", "date" }, headers);
            Assert.Equal("001", rows[0][0]);
            Assert.Equal("Product A", rows[0][1]);
            Assert.Equal("true", rows[0][2]);
            Assert.Equal("2024-01-01T00:00:00.0000000", rows[0][3]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadExcelSheet_TrimsTrailingBlankHeaderCells()
    {
        var path = CreateWorkbook(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="s"><v>0</v></c>
                  <c r="B1" t="inlineStr"><is><t></t></is></c>
                </row>
                <row r="2"><c r="A2" t="s"><v>1</v></c></row>
              </sheetData>
            </worksheet>
            """);

        try
        {
            var sheet = ReadSheet(path);
            var headers = (IReadOnlyList<string>)GetProperty(sheet, "Headers");

            Assert.Equal(new[] { "code" }, headers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadExcelSheet_RejectsMalformedBooleanCells()
    {
        var path = CreateWorkbook(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1">
                  <c r="A1" t="s"><v>0</v></c>
                  <c r="B1" t="s"><v>2</v></c>
                </row>
                <row r="2">
                  <c r="A2" t="s"><v>1</v></c>
                  <c r="B2" t="b"><v>2</v></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        try
        {
            var exception = Assert.Throws<InvalidDataException>(() => ReadSheet(path));
            Assert.Contains("invalid boolean value", exception.Message.ToLowerInvariant());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadExcelSheet_RejectsExcelFormulaErrorCells()
    {
        var path = CreateWorkbook(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
                <row r="2"><c r="A2" t="e"><v>#VALUE!</v></c></row>
              </sheetData>
            </worksheet>
            """);

        try
        {
            var exception = Assert.Throws<InvalidDataException>(() => ReadSheet(path));
            Assert.Contains("formula error", exception.Message.ToLowerInvariant());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReplaceImport_RejectsWorkbookWithHeadersButNoDataRows()
    {
        var path = CreateWorkbook(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
              </sheetData>
            </worksheet>
            """);

        try
        {
            var sheet = ReadSheet(path);
            var method = typeof(DataExportSvc).GetMethod(
                "ValidateReplaceImportHasData",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            var exception = Assert.Throws<TargetInvocationException>(
                () => method!.Invoke(null, new[] { sheet, (object)true }));
            Assert.IsType<InvalidDataException>(exception.InnerException);
            Assert.Contains("empty workbook", exception.InnerException!.Message.ToLowerInvariant());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadExcelSheet_RejectsFormulaCellsEvenWhenCachedValuesExist()
    {
        var path = CreateWorkbook(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
                <row r="2"><c r="A2"><f>1+1</f><v>2</v></c></row>
              </sheetData>
            </worksheet>
            """);

        try
        {
            var exception = Assert.Throws<InvalidDataException>(() => ReadSheet(path));
            Assert.Contains("formula cells are not supported", exception.Message.ToLowerInvariant());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadExcelSheet_RejectsDataBeyondHeaderColumns()
    {
        var path = CreateWorkbook(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c></row>
                <row r="2">
                  <c r="A2" t="s"><v>1</v></c>
                  <c r="B2" t="inlineStr"><is><t>unmapped hidden value</t></is></c>
                </row>
              </sheetData>
            </worksheet>
            """);

        try
        {
            var exception = Assert.Throws<InvalidDataException>(() => ReadSheet(path));
            Assert.Contains("beyond the last header column", exception.Message.ToLowerInvariant());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportKeySelection_UsesCompanyCodeUniqueKeyWhenGeneratedIdIsAbsent()
    {
        var method = typeof(DataExportSvc).GetMethod(
            "SelectUsableImportKeyColumns",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var primaryKeys = new[] { "material_id" };
        var uniqueKeys = new List<List<string>>
        {
            new() { "company_id", "code" }
        };
        var fields = new HashSet<string>(new[] { "company_id", "code", "name_ar" }, StringComparer.OrdinalIgnoreCase);

        var result = method!.Invoke(null, new object[] { primaryKeys, uniqueKeys, fields });

        Assert.Equal(new[] { "company_id", "code" }, Assert.IsType<List<string>>(result));
    }

    [Fact]
    public void GenericImportFieldAliases_MapMaterialNameToNameAr()
    {
        var method = typeof(DataExportSvc).GetMethod(
            "ResolveImportFieldAlias",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var columns = new[] { "material_id", "company_id", "code", "name_ar", "name_en" };

        Assert.Equal("name_ar", method!.Invoke(null, new object[] { "materials", "name", columns }));
        Assert.Equal("name_ar", method.Invoke(null, new object[] { "materials", "الاسم", columns }));
        Assert.Null(method.Invoke(null, new object[] { "materials", "unmapped", columns }));
    }

    [Fact]
    public void ExcelDateFormatDetection_IgnoresColorsAndLiteralsButRecognizesDatesAndElapsedTime()
    {
        var method = typeof(DataExportSvc).GetMethod(
            "IsDateNumberFormat",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(null, new object[] { "yyyy-mm-dd" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "[h]:mm:ss" })!);
        Assert.False((bool)method.Invoke(null, new object[] { "[Red]0.00" })!);
        Assert.False((bool)method.Invoke(null, new object[] { "0.00 \"items\"" })!);
    }

    [Fact]
    public void CompanyScopePolicy_UsesCompanyCodesOnlyForExplicitLegacyTables()
    {
        var method = typeof(DataExportSvc).GetMethod(
            "IsCompanyCodeScopedTable",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(null, new object[] { "materials" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "customers" })!);
        Assert.False((bool)method.Invoke(null, new object[] { "company_roles" })!);
    }

    [Fact]
    public void PostgresPermissionMigration_ContainsPermissionUsedByApplication()
    {
        var root = FindRepositoryRoot();
        var migration = Path.Combine(root, "dba", "mig", "postgresql", "007_permission_manage.sql");

        Assert.True(File.Exists(migration), $"Expected migration file was not found: {migration}");
        Assert.Contains("SYS.PERMISSIONS.MANAGE", File.ReadAllText(migration));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "dba", "mig", "postgresql")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    [Fact]
    public void GenericImportTypeInference_RecognizesIntegerBackedBooleanColumns()
    {
        var method = typeof(DataExportSvc).GetMethod(
            "NormalizeDatabaseImportType",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var result = method!.Invoke(null, new object[] { "is_active", "INTEGER" });

        Assert.Equal("Boolean", result);
    }

    [Fact]
    public void SpreadsheetImportPolicy_RejectsTransactionalDatasets()
    {
        var method = typeof(DataExportSvc).GetMethod(
            "IsMasterDataImportAllowed",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var masterData = new DataExportOption("Products", "Products", "materials");
        var sales = new DataExportOption("Sales", "Sales invoices", "sales_invoices", DataExportFilter.Sales);

        Assert.True((bool)method!.Invoke(null, new object[] { masterData })!);
        Assert.False((bool)method.Invoke(null, new object[] { sales })!);
    }

    [Fact]
    public async Task SqliteMigrationChain_AppliesToAnEmptyDatabaseInOrder()
    {
        var root = FindRepositoryRoot();
        var migrationDirectory = Path.Combine(root, "dba", "mig", "sqlite");
        var migrationFiles = Directory.GetFiles(migrationDirectory, "*.sql", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(migrationDirectory, path).Replace('\\', '/'),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(migrationFiles);

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync();
        }

        foreach (var path in migrationFiles)
        {
            await using var transaction = connection.BeginTransaction();
            try
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = 120;
                command.CommandText = await File.ReadAllTextAsync(path);
                await command.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"SQLite migration '{Path.GetRelativePath(migrationDirectory, path)}' failed on an empty database: {ex.Message}",
                    ex);
            }
        }

        foreach (var table in new[] { "companies", "permissions", "role_permissions", "journal_entries", "chart_of_accounts", "outbound_messages" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
            command.Parameters.AddWithValue("@name", table);
            Assert.Equal(1L, (long)(await command.ExecuteScalarAsync() ?? 0L));
        }
    }

    [Theory]
    [InlineData(true, 0L)]
    [InlineData(false, 1L)]
    public async Task ImportTransactionCompletion_PreviewRollsBackAndConfirmedImportCommits(
        bool previewOnly,
        long expectedRowCount)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE preview_probe (id INTEGER PRIMARY KEY);";
            await setup.ExecuteNonQueryAsync();
        }

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = "INSERT INTO preview_probe (id) VALUES (1);";
            await insert.ExecuteNonQueryAsync();

            var method = typeof(DataExportSvc).GetMethod(
                "CompleteImportTransactionAsync",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var completion = method!.Invoke(null, new object[] { transaction, previewOnly, CancellationToken.None });
            Assert.IsAssignableFrom<Task>(completion);
            await (Task)completion!;
        }

        await using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM preview_probe;";
        Assert.Equal(expectedRowCount, (long)(await verify.ExecuteScalarAsync() ?? 0L));
    }

    private static object ReadSheet(string path)
    {
        var method = typeof(DataExportSvc).GetMethod(
            "ReadExcelSheet",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        try
        {
            return method!.Invoke(null, new object[] { path })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static object GetProperty(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return property!.GetValue(instance)!;
    }

    private static string CreateWorkbook(string worksheetXml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"shoutech-{Guid.NewGuid():N}.xlsx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="Data" sheetId="1" r:id="rId7"/></sheets>
            </workbook>
            """);
        WriteEntry(archive, "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId7"
                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                Target="worksheets/sheet3.xml"/>
            </Relationships>
            """);
        WriteEntry(archive, "xl/sharedStrings.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="4" uniqueCount="4">
              <si><t>code</t></si>
              <si><t>001</t></si>
              <si><t>active</t></si>
              <si><t>date</t></si>
            </sst>
            """);
        WriteEntry(archive, "xl/styles.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="0"/>
              <cellXfs count="2">
                <xf numFmtId="0" applyNumberFormat="0"/>
                <xf numFmtId="14" applyNumberFormat="1"/>
              </cellXfs>
            </styleSheet>
            """);
        WriteEntry(archive, "xl/worksheets/sheet3.xml", worksheetXml);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
