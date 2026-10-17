using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ShouTech.App;
using ShouTech.App.Configuration;
using ShouTech.App.Services;
using Xunit;

namespace Kern.Tests;

public sealed class ChequeBatchPersistenceTests
{
    [Fact]
    public async Task ChequeBatchSvc_ShouldRoundTripHeaderAndLinesRetainVersionsAndScopeOperations()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"shoutech-cheque-batch-{Guid.NewGuid():N}.sqlite3");
        var connectionString = $"Data Source={databasePath};";
        var settingsField = typeof(AppCfgSvc).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("App configuration test seam was not found.");
        var oldSettings = settingsField.GetValue(null);
        var oldCompany = AppState.CurrentCompanyCode;
        var oldUser = AppState.CurrentUser;
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();

        try
        {
            settingsField.SetValue(null, new AppSetng
            {
                Database = new DatabaseSettings { DefaultProvider = "Sqlite" },
                ConnectionStrings = new Dictionary<string, ConnectionStringSettings>
                {
                    ["Default"] = new() { Provider = "Sqlite", ConnectionString = connectionString }
                }
            });

            ApplySqliteMigrations(databasePath);
            using (var connection = Open(databasePath))
            {
                Execute(connection, $"""
                    INSERT INTO companies(company_id, company_code, company_name_ar, company_name_en)
                    VALUES
                      ('{companyAId:D}', 'COMP-A', 'Company A', 'Company A'),
                      ('{companyBId:D}', 'COMP-B', 'Company B', 'Company B');
                    """);
            }

            AppState.CurrentUser = new UserInfo { Id = Guid.NewGuid().ToString("D"), Username = "cheque-batch-test" };
            AppState.CurrentCompanyCode = "COMP-A";

            var original = new ChequeBatchRecord
            {
                Code = "CHQ-ROUNDTRIP",
                BatchDate = new DateTime(2026, 10, 17),
                CashBoxCode = "CASH-01",
                CurrencyCode = "JOD",
                Memo = "October cheque batch",
                Lines = new[]
                {
                    new ChequeBatchLineRecord
                    {
                        LineNumber = 1,
                        ChequeNumber = "CH-1001",
                        BankName = "Jordan Test Bank",
                        Branch = "Irbid Main",
                        DueDate = new DateTime(2026, 11, 17),
                        Amount = 1250.375m,
                        Drawer = "Test Drawer",
                        AccountCode = "121201",
                        Memo = "First cheque"
                    },
                    new ChequeBatchLineRecord
                    {
                        LineNumber = 2,
                        ChequeNumber = "CH-1002",
                        BankName = "Jordan Test Bank",
                        Branch = "Amman Branch",
                        DueDate = new DateTime(2026, 12, 1),
                        Amount = 780.25m,
                        Drawer = "Second Drawer",
                        AccountCode = "121202",
                        Memo = "Second cheque"
                    }
                }
            };

            var batchId = await ChequeBatchSvc.SaveAsync(original);
            var loaded = await ChequeBatchSvc.LoadAsync(original.Code);
            Assert.NotNull(loaded);
            Assert.Equal(batchId, loaded!.BatchId);
            Assert.Equal(original.Code, loaded.Code);
            Assert.Equal(original.BatchDate.Date, loaded.BatchDate.Date);
            Assert.Equal(original.CashBoxCode, loaded.CashBoxCode);
            Assert.Equal(original.CurrencyCode, loaded.CurrencyCode);
            Assert.Equal(original.Memo, loaded.Memo);
            Assert.Equal(2, loaded.Lines.Count);
            Assert.Equal("CH-1001", loaded.Lines[0].ChequeNumber);
            Assert.Equal("Jordan Test Bank", loaded.Lines[0].BankName);
            Assert.Equal("Irbid Main", loaded.Lines[0].Branch);
            Assert.Equal(new DateTime(2026, 11, 17), loaded.Lines[0].DueDate.Date);
            Assert.Equal(1250.375m, loaded.Lines[0].Amount);
            Assert.Equal("Test Drawer", loaded.Lines[0].Drawer);
            Assert.Equal("121201", loaded.Lines[0].AccountCode);
            Assert.Equal("First cheque", loaded.Lines[0].Memo);

            var updated = original with
            {
                Memo = "Updated October batch",
                Lines = new[]
                {
                    original.Lines[0] with { Amount = 1400.625m, Memo = "Revised cheque" },
                    new ChequeBatchLineRecord
                    {
                        LineNumber = 2,
                        ChequeNumber = "CH-1003",
                        BankName = "Other Test Bank",
                        Branch = "Zarqa Branch",
                        DueDate = new DateTime(2027, 1, 15),
                        Amount = 99.875m,
                        Drawer = "Third Drawer",
                        AccountCode = "121203",
                        Memo = "Replacement line"
                    }
                }
            };
            var updateId = await ChequeBatchSvc.SaveAsync(updated);
            Assert.Equal(batchId, updateId);

            var reloaded = await ChequeBatchSvc.LoadAsync(original.Code);
            Assert.NotNull(reloaded);
            Assert.Equal("Updated October batch", reloaded!.Memo);
            Assert.Equal(2, reloaded.Lines.Count);
            Assert.Equal("CH-1001", reloaded.Lines[0].ChequeNumber);
            Assert.Equal(1400.625m, reloaded.Lines[0].Amount);
            Assert.Equal("Revised cheque", reloaded.Lines[0].Memo);
            Assert.Equal("CH-1003", reloaded.Lines[1].ChequeNumber);
            Assert.Equal(99.875m, reloaded.Lines[1].Amount);

            using (var verification = Open(databasePath))
            {
                Assert.Equal(4L, ScalarLong(verification,
                    $"SELECT COUNT(*) FROM cheque_batch_lines WHERE batch_id='{batchId:D}';"));
                Assert.Equal(2L, ScalarLong(verification,
                    $"SELECT COUNT(*) FROM cheque_batch_lines WHERE batch_id='{batchId:D}' AND is_current=0;"));
                Assert.Equal(2L, ScalarLong(verification,
                    $"SELECT COUNT(*) FROM cheque_batch_lines WHERE batch_id='{batchId:D}' AND is_current=1;"));
            }

            AppState.CurrentCompanyCode = "COMP-B";
            Assert.Null(await ChequeBatchSvc.LoadAsync(original.Code));
            var companyBRecord = original with { Memo = "Company B batch" };
            var companyBBatchId = await ChequeBatchSvc.SaveAsync(companyBRecord);
            Assert.NotEqual(batchId, companyBBatchId);
            Assert.Equal("Company B batch", (await ChequeBatchSvc.LoadAsync(original.Code))!.Memo);

            AppState.CurrentCompanyCode = "COMP-A";
            Assert.True(await ChequeBatchSvc.DeleteAsync(original.Code));
            Assert.Null(await ChequeBatchSvc.LoadAsync(original.Code));
            Assert.False(await ChequeBatchSvc.DeleteAsync(original.Code));

            using (var softDeleteAudit = Open(databasePath))
            {
                Assert.Equal(1L, ScalarLong(softDeleteAudit,
                    $"SELECT COUNT(*) FROM deletion_audit_log WHERE table_name='cheque_batches' AND record_key='{batchId:D}' AND method='UI_SOFT_DELETE';"));
            }

            var binItems = await RecycleBinSvc.ListAsync("COMP-A");
            var binItem = binItems.FirstOrDefault(item =>
                item.TableName == "cheque_batches" && item.PrimaryKeyValue == batchId.ToString("D"));
            Assert.NotNull(binItem);
            var purge = await RecycleBinSvc.PurgeAsync(binItem!);
            Assert.True(purge.Succeeded, purge.ErrorMessage);

            using (var purgeVerification = Open(databasePath))
            {
                Assert.Equal(0L, ScalarLong(purgeVerification,
                    $"SELECT COUNT(*) FROM cheque_batches WHERE batch_id='{batchId:D}';"));
                Assert.Equal(0L, ScalarLong(purgeVerification,
                    $"SELECT COUNT(*) FROM cheque_batch_lines WHERE batch_id='{batchId:D}';"));
                Assert.Equal(1L, ScalarLong(purgeVerification,
                    $"SELECT COUNT(*) FROM deletion_audit_log WHERE table_name='cheque_batches' AND record_key='{batchId:D}' AND action_type='PERMANENT_DELETE';"));
            }

            AppState.CurrentCompanyCode = "COMP-B";
            Assert.Equal("Company B batch", (await ChequeBatchSvc.LoadAsync(original.Code))!.Memo);

            using var auditVerification = Open(databasePath);
            Assert.True(ScalarLong(auditVerification,
                $"SELECT COUNT(*) FROM deletion_audit_log WHERE table_name='cheque_batches' AND record_key='{batchId:D}' AND action_type='DELETE';") >= 1L);
        }
        finally
        {
            settingsField.SetValue(null, oldSettings);
            AppState.CurrentCompanyCode = oldCompany;
            AppState.CurrentUser = oldUser;
            SqliteConnection.ClearAllPools();
            TryDeleteFile(databasePath);
            TryDeleteFile(databasePath + "-wal");
            TryDeleteFile(databasePath + "-shm");
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
        process.StartInfo.ArgumentList.Add(connectionStringFor(databasePath));
        Assert.True(process.Start(), "Could not start the DBA SQL migrator.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(120_000), "DBA SQL migrations timed out.");
        Assert.True(process.ExitCode == 0,
            $"Applying SQLite DBA migrations failed (exit {process.ExitCode}).\n{stdout.GetAwaiter().GetResult()}\n{stderr.GetAwaiter().GetResult()}");
    }

    private static string connectionStringFor(string databasePath) => $"Data Source={databasePath};";

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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
