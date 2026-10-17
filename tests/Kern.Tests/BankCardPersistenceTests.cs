using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ShouTech.App;
using ShouTech.App.Configuration;
using ShouTech.App.Services;
using Xunit;

namespace Kern.Tests;

public sealed class BankCardPersistenceTests
{
    [Fact]
    public async Task BankCardSvc_ShouldRoundTripFieldsAndScopeSoftDeleteByCompany()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"shoutech-bank-card-{Guid.NewGuid():N}.sqlite3");
        var settingsField = typeof(AppCfgSvc).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("App configuration test seam was not found.");
        var oldSettings = settingsField.GetValue(null);
        var oldCompany = AppState.CurrentCompanyCode;
        var oldUser = AppState.CurrentUser;

        try
        {
            settingsField.SetValue(null, new AppSetng
            {
                Database = new DatabaseSettings { DefaultProvider = "Sqlite" },
                ConnectionStrings = new Dictionary<string, ConnectionStringSettings>
                {
                    ["Default"] = new()
                    {
                        Provider = "Sqlite",
                        ConnectionString = $"Data Source={databasePath};"
                    }
                }
            });

            ApplySqliteMigrations(databasePath);
            using (var connection = Open(databasePath))
            {
                Execute(connection, """
                    INSERT INTO companies(company_id, company_code, company_name_ar, company_name_en)
                    VALUES
                      ('COMP-A', 'COMP-A', 'Company A', 'Company A'),
                      ('COMP-B', 'COMP-B', 'Company B', 'Company B');
                    """);
            }

            AppState.CurrentUser = new UserInfo { Id = "test-user", Username = "bank-card-test" };
            AppState.CurrentCompanyCode = "COMP-A";

            var original = new BankCardRecord
            {
                Code = "BNK-ROUNDTRIP",
                Name = "Jordan Test Bank",
                Branch = "Irbid Main",
                AccountNumber = "0011223344",
                Iban = "GB82WEST12345698765432",
                CurrencyCode = "JOD",
                GeneralLedgerAccountCode = "121201"
            };
            await BankCardSvc.SaveAsync(original);
            var loaded = await BankCardSvc.LoadByCodeAsync(original.Code);
            Assert.NotNull(loaded);
            Assert.Equal(original.Code, loaded!.Code);
            Assert.Equal(original.Name, loaded.Name);
            Assert.Equal(original.Branch, loaded.Branch);
            Assert.Equal(original.AccountNumber, loaded.AccountNumber);
            Assert.Equal(original.Iban, loaded.Iban);
            Assert.Equal(original.CurrencyCode, loaded.CurrencyCode);
            Assert.Equal(original.GeneralLedgerAccountCode, loaded.GeneralLedgerAccountCode);

            var edited = original with
            {
                Name = "Updated Test Bank",
                Branch = "City Branch",
                AccountNumber = "99887766",
                Iban = "GB82 WEST 1234 5698 7654 32",
                CurrencyCode = "USD",
                GeneralLedgerAccountCode = "121202"
            };
            await BankCardSvc.SaveAsync(edited);
            var reloaded = await BankCardSvc.LoadByCodeAsync(original.Code);
            Assert.NotNull(reloaded);
            Assert.Equal("Updated Test Bank", reloaded!.Name);
            Assert.Equal("City Branch", reloaded.Branch);
            Assert.Equal("99887766", reloaded.AccountNumber);
            Assert.Equal("GB82WEST12345698765432", reloaded.Iban);
            Assert.Equal("USD", reloaded.CurrencyCode);
            Assert.Equal("121202", reloaded.GeneralLedgerAccountCode);

            AppState.CurrentCompanyCode = "COMP-B";
            await BankCardSvc.SaveAsync(original with { Name = "Company B Bank" });
            Assert.Equal("Company B Bank", (await BankCardSvc.LoadByCodeAsync(original.Code))!.Name);

            AppState.CurrentCompanyCode = "COMP-A";
            Assert.True(await BankCardSvc.DeleteAsync(original.Code));
            Assert.Null(await BankCardSvc.LoadByCodeAsync(original.Code));
            Assert.False(await BankCardSvc.DeleteAsync(original.Code));

            AppState.CurrentCompanyCode = "COMP-B";
            var secondCompany = await BankCardSvc.LoadByCodeAsync(original.Code);
            Assert.NotNull(secondCompany);
            Assert.Equal("Company B Bank", secondCompany!.Name);

            using var verification = Open(databasePath);
            Assert.Equal(2L, ScalarLong(verification,
                "SELECT COUNT(*) FROM banks WHERE bank_code='BNK-ROUNDTRIP';"));
            Assert.Equal(1L, ScalarLong(verification,
                "SELECT COUNT(*) FROM banks WHERE company_id='COMP-A' AND bank_code='BNK-ROUNDTRIP' AND is_deleted=1;"));
            Assert.Equal(0L, ScalarLong(verification,
                "SELECT COUNT(*) FROM banks WHERE company_id='COMP-B' AND bank_code='BNK-ROUNDTRIP' AND is_deleted=1;"));
        }
        finally
        {
            settingsField.SetValue(null, oldSettings);
            AppState.CurrentCompanyCode = oldCompany;
            AppState.CurrentUser = oldUser;
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var sidecar = databasePath + suffix;
                if (File.Exists(sidecar))
                    File.Delete(sidecar);
            }
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
