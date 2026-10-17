using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ShouTech.App.Configuration;
using ShouTech.App.Services;
using ShouTech.Domain.Entities;
using ShouTech.Infrastructure;
using Xunit;

namespace Kern.Tests;

public sealed class SalesRepresentativePersistenceTests
{
    [Fact]
    public void SalesRepresentativeRepository_ShouldNormalizeProviderNamesAndRejectUnknownProviders()
    {
        const string connectionString = "Data Source=:memory;";
        Assert.NotNull(new SalesRepresentativeRepository("sqlite", connectionString));
        Assert.NotNull(new SalesRepresentativeRepository("SQLite3", connectionString));
        Assert.NotNull(new SalesRepresentativeRepository("postgres", "Host=localhost;Database=test;Username=test;Password=test"));
        Assert.NotNull(new SalesRepresentativeRepository("Npgsql", "Host=localhost;Database=test;Username=test;Password=test"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SalesRepresentativeRepository("UnknownProvider", connectionString));
        Assert.Throws<ArgumentException>(() =>
            new SalesRepresentativeRepository(" ", connectionString));
    }

    [Fact]
    public async Task SalesRepresentativeRepositoryAndTargets_ShouldRoundTripAndEnforceCompanyScope()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"shoutech-rep-card-{Guid.NewGuid():N}.sqlite3");
        var connectionString = $"Data Source={databasePath};";
        var settingsField = typeof(AppCfgSvc).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("App configuration test seam was not found.");
        var oldSettings = settingsField.GetValue(null);
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var repId = Guid.Empty;

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
                        ConnectionString = connectionString
                    }
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

            var repository = new SalesRepresentativeRepository("SQLite", connectionString);
            var resolvedCompanyA = await repository.ResolveCompanyIdAsync("COMP-A");
            var resolvedCompanyB = await repository.ResolveCompanyIdAsync("COMP-B");
            Assert.Equal(companyAId, resolvedCompanyA);
            Assert.Equal(companyBId, resolvedCompanyB);

            var entity = new SalesRepresentative(
                resolvedCompanyA,
                "REP-ROUNDTRIP",
                "Test Representative",
                "North",
                2.75m,
                "0791234567");
            var savedId = await repository.SaveAsync(entity, actorId.ToString("D"));
            repId = savedId;

            var loaded = await repository.FindByCodeAsync(resolvedCompanyA, "REP-ROUNDTRIP");
            Assert.NotNull(loaded);
            Assert.Equal(savedId, loaded!.Id);
            Assert.Equal("Test Representative", loaded.Name);
            Assert.Equal("North", loaded.Area);
            Assert.Equal(2.75m, loaded.CommissionRate);
            Assert.Equal("0791234567", loaded.Phone);

            var editable = SalesRepresentative.Rehydrate(loaded);
            editable.Update(
                name: "Updated Representative",
                commissionRate: 3.125m,
                phone: "0787654321",
                modifiedBy: actorId);
            Assert.True(await repository.UpdateAsync(editable, actorId.ToString("D")));

            var updated = await repository.FindByCodeAsync(resolvedCompanyA, "REP-ROUNDTRIP");
            Assert.NotNull(updated);
            Assert.Equal("Updated Representative", updated!.Name);
            Assert.Equal(3.125m, updated.CommissionRate);
            Assert.Equal("0787654321", updated.Phone);

            var targetRows = new[]
            {
                new SalesRepTargetRecord
                {
                    PeriodCode = "2026-10",
                    TargetAmount = 12345.6789m,
                    Notes = "Monthly target"
                },
                new SalesRepTargetRecord
                {
                    PeriodCode = "2026-11",
                    TargetAmount = 18000m,
                    Notes = "Next month"
                }
            };
            await SalesRepTargetSvc.SaveAsync(resolvedCompanyA, savedId, targetRows, actorId.ToString("D"));

            var loadedTargets = await SalesRepTargetSvc.LoadAsync(resolvedCompanyA, savedId);
            Assert.Equal(2, loadedTargets.Count);
            Assert.Equal("2026-11", loadedTargets[0].PeriodCode);
            Assert.Equal(18000m, loadedTargets[0].TargetAmount);
            Assert.Equal("2026-10", loadedTargets[1].PeriodCode);
            Assert.Equal(12345.6789m, loadedTargets[1].TargetAmount);
            Assert.Equal("Monthly target", loadedTargets[1].Notes);

            await SalesRepTargetSvc.SaveAsync(resolvedCompanyA, savedId,
                new[]
                {
                    new SalesRepTargetRecord
                    {
                        PeriodCode = "2026-10",
                        TargetAmount = 22222.25m,
                        Notes = "Updated monthly target"
                    }
                },
                actorId.ToString("D"));
            var targetsAfterUpdate = await SalesRepTargetSvc.LoadAsync(resolvedCompanyA, savedId);
            Assert.Equal(2, targetsAfterUpdate.Count);
            Assert.Equal(22222.25m, targetsAfterUpdate[1].TargetAmount);
            Assert.Equal("Updated monthly target", targetsAfterUpdate[1].Notes);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                SalesRepTargetSvc.SaveAsync(resolvedCompanyA, savedId,
                    new[]
                    {
                        new SalesRepTargetRecord { PeriodCode = "2026-12", TargetAmount = 1 },
                        new SalesRepTargetRecord { PeriodCode = "2026-12", TargetAmount = 2 }
                    },
                    actorId.ToString("D")));

            Assert.False(await repository.DeleteAsync(savedId, resolvedCompanyB, actorId.ToString("D")));
            Assert.NotNull(await repository.FindByCodeAsync(resolvedCompanyA, "REP-ROUNDTRIP"));

            Assert.True(await repository.DeleteAsync(savedId, resolvedCompanyA, actorId.ToString("D"), "Integration test"));
            Assert.Null(await repository.FindByCodeAsync(resolvedCompanyA, "REP-ROUNDTRIP"));
            Assert.False(await repository.RestoreAsync(savedId, resolvedCompanyB, actorId.ToString("D")));
            Assert.True(await repository.RestoreAsync(savedId, resolvedCompanyA, actorId.ToString("D")));
            Assert.NotNull(await repository.FindByCodeAsync(resolvedCompanyA, "REP-ROUNDTRIP"));

            using var verification = Open(databasePath);
            Assert.Equal(1L, ScalarLong(verification,
                $"SELECT COUNT(*) FROM sales_representatives WHERE rep_id='{savedId:D}' AND is_deleted=0;"));
            Assert.Equal(2L, ScalarLong(verification,
                $"SELECT COUNT(*) FROM sales_rep_targets WHERE rep_id='{savedId:D}' AND is_deleted=0;"));
        }
        finally
        {
            settingsField.SetValue(null, oldSettings);
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
        process.StartInfo.ArgumentList.Add($"Data Source={databasePath};");
        Assert.True(process.Start(), "Could not start the DBA SQL migrator.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(120_000), "DBA SQL migrations timed out.");
        Assert.True(process.ExitCode == 0,
            $"Applying SQLite DBA migrations failed (exit {process.ExitCode}).\n{stdout.GetAwaiter().GetResult()}\n{stderr.GetAwaiter().GetResult()}");
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Providers can briefly retain file handles after the assertions have completed.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup must not turn a verified database result into a false failure.
        }
    }
}
