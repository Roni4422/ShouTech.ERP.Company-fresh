using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using ShouTech.App;
using ShouTech.App.Reporting.Invoices;
using ShouTech.App.Services;
using Xunit;

namespace Kern.Tests;

public sealed class GlPostingTests
{
    [Theory]
    [InlineData(InvDocKind.SalesInvoice)]
    [InlineData(InvDocKind.PosSale)]
    [InlineData(InvDocKind.SalesReturn)]
    [InlineData(InvDocKind.PurchaseInvoice)]
    [InlineData(InvDocKind.PurchaseReturn)]
    public void BuildLines_ShouldBalanceForEveryPostingKind(InvDocKind kind)
    {
        var lines = GlPost.BuildLines(kind, 100m, 15m, 40m);

        Assert.NotEmpty(lines);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.All(lines, l => Assert.True((l.Debit > 0m) ^ (l.Credit > 0m)));
    }

    [Fact]
    public void BuildLines_SalesInvoice_ShouldDebitReceivablesAndRelieveInventoryAtCost()
    {
        var lines = GlPost.BuildLines(InvDocKind.SalesInvoice, 100m, 15m, 40m);

        Assert.Equal(5, lines.Count);
        Assert.Contains(lines, l => l.AccountKey == GlPost.Receivables && l.Debit == 115m);
        Assert.Contains(lines, l => l.AccountKey == GlPost.SalesRevenue && l.Credit == 100m);
        Assert.Contains(lines, l => l.AccountKey == GlPost.OutputTax && l.Credit == 15m);
        Assert.Contains(lines, l => l.AccountKey == GlPost.CostOfGoodsSold && l.Debit == 40m);
        Assert.Contains(lines, l => l.AccountKey == GlPost.Inventory && l.Credit == 40m);
    }

    [Fact]
    public void BuildLines_PosSale_ShouldDebitCashInsteadOfReceivables()
    {
        var lines = GlPost.BuildLines(InvDocKind.PosSale, 100m, 0m, 0m);

        Assert.Contains(lines, l => l.AccountKey == GlPost.Cash && l.Debit == 100m);
        Assert.DoesNotContain(lines, l => l.AccountKey == GlPost.Receivables);
    }

    [Fact]
    public void BuildLines_PurchaseInvoice_WithoutTax_ShouldOmitTaxLine()
    {
        var lines = GlPost.BuildLines(InvDocKind.PurchaseInvoice, 250m, 0m, 0m);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.AccountKey == GlPost.Inventory && l.Debit == 250m);
        Assert.Contains(lines, l => l.AccountKey == GlPost.Payables && l.Credit == 250m);
    }

    [Fact]
    public void BuildLines_ShouldReturnNothingForNonPostingKindsAndRejectNegativeAmounts()
    {
        Assert.Empty(GlPost.BuildLines(InvDocKind.Quotation, 100m, 0m, 0m));
        Assert.Empty(GlPost.BuildLines(InvDocKind.StockIn, 100m, 0m, 0m));
        Assert.Throws<InvalidOperationException>(() => GlPost.BuildLines(InvDocKind.SalesInvoice, -1m, 0m, 0m));
    }

    [Fact]
    public void PostDocument_ShouldPostBalancedJournalHonourMappingsAndRespectFiscalPeriods()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"shoutech-gl-post-{Guid.NewGuid():N}.sqlite3");
        var oldCompany = AppState.CurrentCompanyCode;
        try
        {
            ApplySqliteMigrations(databasePath);
            AppState.CurrentCompanyCode = "GL-CO";
            using var connection = Open(databasePath);
            Execute(connection, """
                INSERT INTO companies(company_id, company_code, company_name_ar, company_name_en)
                VALUES ('GL-CO', 'GL-CO', 'GL Company', 'GL Company');
                INSERT INTO chart_of_accounts(account_id, company_id, account_code, account_name, account_type)
                VALUES
                  ('A-REC', 'GL-CO', '1100', 'Receivables', 'ASSET'),
                  ('A-INV', 'GL-CO', '1200', 'Inventory', 'ASSET'),
                  ('A-TAX', 'GL-CO', '2100', 'Output tax', 'LIABILITY'),
                  ('A-REV', 'GL-CO', '4000', 'Revenue', 'REVENUE'),
                  ('A-COG', 'GL-CO', '5000', 'Cost of goods sold', 'EXPENSE');
                INSERT INTO fiscal_years(company_id, fiscal_year, database_name, start_date, end_date)
                VALUES ('GL-CO', 2026, '', '2026-01-01', '2026-12-31');
                INSERT INTO fiscal_periods(fiscal_period_id, company_id, fiscal_year_id, period_number, period_name, start_date, end_date)
                SELECT 'P-2026', company_id, fiscal_year_id, 1, '2026', '2026-01-01', '2026-12-31'
                FROM fiscal_years WHERE company_id = 'GL-CO';
                INSERT INTO material_stock_balances(company_id, warehouse_id, material_id, available_qty, average_cost)
                VALUES ('GL-CO', 1, 7, 10, 4);
                INSERT INTO sales_documents(company_id, doc_kind, doc_no, doc_date, exchange_rate)
                VALUES ('GL-CO', 'SAL', 'SAL-2026-0001', '2026-03-10', 1);
                """);

            var sale = NewSale(InvDocKind.SalesInvoice, "SAL-2026-0001", new DateTime(2026, 3, 10));

            // No mappings: legacy behaviour, nothing is posted.
            Assert.Null(Post(connection, sale, 1));
            Assert.Equal(0L, ScalarLong(connection, "SELECT COUNT(*) FROM journal_entries;"));

            // Incomplete mappings: posting is refused and nothing is written.
            Execute(connection, "INSERT INTO account_mappings(company_id, mapping_key, account_id) VALUES ('GL-CO', 'RECEIVABLES', 'A-REC');");
            var incomplete = Assert.Throws<InvalidOperationException>(() => Post(connection, sale, 1));
            Assert.Contains("SALES_REVENUE", incomplete.Message);
            Assert.Equal(0L, ScalarLong(connection, "SELECT COUNT(*) FROM journal_entries;"));

            Execute(connection, """
                INSERT INTO account_mappings(company_id, mapping_key, account_id)
                VALUES
                  ('GL-CO', 'SALES_REVENUE', 'A-REV'),
                  ('GL-CO', 'OUTPUT_TAX', 'A-TAX'),
                  ('GL-CO', 'INVENTORY', 'A-INV'),
                  ('GL-CO', 'COST_OF_GOODS_SOLD', 'A-COG');
                """);

            // 2 units at 10 with 15% tax: net 20, tax 3, total 23; cost 2 x 4 = 8.
            var posted = Post(connection, sale, 1);
            Assert.NotNull(posted);
            Assert.Equal("GL:SAL:SAL-2026-0001", posted!.DocumentNumber);
            Assert.Equal(31m, posted.Total);
            Assert.Equal(1L, ScalarLong(connection, "SELECT COUNT(*) FROM journal_entries WHERE status = 1 AND document_number = 'GL:SAL:SAL-2026-0001';"));
            Assert.Equal(5L, ScalarLong(connection, "SELECT COUNT(*) FROM journal_entry_lines;"));
            Assert.Equal(31L, ScalarLong(connection, "SELECT SUM(debit) FROM journal_entry_lines;"));
            Assert.Equal(31L, ScalarLong(connection, "SELECT SUM(credit) FROM journal_entry_lines;"));
            Assert.Equal(20L, ScalarLong(connection, "SELECT SUM(natural_amount) FROM v_posted_gl_lines WHERE account_code = '4000';"));
            Assert.Equal(2L, ScalarLong(connection, "SELECT COUNT(*) FROM journal_audit_events;"));

            // A date with no open fiscal period is refused and the failed attempt leaves no journal behind.
            var late = NewSale(InvDocKind.SalesInvoice, "SAL-2027-0001", new DateTime(2027, 1, 5));
            var closed = Assert.Throws<InvalidOperationException>(() => Post(connection, late, 3));
            Assert.Contains("No open fiscal period", closed.Message);
            Assert.Equal(1L, ScalarLong(connection, "SELECT COUNT(*) FROM journal_entries;"));

            // A return falls back to the revenue account when no returns account is mapped,
            // so revenue nets back to zero.
            var salesReturn = NewSale(InvDocKind.SalesReturn, "SRT-2026-0001", new DateTime(2026, 3, 12));
            Assert.NotNull(Post(connection, salesReturn, 2));
            Assert.Equal(0L, ScalarLong(connection, "SELECT SUM(natural_amount) FROM v_posted_gl_lines WHERE account_code = '4000';"));
            Assert.Equal(0L, ScalarLong(connection, "SELECT SUM(natural_amount) FROM v_posted_gl_lines WHERE account_code = '1100';"));
        }
        finally
        {
            AppState.CurrentCompanyCode = oldCompany;
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    private static InvDoc NewSale(InvDocKind kind, string docNo, DateTime date)
    {
        var doc = new InvDoc
        {
            Kind = kind,
            DocNo = docNo,
            DocDate = date,
            CurrencyCode = "SAR",
            WarehouseId = 1
        };
        doc.Lines.Add(new InvLine
        {
            LineNo = 1,
            MaterialId = 7,
            MaterialCode = "M7",
            Description = "Test item",
            Qty = 2m,
            UnitPrice = 10m,
            TaxRate = 15m,
            WarehouseId = 1
        });
        return doc;
    }

    private static GlPost.PostedJournal? Post(SqliteConnection connection, InvDoc doc, long docId)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            var result = GlPost.PostDocument(connection, transaction, doc, docId, "gl-tester");
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath};");
        connection.Open();
        return connection;
    }

    private static void ApplySqliteMigrations(string databasePath)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "dba", "mig")))
            root = root.Parent;

        Assert.NotNull(root);
        var migrator = Path.Combine(root!.FullName, "tools", "SqlMigrator", "bin", "Release", "net8.0", "SqlMigrator.dll");
        Assert.True(File.Exists(migrator), $"SQL migrator was not built: {migrator}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = root.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add(migrator);
        process.StartInfo.ArgumentList.Add("--provider");
        process.StartInfo.ArgumentList.Add("sqlite");
        process.StartInfo.ArgumentList.Add("--connection");
        process.StartInfo.ArgumentList.Add($"Data Source={databasePath};");

        Assert.True(process.Start(), "Could not start the DBA SQL migrator.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(120_000), "DBA SQL migrations timed out.");
        var output = stdout.GetAwaiter().GetResult();
        var errors = stderr.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0,
            $"Applying SQLite DBA migrations failed (exit {process.ExitCode}).\n{output}\n{errors}");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
