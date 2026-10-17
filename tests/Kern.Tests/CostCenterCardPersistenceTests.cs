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

public sealed class CostCenterCardPersistenceTests
{
    [Fact]
    public async Task CostCenterCardSvc_ShouldRoundTripHierarchyValidateCyclesAndIsolateCompanies()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"shoutech-cost-center-card-{Guid.NewGuid():N}.sqlite3");
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

            AppState.CurrentUser = new UserInfo { Id = "test-user", Username = "cost-center-test" };
            AppState.CurrentCompanyCode = "COMP-A";

            var root = new CostCenterCardRecord
            {
                Code = "CC-ROOT",
                ParentCode = "",
                Level = 1,
                NameAr = "المركز الرئيسي",
                NameEn = "Root Center",
                Notes = "Root notes"
            };
            await CostCenterCardSvc.SaveAsync(root);
            var loadedRoot = await CostCenterCardSvc.LoadByCodeAsync(root.Code);
            Assert.NotNull(loadedRoot);
            Assert.Equal(root.Code, loadedRoot!.Code);
            Assert.Equal(root.ParentCode, loadedRoot.ParentCode);
            Assert.Equal(1, loadedRoot.Level);
            Assert.Equal(root.NameAr, loadedRoot.NameAr);
            Assert.Equal(root.NameEn, loadedRoot.NameEn);
            Assert.Equal(root.Notes, loadedRoot.Notes);

            var child = new CostCenterCardRecord
            {
                Code = "CC-CHILD",
                ParentCode = "CC-ROOT",
                Level = 2,
                NameAr = "المركز الفرعي",
                NameEn = "Child Center",
                Notes = "Child notes"
            };
            await CostCenterCardSvc.SaveAsync(child);
            var loadedChild = await CostCenterCardSvc.LoadByCodeAsync(child.Code);
            Assert.NotNull(loadedChild);
            Assert.Equal("CC-ROOT", loadedChild!.ParentCode);
            Assert.Equal(2, loadedChild.Level);
            Assert.Equal("Child notes", loadedChild.Notes);

            await Assert.ThrowsAsync<CostCenterCardException>(
                () => CostCenterCardSvc.SaveAsync(root with { ParentCode = "CC-CHILD", Level = 3 }));

            await Assert.ThrowsAsync<CostCenterCardException>(
                () => CostCenterCardSvc.SaveAsync(child with { Level = 3 }));

            Assert.False(await CostCenterCardSvc.DeleteAsync(root.Code),
                "A parent center with an active child must not be deleted.");

            var editedChild = child with { NameEn = "Updated Child", Notes = "Edited notes" };
            await CostCenterCardSvc.SaveAsync(editedChild);
            var reloadedChild = await CostCenterCardSvc.LoadByCodeAsync(child.Code);
            Assert.NotNull(reloadedChild);
            Assert.Equal("Updated Child", reloadedChild!.NameEn);
            Assert.Equal("Edited notes", reloadedChild.Notes);

            AppState.CurrentCompanyCode = "COMP-B";
            await CostCenterCardSvc.SaveAsync(root with { NameEn = "Company B Root" });
            Assert.Equal("Company B Root", (await CostCenterCardSvc.LoadByCodeAsync(root.Code))!.NameEn);

            AppState.CurrentCompanyCode = "COMP-A";
            Assert.True(await CostCenterCardSvc.DeleteAsync(child.Code));
            Assert.Null(await CostCenterCardSvc.LoadByCodeAsync(child.Code));
            Assert.True(await CostCenterCardSvc.DeleteAsync(root.Code));
            Assert.Null(await CostCenterCardSvc.LoadByCodeAsync(root.Code));

            AppState.CurrentCompanyCode = "COMP-B";
            var otherCompanyRoot = await CostCenterCardSvc.LoadByCodeAsync(root.Code);
            Assert.NotNull(otherCompanyRoot);
            Assert.Equal("Company B Root", otherCompanyRoot!.NameEn);

            using var verification = Open(databasePath);
            Assert.Equal(3L, ScalarLong(verification,
                "SELECT COUNT(*) FROM cost_centers;"));
            Assert.Equal(2L, ScalarLong(verification,
                "SELECT COUNT(*) FROM cost_centers WHERE company_id='COMP-A' AND is_deleted=1;"));
            Assert.Equal(0L, ScalarLong(verification,
                "SELECT COUNT(*) FROM cost_centers WHERE company_id='COMP-B' AND is_deleted=1;"));
        }
        finally
        {
            settingsField.SetValue(null, oldSettings);
            AppState.CurrentCompanyCode = oldCompany;
            AppState.CurrentUser = oldUser;
            SqliteConnection.ClearAllPools();
            TryDeleteFile(databasePath);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var sidecar = databasePath + suffix;
                TryDeleteFile(sidecar);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // SQLite providers can retain pooled handles until the process exits.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup must not mask the persistence assertions that already ran.
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
