using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.App.Views;

namespace ShouTech.App.Services;

/// <summary>
/// Resolves default VAT / tax rates for the active company using tax_codes
/// and regional defaults (GCC, North Africa, Levant, EU-style).
/// </summary>
public static class TaxSvc
{
    private static readonly Dictionary<string, decimal> CountryDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        // GCC
        ["SA"] = 15m, ["AE"] = 5m, ["BH"] = 10m, ["OM"] = 5m, ["QA"] = 0m, ["KW"] = 0m,
        // Levant
        ["SY"] = 0m, ["JO"] = 16m, ["LB"] = 11m, ["PS"] = 16m, ["IQ"] = 0m,
        // North Africa
        ["EG"] = 14m, ["MA"] = 20m, ["TN"] = 19m, ["DZ"] = 19m, ["LY"] = 0m,
        // Europe (common)
        ["TR"] = 20m, ["DE"] = 19m, ["FR"] = 20m, ["GB"] = 20m, ["NL"] = 21m,
    };

    public sealed record TaxCodeRec(string Code, string Name, decimal Rate, string Country);

    public static decimal GetDefaultRate()
    {
        try
        {
            DocSchema.Ensure();
            var fromDb = TryLoadActiveRate();
            if (fromDb.HasValue) return fromDb.Value;

            var country = NormalizeCountryCode(GetCompanyCountry());
            if (country.Length > 0 && CountryDefaults.TryGetValue(country, out var rate))
                return rate;

            var missingRate = ShouTech.App.Localization.ErrorCatalog.Resolve(
                ShouTech.App.Localization.ErrorCodes.TaxRateNotConfigured);
            throw new InvalidOperationException($"[{missingRate.Code}] {missingRate.Message}");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("TAX-RATE-001", StringComparison.Ordinal))
        {
            throw;
        }
        catch (Exception ex)
        {
            var definition = ShouTech.App.Localization.ErrorCatalog.Resolve(
                ShouTech.App.Localization.ErrorCodes.TaxConfigurationLoadFailed);
            AppHost.Logging?.LogError(
                "TaxService",
                $"[{definition.Code}] {definition.Name} | Company={AppState.CurrentCompanyCode ?? "(none)"}",
                ex);
            throw new InvalidOperationException($"[{definition.Code}] {definition.Message}", ex);
        }
    }

    public static IReadOnlyList<TaxCodeRec> LoadCodes()
    {
        var list = new List<TaxCodeRec>();
        var cs = RequireConnectionString("LoadCodes");
        var companyCode = AppState.CurrentCompanyCode;
        if (string.IsNullOrWhiteSpace(companyCode))
            throw CreateTaxDatabaseError("LoadCodes", new InvalidOperationException("No active company is selected."));

        try
        {
            using var connection = Open(cs);
            using var cmd = connection.CreateCommand();
            var activePredicate = DatabaseRuntime.IsPostgreSql()
                ? "COALESCE(is_active, TRUE) = TRUE"
                : "COALESCE(is_active, 1) = 1";
            cmd.CommandText = $"""
                SELECT tax_code, COALESCE(tax_name, tax_code), COALESCE(rate, 0), COALESCE(country_code, '')
                FROM tax_codes
                WHERE {activePredicate}
                  AND company_id = (
                      SELECT company_id FROM companies
                      WHERE company_code = $companyCode
                      LIMIT 1
                  )
                ORDER BY tax_code;
                """;
            Add(cmd, "$companyCode", companyCode);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new TaxCodeRec(
                    Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? "",
                    Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "",
                    Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
                    Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? ""));
            }
        }
        catch (Exception ex)
        {
            var definition = ShouTech.App.Localization.ErrorCatalog.Resolve(
                ShouTech.App.Localization.ErrorCodes.TaxLookupFailed);
            AppHost.Logging?.LogError(
                "TaxService",
                $"[{definition.Code}] {definition.Name} | Operation=LoadCodes | Company={AppState.CurrentCompanyCode ?? "(none)"}",
                ex);
            throw new InvalidOperationException($"[{definition.Code}] {definition.Message}", ex);
        }
        return list;
    }

    public static (long? Id, string Name)? ResolveParty(string code, bool customer)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var cs = RequireConnectionString("ResolveParty");
        var company = AppState.CurrentCompanyCode ?? "";
        if (company.Length == 0)
            throw CreateTaxDatabaseError("ResolveParty", new InvalidOperationException("No active company is selected."));
        var activeRowPredicate = DatabaseRuntime.IsPostgreSql() ? "is_deleted=FALSE" : "is_deleted=0";

        try
        {
            using var connection = Open(cs);
            using var cmd = connection.CreateCommand();
            if (customer)
            {
                cmd.CommandText = $"""
                    SELECT customer_id, customer_name_ar FROM customers
                    WHERE company_id = (SELECT company_id FROM companies WHERE company_code=$c LIMIT 1)
                      AND customer_code=$code AND {activeRowPredicate} LIMIT 1;
                    """;
            }
            else
            {
                cmd.CommandText = $"""
                    SELECT supplier_id, supplier_name_ar FROM suppliers
                    WHERE company_id = (SELECT company_id FROM companies WHERE company_code=$c LIMIT 1)
                      AND supplier_code=$code AND {activeRowPredicate} LIMIT 1;
                    """;
            }
            Add(cmd, "$c", company);
            Add(cmd, "$code", code.Trim());
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return (Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
                    Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "");
        }
        catch (Exception ex)
        {
            var definition = ShouTech.App.Localization.ErrorCatalog.Resolve(
                ShouTech.App.Localization.ErrorCodes.TaxLookupFailed);
            AppHost.Logging?.LogError(
                "TaxService",
                $"[{definition.Code}] {definition.Name} | Operation=ResolveParty | Company={AppState.CurrentCompanyCode ?? "(none)"}",
                ex);
            throw new InvalidOperationException($"[{definition.Code}] {definition.Message}", ex);
        }
    }

    public static (long? Id, string Name, decimal Price, string Unit, decimal TaxRate)? ResolveMaterial(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var cs = RequireConnectionString("ResolveMaterial");
        var company = AppState.CurrentCompanyCode ?? "";
        if (company.Length == 0)
            throw CreateTaxDatabaseError("ResolveMaterial", new InvalidOperationException("No active company is selected."));

        try
        {
            using var connection = Open(cs);
            using var cmd = connection.CreateCommand();
            var activeMaterialPredicate = DatabaseRuntime.IsPostgreSql() ? "is_deleted = FALSE" : "COALESCE(is_deleted,0)=0";
            cmd.CommandText = $"""
                SELECT material_id, COALESCE(NULLIF(pos_name,''), NULLIF(name_ar,''), NULLIF(name_en,''), code),
                       COALESCE(price_retail, price_list, 0),
                       COALESCE(default_unit, unit1, ''),
                       tax_rate, is_taxable, is_zero_rated
                FROM materials
                WHERE company_id = (SELECT company_id FROM companies WHERE company_code=$c LIMIT 1)
                  AND {activeMaterialPredicate}
                  AND (
                    code=$code OR alt_code=$code OR old_code=$code OR gtin=$code OR sku_external=$code
                    OR unit1_barcode=$code OR unit2_barcode=$code OR unit3_barcode=$code
                    OR unit4_barcode=$code OR unit5_barcode=$code
                    OR name_ar=$code OR name_en=$code OR pos_name=$code OR short_name=$code
                    OR search_name=$code OR name_ar LIKE $like OR name_en LIKE $like
                    OR pos_name LIKE $like OR short_name LIKE $like OR search_name LIKE $like
                  )
                ORDER BY CASE WHEN code=$code OR alt_code=$code OR old_code=$code OR gtin=$code
                    OR unit1_barcode=$code OR unit2_barcode=$code OR unit3_barcode=$code
                    OR unit4_barcode=$code OR unit5_barcode=$code THEN 0 ELSE 1 END,
                    name_ar
                LIMIT 1;
                """;
            Add(cmd, "$c", company);
            Add(cmd, "$code", code.Trim());
            Add(cmd, "$like", "%" + code.Trim() + "%");
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            var configuredTaxRate = reader.IsDBNull(4)
                ? 0m
                : Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture);
            var isTaxable = reader.IsDBNull(5)
                || Convert.ToBoolean(reader.GetValue(5), CultureInfo.InvariantCulture);
            var isZeroRated = !reader.IsDBNull(6)
                && Convert.ToBoolean(reader.GetValue(6), CultureInfo.InvariantCulture);
            var effectiveTaxRate = !isTaxable || isZeroRated
                ? 0m
                : configuredTaxRate > 0m ? configuredTaxRate : GetDefaultRate();

            return (
                Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
                Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "",
                Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
                Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? "",
                effectiveTaxRate);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("TAX-RATE-001", StringComparison.Ordinal))
        {
            throw;
        }
        catch (Exception ex)
        {
            var definition = ShouTech.App.Localization.ErrorCatalog.Resolve(
                ShouTech.App.Localization.ErrorCodes.TaxLookupFailed);
            AppHost.Logging?.LogError(
                "TaxService",
                $"[{definition.Code}] {definition.Name} | Operation=ResolveMaterial | Company={AppState.CurrentCompanyCode ?? "(none)"}",
                ex);
            throw new InvalidOperationException($"[{definition.Code}] {definition.Message}", ex);
        }
    }

    private static decimal? TryLoadActiveRate()
    {
        var cs = RequireConnectionString("TryLoadActiveRate");
        var companyCode = AppState.CurrentCompanyCode;
        if (string.IsNullOrWhiteSpace(companyCode))
            throw new InvalidOperationException("No active company is selected.");
        using var connection = Open(cs);
        using var cmd = connection.CreateCommand();
        var activePredicate = DatabaseRuntime.IsPostgreSql()
            ? "COALESCE(is_active, TRUE) = TRUE"
            : "COALESCE(is_active, 1) = 1";
        var defaultPredicate = DatabaseRuntime.IsPostgreSql()
            ? "COALESCE(is_default, FALSE) = TRUE"
            : "COALESCE(is_default, 0) = 1";
        cmd.CommandText = $"""
            SELECT rate FROM tax_codes
            WHERE {activePredicate} AND {defaultPredicate}
              AND company_id = (
                  SELECT company_id FROM companies
                  WHERE company_code = $companyCode
                  LIMIT 1
              )
            LIMIT 1;
            """;
        Add(cmd, "$companyCode", companyCode);
        var v = cmd.ExecuteScalar();
        if (v is null or DBNull) return null;
        return Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    }

    private static string GetCompanyCountry()
    {
        var cs = RequireConnectionString("GetCompanyCountry");
        using var connection = Open(cs);
        using var cmd = connection.CreateCommand();
        var activePredicate = DatabaseRuntime.IsPostgreSql()
            ? "COALESCE(is_deleted, FALSE) = FALSE"
            : "COALESCE(is_deleted, 0) = 0";
        cmd.CommandText = $"""
            SELECT COALESCE(country, '') FROM companies
            WHERE company_code=$code AND {activePredicate}
            LIMIT 1;
            """;
        Add(cmd, "$code", AppState.CurrentCompanyCode ?? string.Empty);
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    }


    private static string RequireConnectionString(string operation)
    {
        var connectionString = DatabaseRuntime.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        throw CreateTaxDatabaseError(
            operation,
            new InvalidOperationException("The active database connection is not configured."));
    }

    private static InvalidOperationException CreateTaxDatabaseError(string operation, Exception exception)
    {
        var definition = ShouTech.App.Localization.ErrorCatalog.Resolve(
            ShouTech.App.Localization.ErrorCodes.TaxConfigurationLoadFailed);
        AppHost.Logging?.LogError(
            "TaxService",
            $"[{definition.Code}] {definition.Name} | Operation={operation} | Company={AppState.CurrentCompanyCode ?? "(none)"}",
            exception);
        return new InvalidOperationException($"[{definition.Code}] {definition.Message}", exception);
    }

    private static string NormalizeCountryCode(string? country)
    {
        var value = country?.Trim() ?? string.Empty;
        if (value.Length == 2)
            return value.ToUpperInvariant();

        return value.ToUpperInvariant() switch
        {
            "SAUDI ARABIA" or "KINGDOM OF SAUDI ARABIA" or "السعودية" or "المملكة العربية السعودية" => "SA",
            "UNITED ARAB EMIRATES" or "UAE" or "الإمارات" or "الإمارات العربية المتحدة" => "AE",
            "BAHRAIN" or "البحرين" => "BH",
            "OMAN" or "عمان" or "سلطنة عمان" => "OM",
            "QATAR" or "قطر" => "QA",
            "KUWAIT" or "الكويت" => "KW",
            "SYRIA" or "SYRIAN ARAB REPUBLIC" or "سوريا" => "SY",
            "JORDAN" or "الأردن" => "JO",
            "LEBANON" or "لبنان" => "LB",
            "PALESTINE" or "STATE OF PALESTINE" or "فلسطين" => "PS",
            "IRAQ" or "العراق" => "IQ",
            "EGYPT" or "مصر" => "EG",
            "MOROCCO" or "المغرب" => "MA",
            "TUNISIA" or "تونس" => "TN",
            "ALGERIA" or "الجزائر" => "DZ",
            "LIBYA" or "ليبيا" => "LY",
            "TURKEY" or "TÜRKIYE" or "TURKIYE" or "تركيا" => "TR",
            "GERMANY" or "ألمانيا" => "DE",
            "FRANCE" or "فرنسا" => "FR",
            "UNITED KINGDOM" or "UK" or "BRITAIN" or "المملكة المتحدة" => "GB",
            "NETHERLANDS" or "THE NETHERLANDS" or "هولندا" => "NL",
            _ => string.Empty
        };
    }

    private static DbConnection Open(string connectionString)
    {
        DbConnection connection = DatabaseRuntime.IsPostgreSql()
            ? new NpgsqlConnection(connectionString)
            : new SqliteConnection(DatabaseRuntime.NormalizeSqliteConnectionString(connectionString));
        connection.Open();
        return connection;
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        command.Parameters.Add(p);
    }
}
