using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
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
