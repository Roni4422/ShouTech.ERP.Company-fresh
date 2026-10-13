using System.Data.Common;
using System.IO.Compression;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.App;
using ShouTech.App.Configuration;
using ShouTech.App.Reporting;

namespace ShouTech.App.Services;

/// <summary>A user-facing export item backed by a known business table.</summary>
public sealed record DataExportOption(
    string Key,
    string DisplayName,
    string TableName,
    DataExportFilter Filter = DataExportFilter.None,
    bool IsSystemInfo = false);

public enum DataExportFilter
{
    None,
    Sales,
    Returns,
    OpeningStock
}

/// <summary>
/// Result of an Excel dataset import.
/// Upsert mode only writes rows present in the file.
/// Replace mode also removes (soft-delete when available) company rows absent from the file.
/// </summary>
public sealed record ExcelImportResult(
    int UpsertedRows,
    int DeletedRows,
    bool ReplaceMode,
    string DatasetKey,
    string TableName);


/// <summary>
/// Exports curated business datasets to the report service's Excel and PDF
/// writers. Table names are selected only from the static catalog below.
/// </summary>
public static class DataExportSvc
{
    private const string LogCategory = "DataExchange";

    private static void LogInfo(string message)
    {
        try { AppServiceHost.Logging.LogInfo(LogCategory, message); } catch { }
    }

    private static void LogWarn(string message)
    {
        try { AppServiceHost.Logging.LogWarning(LogCategory, message); } catch { }
    }

    private static void LogError(string message, Exception? ex = null)
    {
        try
        {
            if (ex != null) AppServiceHost.Logging.LogError(LogCategory, message, ex);
            else AppServiceHost.Logging.LogError(LogCategory, message);
        }
        catch { }
    }

    private static void LogOp(string action, string message)
    {
        try { AppServiceHost.Logging.LogOperation(LogCategory, action, message); } catch { }
    }

    private sealed record DatasetIdentity(
        int Version,
        string Key,
        string TableName,
        string[] Headers,
        string[] Fields,
        string[] Types);

    private sealed record ExcelSheet(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string?>> Rows);

    private sealed record DatasetSeed(
        string Key,
        string EnglishName,
        string[] TableCandidates,
        DataExportFilter Filter = DataExportFilter.None);

    private static readonly DatasetSeed[] Catalog =
    [
        new("Company", "Company data", ["companies", "Companies"]),
        new("Branches", "Branches", ["branches", "Branches"]),
        new("Accounts", "Chart of accounts", ["accounts", "chart_of_accounts", "Accounts"]),
        new("AccountTypes", "Account types", ["account_types", "AccountTypes"]),
        new("Products", "Product cards and directory", ["materials", "products", "Products"]),
        new("ProductClasses", "Product classes", ["product_categories", "product_classes", "ProductClasses", "Categories"]),
        new("Brands", "Brands", ["brands", "Brands"]),
        new("ProductAttributes", "Product attributes", ["material_attributes", "product_attributes", "ProductAttributes"]),
        new("ProductClassAttributes", "Product class attributes", ["product_class_attributes", "ProductClassAttributes"]),
        new("ProductSuppliers", "Product suppliers", ["material_suppliers", "product_suppliers", "ProductSuppliers"]),
        new("ProductBarcodes", "Product barcodes", ["material_extra_barcodes", "product_barcodes", "ProductBarcodes"]),
        new("ProductUnits", "Product units and barcodes", ["product_units", "ProductUnits"]),
        new("Customers", "Customer cards", ["customers", "Customers"]),
        new("CustomerGroups", "Customer groups", ["customer_groups", "CustomerGroups"]),
        new("CustomerContacts", "Customer contacts", ["customer_contacts", "CustomerContacts"]),
        new("CustomerAddresses", "Customer addresses", ["customer_addresses", "CustomerAddresses"]),
        new("Suppliers", "Supplier cards", ["suppliers", "Suppliers", "Vendors"]),
        new("SupplierGroups", "Supplier groups", ["supplier_groups", "SupplierGroups", "VendorGroups"]),
        new("SupplierContacts", "Supplier contacts", ["supplier_contacts", "SupplierContacts"]),
        new("SalesRepresentatives", "Sales representatives", ["sales_reps", "SalesReps"]),
        new("SalesRepresentativeTargets", "Sales representative targets", ["sales_rep_targets", "SalesRepTargets"]),
        new("Employees", "Employee cards", ["employees", "Employees"]),
        new("Departments", "Departments", ["departments", "Departments"]),
        new("JobTitles", "Job titles", ["job_titles", "JobTitles"]),
        new("Warehouses", "Warehouses", ["warehouses", "Warehouses"]),
        new("WarehouseLocations", "Warehouse locations", ["warehouse_locations", "WarehouseBins"]),
        new("WarehouseZones", "Warehouse zones", ["warehouse_zones", "WarehouseZones"]),
        new("WarehouseTypes", "Warehouse types", ["warehouse_types", "WarehouseTypes"]),
        new("Units", "Units of measure", ["units_of_measure", "UnitsOfMeasure"]),
        new("FiscalYears", "Fiscal years", ["fiscal_years", "FiscalYears"]),
        new("Currencies", "Currencies and rates", ["currencies", "Currencies"]),
        new("CurrencyRates", "Currency exchange rates", ["currency_rates", "CurrencyRates"]),
        new("Banks", "Banks and bank accounts", ["banks", "Banks"]),
        new("BankAccounts", "Bank accounts", ["bank_accounts", "BankAccounts"]),
        new("TaxCodes", "Tax codes", ["tax_codes", "TaxCodes"]),
        new("Sales", "Sales invoices", ["sales_invoices", "SalesInvoices", "pos_transactions"], DataExportFilter.Sales),
        new("SalesLines", "Sales invoice details", ["sales_invoice_lines", "SalesInvoiceLines", "pos_transaction_items"], DataExportFilter.Sales),
        new("SalesReturns", "Sales returns", ["sales_returns", "SalesReturns", "pos_transactions"], DataExportFilter.Returns),
        new("SalesReturnLines", "Sales return details", ["sales_return_lines", "SalesReturnLines", "pos_transaction_items"], DataExportFilter.Returns),
        new("Purchases", "Purchase invoices", ["purchase_invoices", "PurchaseInvoices"]),
        new("PurchaseLines", "Purchase invoice details", ["purchase_invoice_lines", "PurchaseInvoiceLines"]),
        new("PurchaseReturns", "Purchase returns", ["purchase_returns", "PurchaseReturns"]),
        new("PurchaseReturnLines", "Purchase return details", ["purchase_return_lines", "PurchaseReturnLines"]),
        new("OpeningStock", "Opening stock and balances", ["opening_stock", "opening_stocks", "stock_balances", "StockBalances"], DataExportFilter.OpeningStock),
        new("StockMovements", "Inventory movements", ["material_stock_movements", "stock_movements", "StockMovements"]),
        new("StockAdjustments", "Inventory adjustments", ["stock_adjustments", "StockAdjustments"]),
        new("StockAdjustmentLines", "Inventory adjustment details", ["stock_adjustment_items", "StockAdjustmentLines"]),
        new("StockReservations", "Inventory reservations", ["stock_reservations", "StockReservations"]),
        new("Batches", "Product batches", ["batches", "ProductBatches"]),
        new("SerialNumbers", "Product serial numbers", ["serial_numbers", "ProductSerials"]),
        new("ValuationMethods", "Inventory valuation methods", ["valuation_methods", "ValuationMethods"]),
        new("ProductValuation", "Product valuation", ["product_valuation", "ProductValuation"]),
        new("CostLayers", "Inventory cost layers", ["cost_layers", "CostLayers"]),
        new("ValuationHistory", "Inventory valuation history", ["valuation_history", "ValuationHistory"]),
        new("ProductionOrders", "Production orders", ["production_orders", "ProductionOrders"]),
        new("ProductionOrderLines", "Production order details", ["production_order_lines", "ProductionOrderLines"]),
        new("WorkCenters", "Work centers", ["work_centers", "WorkCenters"]),
        new("ReceiptVouchers", "Receipt vouchers", ["receipt_vouchers", "ReceiptVouchers"]),
        new("PaymentVouchers", "Payment vouchers", ["payment_vouchers", "PaymentVouchers"]),
        new("PosTerminals", "POS terminals", ["pos_terminals", "POSTerminals"]),
        new("PosSessions", "POS sessions", ["pos_sessions", "POSSessions"])
    ];

    private static readonly string[] MaterialCardExportFields =
    [
        "material_id", "code", "name_ar", "name_en", "group_code", "item_type", "category",
        "brand", "model", "color", "size_spec", "manufacturer", "origin_country", "source",
        "alt_code", "gtin", "supplier_code", "abc_class", "status", "is_stocked", "is_purchased",
        "is_sold", "is_produced", "is_service", "notes", "unit1", "unit1_factor", "unit1_barcode",
        "unit2", "unit2_factor", "unit2_barcode", "unit3", "unit3_factor", "unit3_barcode",
        "unit4", "unit4_factor", "unit4_barcode", "unit5", "unit5_factor", "unit5_barcode",
        "default_unit", "purchase_unit", "sales_unit", "cost_method", "cost", "price_retail",
        "price_wholesale", "price_special", "price_min", "price_list", "price_export",
        "price_distributor", "price_online", "price_currency", "allow_invoice_disc",
        "price_includes_tax", "qty_on_hand", "min_qty", "max_qty", "reorder_qty", "safety_stock",
        "default_warehouse", "shelf_bin", "lead_time_days", "has_expiry", "shelf_life_days",
        "has_serial", "has_batch", "is_taxable", "tax_rate", "tax_code", "acc_inventory",
        "acc_sales", "acc_sales_return", "acc_cogs", "acc_purchase", "acc_purchase_return",
        "acc_expense", "acc_wip", "allow_sale", "allow_purchase", "allow_return",
        "allow_negative", "image_path", "description"
    ];

    public static async Task<IReadOnlyList<DataExportOption>> GetAvailableOptionsAsync(CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        var tables = await GetTableNamesAsync(connection, ct);
        var options = new List<DataExportOption>();

        foreach (var seed in Catalog)
        {
            var table = ResolveTableName(tables, seed.TableCandidates);
            if (table == null)
                continue;

            var display = AppServiceHost.Localization.GetString(
                $"DataExchange.Datasets.{seed.Key}", seed.EnglishName);
            options.Add(new DataExportOption(seed.Key, display, table, seed.Filter));
        }

        options.Insert(0, new DataExportOption(
            "SystemInfo",
            AppServiceHost.Localization.GetString("DataExchange.Datasets.SystemInfo", "Company, database and backup information"),
            string.Empty,
            IsSystemInfo: true));
        return options;
    }

    internal static string? ResolveTableName(
        IEnumerable<string> tableNames,
        IEnumerable<string> tableCandidates)
    {
        ArgumentNullException.ThrowIfNull(tableNames);
        ArgumentNullException.ThrowIfNull(tableCandidates);
        var available = tableNames.ToArray();
        foreach (var candidate in tableCandidates)
        {
            var actualName = available.FirstOrDefault(name =>
                string.Equals(name, candidate, StringComparison.Ordinal));
            actualName ??= available.FirstOrDefault(name =>
                string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase));
            if (actualName != null)
                return actualName;
        }
        return null;
    }

    public static async Task<XptServ.ExportModel> BuildExportModelAsync(
        DataExportOption option,
        string companyCode,
        string companyName,
        string generatedBy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.IsSystemInfo)
            return BuildSystemInfoModel(option, companyCode, companyName, generatedBy);

        if (!Catalog.Any(x => x.Key == option.Key && x.Filter == option.Filter &&
            x.TableCandidates.Contains(option.TableName, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The selected dataset is not in the export catalog.");

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        var sql = $"SELECT * FROM {QuoteIdentifier(option.TableName)}{BuildFilter(option)}";
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);

        var fields = Enumerable.Range(0, reader.FieldCount)
            .Where(i => reader.GetFieldType(i) != typeof(byte[]))
            .Select(i => new { Index = i, Name = reader.GetName(i), Type = reader.GetFieldType(i) })
            .ToList();
        var fieldsByName = fields.ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
        fields = GetMaterialCardExportFieldNames(option.TableName, fieldsByName.Keys)
            .Select(name => fieldsByName[name])
            .ToList();
        if (fields.Count == 0)
            throw new InvalidOperationException("This dataset does not contain exportable columns.");

        var columnKeys = fields.Select((f, i) => $"Column{i + 1}").ToArray();
        var columns = fields.Select((field, i) => new RptCol
        {
            Key = columnKeys[i],
            HeaderTextKey = field.Name,
            Type = GetColumnType(field.Name, field.Type),
            Width = 140
        }).ToArray();
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < fields.Count; i++)
            {
                var value = reader.GetValue(fields[i].Index);
                row[columnKeys[i]] = value is DBNull ? null : value;
            }
            rows.Add(row);
        }

        if (rows.Count == 0)
            throw new InvalidOperationException("No records were found for the selected dataset and company.");

        var translatedHeaders = columns.ToDictionary(
            c => c.HeaderTextKey,
            c => AppServiceHost.Localization.GetString(
                $"DataExchange.Columns.{ToSnakeCase(c.HeaderTextKey)}", Humanize(c.HeaderTextKey)),
            StringComparer.Ordinal);

        return new XptServ.ExportModel
        {
            ReportKey = $"DataExport.{option.Key}",
            Title = option.DisplayName,
            CompanyName = companyName,
            GeneratedBy = generatedBy,
            FilterSummary =
                $"{rows.Count.ToString("N0", CultureInfo.CurrentCulture)} records | User: {generatedBy} | Company: {companyCode} | At: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            Columns = columns,
            Rows = rows,
            Rtl = true,
            Translate = key => translatedHeaders.TryGetValue(key, out var label) ? label : key,
            DatasetIdentityHeader = CreateDatasetIdentityHeader(option, columns, translatedHeaders)
        };
    }

    internal static IReadOnlyList<string> GetMaterialCardExportFieldNames(
        string tableName,
        IEnumerable<string> availableFieldNames)
    {
        ArgumentNullException.ThrowIfNull(availableFieldNames);
        if (!tableName.Equals("materials", StringComparison.OrdinalIgnoreCase))
            return availableFieldNames.ToArray();

        var available = new HashSet<string>(availableFieldNames, StringComparer.OrdinalIgnoreCase);
        return MaterialCardExportFields
            .Where(available.Contains)
            .ToList();
    }

    public static async Task<ExcelImportResult> ImportExcelAsync(
        string path,
        DataExportOption option,
        string companyCode,
        bool replaceMode = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        try
        {
            if (option.IsSystemInfo || !Catalog.Any(x => x.Key == option.Key && x.Filter == option.Filter &&
                    x.TableCandidates.Contains(option.TableName, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The selected dataset cannot be imported.");
            if (!IsMasterDataImportAllowed(option))
                throw new InvalidOperationException(
                    "Spreadsheet import is limited to approved master-data datasets. Sales, purchases, vouchers, journals, stock movements, and other transactional records must be created or corrected through their business workflows, not imported or overwritten from Excel.");

            var sheet = ReadExcelSheet(path);
            var markerIndexes = sheet.Headers
                .Select((header, index) => (header, index))
                .Where(x => x.header.StartsWith(DatasetIdentityPrefix, StringComparison.Ordinal))
                .ToArray();
            if (markerIndexes.Length > 1)
                throw new InvalidDataException("The workbook contains more than one ShouTech dataset identity column.");

            DatasetIdentity identity;
            var identityColumnIndex = -1;
            if (markerIndexes.Length == 1)
            {
                identityColumnIndex = markerIndexes[0].index;
                try
                {
                    var marker = Convert.FromBase64String(markerIndexes[0].header[DatasetIdentityPrefix.Length..]);
                    var payload = XptCry.Decrypt(marker, XptCry.KindDatasetIdentity);
                    identity = JsonSerializer.Deserialize<DatasetIdentity>(payload)
                        ?? throw new InvalidDataException("The workbook dataset identity is empty.");
                }
                catch (Exception ex) when (ex is FormatException or XptCry.XptCryException or JsonException)
                {
                    throw new InvalidDataException("The workbook dataset identity is invalid or belongs to another installation.", ex);
                }

                if (identity.Version != DatasetIdentityVersion ||
                    !string.Equals(identity.Key, option.Key, StringComparison.Ordinal) ||
                    !string.Equals(identity.TableName, option.TableName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The selected dataset does not match this Excel file. In the import window, choose the same dataset type used to export the workbook; do not rename its column headers.");
            }
            else if (markerIndexes.Length == 0)
            {
                // Generic workbooks are accepted only when every header maps to a real
                // column in the explicitly selected, allow-listed dataset. No SQL
                // identifiers are ever taken directly from an unvalidated workbook.
                identity = await CreateGenericDatasetIdentityAsync(sheet, option, ct);
            }
            else
            {
                throw new InvalidDataException("The workbook contains more than one ShouTech dataset identity column.");
            }

            var result = await ImportRowsAsync(
                sheet, identity, option, companyCode, identityColumnIndex, replaceMode, ct);
            LogOp("ImportExcel",
                $"key={option.Key} | table={option.TableName} | mode={(replaceMode ? "Replace" : "Upsert")} | upserted={result.UpsertedRows} | deleted={result.DeletedRows} | company={companyCode} | file={path}");
            return result;
        }
        catch (Exception ex)
        {
            LogError(
                $"ImportExcel failed | key={option.Key} | mode={(replaceMode ? "Replace" : "Upsert")} | company={companyCode} | file={path} | {ex.Message}",
                ex);
            throw;
        }
    }

    public static async Task<int> ImportEncryptedAsync(
        string path,
        DataExportOption option,
        string companyCode,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.IsSystemInfo || !Catalog.Any(x => x.Key == option.Key && x.Filter == option.Filter &&
                x.TableCandidates.Contains(option.TableName, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The selected dataset cannot be imported.");
        if (!IsMasterDataImportAllowed(option))
            throw new InvalidOperationException(
                "Encrypted dataset imports are limited to approved master-data datasets. Transactional records must use their business workflows.");

        if (!string.Equals(Path.GetExtension(path), ".shx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only .shx encrypted dataset files can be imported here.");
        var payload = XptCry.DecryptFromFile(path, XptCry.KindGenericData);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!root.TryGetProperty("report", out var reportElement) ||
            reportElement.GetString() != $"DataExport.{option.Key}")
            throw new InvalidDataException("The encrypted file belongs to a different dataset type.");

        var sourceColumns = root.GetProperty("columns").EnumerateArray().ToArray();
        var fields = sourceColumns.Select(column => column.GetProperty("HeaderTextKey").GetString()
            ?? throw new InvalidDataException("An encrypted dataset column has no field name.")).ToArray();
        var headers = fields.ToArray();
        var types = sourceColumns.Select(column => NormalizeEncryptedType(column.GetProperty("type").GetString())).ToArray();
        var identity = new DatasetIdentity(
            DatasetIdentityVersion, option.Key, option.TableName, headers, fields, types);
        var rows = new List<IReadOnlyList<string?>>();
        foreach (var sourceRow in root.GetProperty("rows").EnumerateArray())
        {
            var row = new string?[sourceColumns.Length];
            for (var i = 0; i < sourceColumns.Length; i++)
            {
                var value = sourceRow.GetProperty(sourceColumns[i].GetProperty("Key").GetString()!);
                row[i] = value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => throw new InvalidDataException("The encrypted dataset contains an unsupported cell value.")
                };
            }
            rows.Add(row);
        }

        var encResult = await ImportRowsAsync(new ExcelSheet(headers, rows), identity, option, companyCode, -1, replaceMode: false, ct);
        return encResult.UpsertedRows;
    }

    internal static Task<SteiResult> ExportDatasetPackageAsync(
        string path,
        string password,
        DataExportOption option,
        XptServ.ExportModel model,
        IProgress<(long done, long total)>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.IsSystemInfo || !Catalog.Any(x => x.Key == option.Key && x.Filter == option.Filter &&
                x.TableCandidates.Contains(option.TableName, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The selected dataset cannot be exported as an importable package.");

        var fields = model.Columns.Select(column => column.HeaderTextKey).ToArray();
        var rows = model.Rows.Select(row => (IReadOnlyDictionary<string, object?>)fields
            .Select((field, index) => new KeyValuePair<string, object?>(
                field,
                row.TryGetValue(model.Columns[index].Key, out var value) ? value : null))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return SteiSvc.ExportDatasetAsync(
            path, password, option.Key, option.TableName, rows, fields, progress, ct);
    }

    internal static Task<SteiResult> ImportDatasetPackageAsync(
        string path,
        string password,
        DataExportOption option,
        bool simulation = false,
        IProgress<(long done, long total)>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (option.IsSystemInfo || !Catalog.Any(x => x.Key == option.Key && x.Filter == option.Filter &&
                x.TableCandidates.Contains(option.TableName, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The selected dataset cannot be imported from a package.");
        if (!IsMasterDataImportAllowed(option))
            throw new InvalidOperationException(
                "Encrypted dataset imports are limited to approved master-data datasets. Transactional records must use their business workflows.");
        return SteiSvc.ImportDatasetAsync(
            path, password, option.Key, option.TableName, simulation, progress, ct);
    }

    public static string GetDatasetPackageFileName(DataExportOption option)
        => $"{option.Key switch
        {
            "Sales" => "inv",
            "SalesLines" => "inv-lines",
            "SalesReturns" => "return",
            "SalesReturnLines" => "return-lines",
            "Purchases" => "pur",
            "PurchaseLines" => "pur-lines",
            "PurchaseReturns" => "pur-return",
            "PurchaseReturnLines" => "pur-return-lines",
            "Products" or "ProductClasses" or "ProductAttributes" or "ProductClassAttributes" or "ProductSuppliers" or "ProductBarcodes" or "ProductUnits" or "Brands" => "items",
            "Customers" or "CustomerGroups" or "CustomerContacts" or "CustomerAddresses" => "customers",
            "Accounts" or "AccountTypes" => "accounts",
            "SalesRepresentatives" or "SalesRepresentativeTargets" => "reps",
            "Employees" or "Departments" or "JobTitles" => "employees",
            "Company" or "Branches" => "company",
            "Suppliers" or "SupplierGroups" or "SupplierContacts" => "suppliers",
            "Warehouses" or "WarehouseLocations" or "WarehouseZones" or "WarehouseTypes" => "warehouse",
            _ => option.Key.ToLowerInvariant()
        }}.stei";

    private static string NormalizeEncryptedType(string? type)
        => type switch
        {
            "Integer" => RptColType.WholeNumber.ToString(),
            "Decimal" => RptColType.FractionalNumber.ToString(),
            not null when Enum.TryParse<RptColType>(type, out var parsed) => parsed.ToString(),
            _ => throw new InvalidDataException("The encrypted dataset contains an unsupported column type.")
        };

    private static async Task<ExcelImportResult> ImportRowsAsync(
        ExcelSheet sheet,
        DatasetIdentity identity,
        DataExportOption option,
        string companyCode,
        int identityColumnIndex,
        bool replaceMode,
        CancellationToken ct)
    {
        if (identity.Headers.Length == 0 ||
            identity.Headers.Length != identity.Fields.Length ||
            identity.Headers.Length != identity.Types.Length ||
            identity.Headers.Distinct(StringComparer.Ordinal).Count() != identity.Headers.Length)
            throw new InvalidDataException("The workbook dataset schema is invalid.");

        var visibleHeaders = sheet.Headers
            .Where((_, index) => index != identityColumnIndex)
            .ToArray();
        if (visibleHeaders.Length != identity.Headers.Length ||
            !visibleHeaders.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(identity.Headers.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("The Excel column headers do not match this ShouTech export. Keep the original column names unchanged; edit only the data cells, and select the same dataset type that was exported.");

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        var tableNames = await GetTableNamesAsync(connection, ct);
        if (!tableNames.Contains(option.TableName))
            throw new InvalidOperationException("The dataset table is not available in the current database.");

        var columns = await GetColumnNamesAsync(connection, option.TableName, ct);
        var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < identity.Headers.Length; i++)
        {
            if (!columns.Contains(identity.Fields[i], StringComparer.OrdinalIgnoreCase) ||
                !fieldNames.Add(identity.Fields[i]))
                throw new InvalidDataException("The workbook fields do not match the selected dataset.");
        }

        var keyColumns = await GetPrimaryKeyColumnsAsync(connection, option.TableName, ct);
        if (keyColumns.Count == 0 || keyColumns.Any(key => !identity.Fields.Contains(key, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("The workbook does not contain the complete primary key required for a safe import.");

        var companyColumn = columns.FirstOrDefault(x =>
            x.Equals("company_id", StringComparison.OrdinalIgnoreCase) ||
            x.Equals("companyid", StringComparison.OrdinalIgnoreCase));
        // Legacy tenant tables store the company CODE in company_id; newer tables
        // store the internal companies.company_id. Do not consult the global registry
        // for legacy code-scoped tables or truly global datasets: a valid company-
        // scoped database may intentionally have an empty companies registry.
        var companyCodeScoped = IsCompanyCodeScopedTable(option.TableName);
        var companyId = companyColumn is not null && !companyCodeScoped
            ? await FindCompanyIdAsync(connection, companyCode, ct)
            : null;
        var companyScopeValue = companyCodeScoped ? companyCode.Trim() : companyId;
        if (companyColumn != null && companyScopeValue == null)
            throw new InvalidOperationException("The active company could not be matched to the current database.");
        if (companyColumn == null && !IsCompanyIndependent(option.TableName))
            throw new InvalidOperationException("This dataset cannot be safely restricted to the active company, so it was not imported.");

        var headerToField = identity.Headers
            .Select((header, i) => new { header, field = identity.Fields[i], type = identity.Types[i] })
            .ToDictionary(x => x.header, x => (x.field, x.type), StringComparer.Ordinal);
        var columnIndexes = sheet.Headers
            .Select((header, index) => (header, index))
            .Where(x => x.index != identityColumnIndex)
            .ToDictionary(x => x.header, x => x.index, StringComparer.Ordinal);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            var processed = 0;
            var importedKeys = new List<string>();
            for (var rowIndex = 0; rowIndex < sheet.Rows.Count; rowIndex++)
            {
                ct.ThrowIfCancellationRequested();
                var sourceRow = sheet.Rows[rowIndex];
                if (identityColumnIndex >= 0 && identityColumnIndex < sourceRow.Count &&
                    !string.IsNullOrWhiteSpace(sourceRow[identityColumnIndex]))
                    throw new InvalidDataException("The hidden dataset identity column must not contain row data.");
                if (sourceRow.All(string.IsNullOrWhiteSpace))
                    continue;

                var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var mapping in headerToField)
                {
                    var cellIndex = columnIndexes[mapping.Key];
                    var cell = cellIndex < sourceRow.Count ? sourceRow[cellIndex] : null;
                    values[mapping.Value.field] = ParseExcelValue(cell, mapping.Value.type);
                }

                if (companyColumn != null)
                    values[companyColumn] = companyScopeValue!;

                try
                {
                    ValidateDatasetRow(option, values);
                    processed += await UpsertExcelRowAsync(
                        connection, transaction, option.TableName, values, columns, keyColumns, ct);
                    if (replaceMode)
                        importedKeys.Add(BuildCompositeKey(values, keyColumns));
                }
                catch (Exception ex)
                {
                    LogError($"Import row failed | key={option.Key} | table={option.TableName} | sheetRow={rowIndex + 2} | {ex.Message}", ex);
                    throw new InvalidDataException($"Import stopped at spreadsheet row {rowIndex + 2}: {ex.Message}", ex);
                }
            }

            var deleted = 0;
            if (replaceMode)
            {
                // Database rows for this company that are absent from the file are removed.
                // Product/material tables prefer soft-delete (is_deleted) when the column exists.
                deleted = await DeleteRowsNotInImportAsync(
                    connection, transaction, option.TableName, keyColumns,
                    companyColumn, companyScopeValue, importedKeys, ct);
                LogInfo($"Replace cleanup | key={option.Key} | table={option.TableName} | upserted={processed} | removed={deleted} | company={companyCode}");
            }

            await transaction.CommitAsync(ct);
            return new ExcelImportResult(processed, deleted, replaceMode, option.Key, option.TableName);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static string BuildCompositeKey(Dictionary<string, object?> values, IReadOnlyList<string> keyColumns)
    {
        var parts = new string[keyColumns.Count];
        for (var i = 0; i < keyColumns.Count; i++)
        {
            values.TryGetValue(keyColumns[i], out var value);
            parts[i] = value?.ToString() ?? string.Empty;
        }
        return string.Join("\u001f", parts);
    }

    private static async Task<int> DeleteRowsNotInImportAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        IReadOnlyList<string> keyColumns,
        string? companyColumn,
        object? companyId,
        IReadOnlyList<string> keepKeys,
        CancellationToken ct)
    {
        if (keyColumns.Count == 0)
            return 0;

        // Replace cleanup must never run without an explicit company scope.
        // This also blocks accidental cross-company cleanup for shared/global tables.
        if (string.IsNullOrWhiteSpace(companyColumn) || companyId is null)
            throw new InvalidOperationException(
                $"Replace mode cannot safely remove missing rows from '{table}' without a verified company scope. Use upsert mode instead.");

        var columns = await GetColumnNamesAsync(connection, table, ct);
        var softCol = columns.FirstOrDefault(c => c.Equals("is_deleted", StringComparison.OrdinalIgnoreCase));
        if (softCol is null)
            throw new InvalidOperationException(
                $"Replace mode cannot remove missing rows from '{table}' because the table does not support soft deletion. Use upsert mode instead.");

        var deletedAtCol = columns.FirstOrDefault(c => c.Equals("deleted_at", StringComparison.OrdinalIgnoreCase));
        var keep = new HashSet<string>(keepKeys, StringComparer.Ordinal);

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        var keySelect = string.Join(", ", keyColumns.Select(QuoteIdentifier));
        var where = new List<string>
        {
            $"{QuoteIdentifier(companyColumn!)} = @companyId",
            $"{QuoteIdentifier(softCol)} = {(DatabaseRuntime.IsPostgreSql() ? "FALSE" : "0")}"
        };
        var companyParameter = select.CreateParameter();
        companyParameter.ParameterName = "@companyId";
        companyParameter.Value = companyId;
        select.Parameters.Add(companyParameter);

        select.CommandText =
            $"SELECT {keySelect} FROM {QuoteIdentifier(table)}" +
            (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where));

        var toRemove = new List<object?[]>();
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var parts = new string[keyColumns.Count];
                var raw = new object?[keyColumns.Count];
                for (var i = 0; i < keyColumns.Count; i++)
                {
                    raw[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    parts[i] = raw[i]?.ToString() ?? string.Empty;
                }
                if (!keep.Contains(string.Join("\u001f", parts)))
                    toRemove.Add(raw);
            }
        }

        var deleted = 0;
        foreach (var rawKeys in toRemove)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            var predicates = new List<string>();
            for (var i = 0; i < keyColumns.Count; i++)
            {
                var name = $"@k{i}";
                predicates.Add($"{QuoteIdentifier(keyColumns[i])} = {name}");
                var p = cmd.CreateParameter();
                p.ParameterName = name;
                p.Value = rawKeys[i] ?? DBNull.Value;
                cmd.Parameters.Add(p);
            }
            predicates.Add($"{QuoteIdentifier(companyColumn!)} = @companyId");
            var removeCompanyParameter = cmd.CreateParameter();
            removeCompanyParameter.ParameterName = "@companyId";
            removeCompanyParameter.Value = companyId;
            cmd.Parameters.Add(removeCompanyParameter);

            {
                var deletedByCol = columns.FirstOrDefault(c => c.Equals("deleted_by", StringComparison.OrdinalIgnoreCase));
                var sets = new List<string>
                {
                    $"{QuoteIdentifier(softCol)} = {(DatabaseRuntime.IsPostgreSql() ? "TRUE" : "1")}"
                };
                if (deletedAtCol != null)
                    sets.Add($"{QuoteIdentifier(deletedAtCol)} = @deletedAt");
                if (deletedByCol != null)
                    sets.Add($"{QuoteIdentifier(deletedByCol)} = @deletedBy");
                cmd.CommandText =
                    $"UPDATE {QuoteIdentifier(table)} SET {string.Join(", ", sets)} WHERE {string.Join(" AND ", predicates)}";
                if (deletedAtCol != null)
                {
                    var pAt = cmd.CreateParameter();
                    pAt.ParameterName = "@deletedAt";
                    pAt.Value = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                    cmd.Parameters.Add(pAt);
                }
                if (deletedByCol != null)
                {
                    var pBy = cmd.CreateParameter();
                    pBy.ParameterName = "@deletedBy";
                    pBy.Value = AppState.CurrentUser?.Username ?? AppState.CurrentUser?.FullName ?? "system";
                    cmd.Parameters.Add(pBy);
                }
            }

            deleted += await cmd.ExecuteNonQueryAsync(ct);
        }

        return deleted;
    }

    private static void ValidateDatasetRow(

        DataExportOption option,
        IReadOnlyDictionary<string, object?> row)
    {
        if ((option.Filter is DataExportFilter.Sales or DataExportFilter.Returns) &&
            (option.TableName.Equals("pos_transactions", StringComparison.OrdinalIgnoreCase) ||
             option.TableName.Equals("postransactions", StringComparison.OrdinalIgnoreCase)))
        {
            var type = GetRowValue(row, "transaction_type")?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(type) && IsReturnType(type) != (option.Filter == DataExportFilter.Returns))
                throw new InvalidDataException("This row's transaction type does not match the selected sales/returns dataset.");
        }

        if (option.Filter == DataExportFilter.OpeningStock &&
            option.TableName.Contains("stock", StringComparison.OrdinalIgnoreCase))
        {
            var type = GetRowValue(row, "movement_type")?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(type) && !IsOpeningStockType(type))
                throw new InvalidDataException("This movement is not an opening-stock record.");
        }
    }

    private static object? GetRowValue(IReadOnlyDictionary<string, object?> row, string column)
        => row.FirstOrDefault(x => x.Key.Equals(column, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool IsReturnType(string value)
    {
        var normalized = value.ToLowerInvariant();
        return normalized.Contains("return", StringComparison.Ordinal) ||
            normalized.Contains("refund", StringComparison.Ordinal) ||
            value.Contains("مرتجع", StringComparison.Ordinal);
    }

    private static bool IsOpeningStockType(string value)
        => value.Contains("open", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("افتتاح", StringComparison.Ordinal) ||
            value.Contains("أول", StringComparison.Ordinal);

    private const string DatasetIdentityPrefix = "__SHOUTECH_DATASET_V1__:";
    private const int DatasetIdentityVersion = 1;

    private static string CreateDatasetIdentityHeader(
        DataExportOption option,
        IReadOnlyList<RptCol> columns,
        Dictionary<string, string> translatedHeaders)
    {
        var identity = new DatasetIdentity(
            DatasetIdentityVersion,
            option.Key,
            option.TableName,
            columns.Select(c => translatedHeaders.TryGetValue(c.HeaderTextKey, out var label) ? label : c.HeaderTextKey).ToArray(),
            columns.Select(c => c.HeaderTextKey).ToArray(),
            columns.Select(c => c.Type.ToString()).ToArray());
        var encrypted = XptCry.Encrypt(
            JsonSerializer.SerializeToUtf8Bytes(identity),
            XptCry.KindDatasetIdentity);
        return DatasetIdentityPrefix + Convert.ToBase64String(encrypted);
    }

    private static XptServ.ExportModel BuildSystemInfoModel(
        DataExportOption option,
        string companyCode,
        string companyName,
        string generatedBy)
    {
        var connectionString = DatabaseRuntime.GetConnectionString();
        var databaseName = DatabaseRuntime.IsSqlite()
            ? Path.GetFileName(DatabaseRuntime.GetSqliteDataSource(connectionString ?? string.Empty) ?? "-")
            : DatabaseRuntime.IsPostgreSql()
                ? new NpgsqlConnectionStringBuilder(connectionString).Database ?? "-"
                : "-";
        var safeCompanyCode = new string(companyCode
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)
            .ToArray()).Trim('.');
        var backupDirectory = AppPaths.CompanyBackup(
            string.IsNullOrWhiteSpace(safeCompanyCode) ? "default" : safeCompanyCode);
        var backupCount = Directory.Exists(backupDirectory)
            ? Directory.EnumerateFiles(backupDirectory, "*", SearchOption.TopDirectoryOnly).Count()
            : 0;
        var values = new object?[]
        {
            companyCode,
            companyName,
            databaseName,
            DatabaseRuntime.GetProvider(),
            backupCount,
            Environment.UserName,
            DateTime.Now
        };
        var fields = new[] { "CompanyCode", "CompanyName", "DatabaseName", "Provider", "BackupCount", "User", "GeneratedAt" };
        var columns = fields.Select((field, index) => new RptCol
        {
            Key = $"Column{index + 1}",
            HeaderTextKey = field,
            Type = index == 4 ? RptColType.WholeNumber : index == 6 ? RptColType.DateTime : RptColType.Text,
            Width = 150
        }).ToArray();
        var row = columns.Select((column, index) => (column.Key, values[index]))
            .ToDictionary(x => x.Key, x => x.Item2, StringComparer.Ordinal);
        var labels = fields.ToDictionary(
            field => field,
            field => AppServiceHost.Localization.GetString(
                $"DataExchange.Columns.{field}", Humanize(field)),
            StringComparer.Ordinal);

        return new XptServ.ExportModel
        {
            ReportKey = "DataExport.SystemInfo",
            Title = option.DisplayName,
            CompanyName = companyName,
            GeneratedBy = generatedBy,
            FilterSummary = "1 record",
            Columns = columns,
            Rows = [row],
            Rtl = true,
            Translate = key => labels.TryGetValue(key, out var label) ? label : key
        };
    }

    private static bool IsMasterDataImportAllowed(DataExportOption option)
        => option.Key is
            "Branches" or "Accounts" or "AccountTypes" or "Products" or "ProductClasses" or
            "Brands" or "ProductAttributes" or "ProductClassAttributes" or "ProductSuppliers" or
            "ProductBarcodes" or "ProductUnits" or "Customers" or "CustomerGroups" or
            "CustomerContacts" or "CustomerAddresses" or "Suppliers" or "SupplierGroups" or
            "SupplierContacts" or "SalesRepresentatives" or "SalesRepresentativeTargets" or
            "Employees" or "Departments" or "JobTitles" or "Warehouses" or
            "WarehouseLocations" or "WarehouseZones" or "WarehouseTypes" or "Units" or
            "Currencies" or "CurrencyRates" or "Banks" or "BankAccounts" or "TaxCodes";

    private static async Task<DatasetIdentity> CreateGenericDatasetIdentityAsync(
        ExcelSheet sheet,
        DataExportOption option,
        CancellationToken ct)
    {
        if (sheet.Headers.Count == 0 ||
            sheet.Headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != sheet.Headers.Count)
            throw new InvalidDataException("The generic Excel file must have unique, non-empty headers.");

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        var columns = await GetColumnNamesAsync(connection, option.TableName, ct);
        if (columns.Count == 0)
            throw new InvalidOperationException("The selected dataset table is not available in the current database.");

        var fields = new List<string>(sheet.Headers.Count);
        foreach (var header in sheet.Headers)
        {
            var field = columns.FirstOrDefault(column =>
                string.Equals(column, header.Trim(), StringComparison.OrdinalIgnoreCase));
            if (field is null)
                throw new InvalidDataException(
                    $"Excel column '{header}' does not match a database field in '{option.TableName}'. For a generic workbook, use database field names as the first-row headers.");

            fields.Add(field);
        }

        var types = await GetColumnImportTypesAsync(connection, option.TableName, ct);
        var mappedTypes = fields.Select(field =>
        {
            if (!types.TryGetValue(field, out var databaseType))
                throw new InvalidDataException($"The database type for field '{field}' could not be determined.");
            return databaseType;
        }).ToArray();

        return new DatasetIdentity(
            DatasetIdentityVersion,
            option.Key,
            option.TableName,
            sheet.Headers.ToArray(),
            fields.ToArray(),
            mappedTypes);
    }

    private static async Task<Dictionary<string, string>> GetColumnImportTypesAsync(
        DbConnection connection,
        string table,
        CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        if (DatabaseRuntime.IsSqlite())
        {
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)})";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(1);
                var sqlType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                result[name] = NormalizeDatabaseImportType(name, sqlType);
            }
            return result;
        }

        command.CommandText = """
            SELECT column_name, data_type, udt_name
            FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = @table
            ORDER BY ordinal_position
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@table";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                var sqlType = reader.GetString(1);
                var underlyingType = reader.GetString(2);
                result[name] = NormalizeDatabaseImportType(name, sqlType + " " + underlyingType);
            }
        }
        return result;
    }

    private static string NormalizeDatabaseImportType(string columnName, string databaseType)
    {
        var type = databaseType.ToLowerInvariant();
        var name = columnName.ToLowerInvariant();
        if (type.Contains("bool", StringComparison.Ordinal) ||
            ((name.StartsWith("is_", StringComparison.Ordinal) ||
              name.StartsWith("has_", StringComparison.Ordinal) ||
              name.StartsWith("allow_", StringComparison.Ordinal) ||
              name.StartsWith("can_", StringComparison.Ordinal) ||
              name.StartsWith("enable_", StringComparison.Ordinal)) &&
             (type.Contains("int", StringComparison.Ordinal) || type.Contains("bit", StringComparison.Ordinal))))
            return RptColType.Boolean.ToString();
        if (type.Contains("timestamp", StringComparison.Ordinal) || type.Contains("datetime", StringComparison.Ordinal))
            return RptColType.DateTime.ToString();
        if (type.Contains("date", StringComparison.Ordinal) || type.Contains("time", StringComparison.Ordinal))
            return RptColType.Date.ToString();
        if (type.Contains("int", StringComparison.Ordinal) || type.Contains("serial", StringComparison.Ordinal))
            return RptColType.WholeNumber.ToString();
        if (type.Contains("numeric", StringComparison.Ordinal) || type.Contains("decimal", StringComparison.Ordinal) ||
            type.Contains("real", StringComparison.Ordinal) || type.Contains("double", StringComparison.Ordinal) ||
            type.Contains("float", StringComparison.Ordinal))
        {
            return name.Contains("amount", StringComparison.Ordinal) || name.Contains("price", StringComparison.Ordinal) ||
                   name.Contains("cost", StringComparison.Ordinal) || name.Contains("balance", StringComparison.Ordinal) ||
                   name.Contains("total", StringComparison.Ordinal)
                ? RptColType.Money.ToString()
                : RptColType.FractionalNumber.ToString();
        }
        return RptColType.Text.ToString();
    }

    private static ExcelSheet ReadExcelSheet(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only .xlsx workbooks can be imported as editable datasets.");

        using var archive = ZipFile.OpenRead(path);
        const long maxUncompressedBytes = 64L * 1024 * 1024;
        const int maxArchiveEntries = 4096;
        if (archive.Entries.Count > maxArchiveEntries ||
            archive.Entries.Sum(entry => entry.Length) > maxUncompressedBytes)
            throw new InvalidDataException("The Excel workbook exceeds the supported package size limits.");

        var sheetEntry = ResolveFirstWorksheetEntry(archive);
        if (sheetEntry.Length > 32L * 1024 * 1024)
            throw new InvalidDataException("The selected worksheet is too large to import safely.");

        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry is not null && sharedStringsEntry.Length > 24L * 1024 * 1024)
            throw new InvalidDataException("The workbook shared-string table is too large to import safely.");

        var sharedStrings = ReadSharedStrings(archive);
        var dateStyleIndexes = ReadDateStyleIndexes(archive);
        using var stream = sheetEntry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = document.Root?.Element(ns + "sheetData")?.Elements(ns + "row").ToArray()
            ?? throw new InvalidDataException("The selected worksheet is empty.");

        if (rows.Length > 100_001)
            throw new InvalidDataException("The workbook exceeds the 100,000 data-row import limit.");

        var matrix = new List<IReadOnlyList<string?>>();
        var maximumColumn = -1;
        foreach (var row in rows)
        {
            var cells = new Dictionary<int, string?>();
            foreach (var cell in row.Elements(ns + "c"))
            {
                if (cell.Element(ns + "f") is not null)
                    throw new InvalidDataException("Formula cells are not supported for import. Replace formulas with their intended values first.");

                var reference = (string?)cell.Attribute("r")
                    ?? throw new InvalidDataException("A workbook cell has no column reference.");
                var index = ExcelColumnIndex(reference);
                if (index >= 512)
                    throw new InvalidDataException("The workbook exceeds the 512-column import limit.");
                var type = (string?)cell.Attribute("t");
                var rawValue = cell.Element(ns + "v")?.Value;
                string? value = type switch
                {
                    "inlineStr" => string.Concat(cell.Element(ns + "is")?.Descendants(ns + "t").Select(x => x.Value) ?? []),
                    "s" => SharedStringValue(rawValue, sharedStrings),
                    "b" => rawValue == "1" ? "true" : "false",
                    "str" => rawValue,
                    "e" => throw new InvalidDataException($"Cell {reference} contains an Excel formula error."),
                    _ => rawValue
                };

                if (type is null or "n")
                {
                    var styleText = (string?)cell.Attribute("s");
                    if (value is not null &&
                        int.TryParse(styleText, NumberStyles.None, CultureInfo.InvariantCulture, out var styleIndex) &&
                        dateStyleIndexes.Contains(styleIndex) &&
                        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
                    {
                        try { value = DateTime.FromOADate(serial).ToString("O", CultureInfo.InvariantCulture); }
                        catch (ArgumentException) { throw new InvalidDataException($"Cell {reference} contains an invalid Excel date."); }
                    }
                }

                if (!cells.TryAdd(index, value))
                    throw new InvalidDataException($"Row {((string?)row.Attribute("r") ?? "?")} contains duplicate cells for column {index + 1}.");
                maximumColumn = Math.Max(maximumColumn, index);
            }

            var values = new string?[maximumColumn + 1];
            foreach (var pair in cells)
                values[pair.Key] = pair.Value;
            matrix.Add(values);
        }

        if (matrix.Count == 0 || matrix[0].Count == 0 || matrix[0].Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("The workbook header row is missing or contains an empty column name.");

        var headers = matrix[0].Select(x => x!.Trim()).ToArray();
        if (headers.Any(string.IsNullOrWhiteSpace) ||
            headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new InvalidDataException("The workbook header row contains blank or duplicate column names.");

        foreach (var row in matrix.Skip(1))
        {
            if (row.Skip(headers.Length).Any(value => !string.IsNullOrWhiteSpace(value)))
                throw new InvalidDataException("A data row contains values beyond the last header column.");
        }

        return new ExcelSheet(headers, matrix.Skip(1).ToArray());
    }

    private static ZipArchiveEntry ResolveFirstWorksheetEntry(ZipArchive archive)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException("The XLSX package does not contain xl/workbook.xml.");
        XDocument workbook;
        using (var stream = workbookEntry.Open()) workbook = XDocument.Load(stream);
        var relationshipId = (string?)workbook.Root?
            .Element(main + "sheets")?
            .Elements(main + "sheet")
            .FirstOrDefault()?
            .Attribute(rel + "id");
        if (string.IsNullOrWhiteSpace(relationshipId))
            throw new InvalidDataException("The workbook does not declare a worksheet.");

        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("The XLSX package is missing workbook relationships.");
        XDocument relationships;
        using (var stream = relationshipsEntry.Open()) relationships = XDocument.Load(stream);
        var target = (string?)relationships.Root?
            .Elements(packageRel + "Relationship")
            .FirstOrDefault(x => string.Equals((string?)x.Attribute("Id"), relationshipId, StringComparison.Ordinal))?
            .Attribute("Target");
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidDataException("The first worksheet relationship is missing or invalid.");

        var segments = (target.StartsWith("/", StringComparison.Ordinal) ? target.TrimStart('/') : "xl/" + target)
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>();
        foreach (var segment in segments)
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (normalized.Count == 0) throw new InvalidDataException("The worksheet path escapes the XLSX package.");
                normalized.RemoveAt(normalized.Count - 1);
            }
            else normalized.Add(segment);
        }

        var entry = archive.GetEntry(string.Join("/", normalized));
        return entry ?? throw new InvalidDataException("The worksheet referenced by the workbook could not be found.");
    }

    private static HashSet<int> ReadDateStyleIndexes(ZipArchive archive)
    {
        var result = new HashSet<int>();
        var entry = archive.GetEntry("xl/styles.xml");
        if (entry is null) return result;

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var customFormats = document.Root?.Element(ns + "numFmts")?
            .Elements(ns + "numFmt")
            .ToDictionary(
                x => (uint?)x.Attribute("numFmtId") ?? 0,
                x => (string?)x.Attribute("formatCode") ?? string.Empty) ?? new Dictionary<uint, string>();
        var cellFormats = document.Root?.Element(ns + "cellXfs")?.Elements(ns + "xf").ToArray() ?? [];
        for (var index = 0; index < cellFormats.Length; index++)
        {
            var formatId = (uint?)cellFormats[index].Attribute("numFmtId") ?? 0;
            if ((formatId >= 14 && formatId <= 22) || (formatId >= 45 && formatId <= 47) ||
                (customFormats.TryGetValue(formatId, out var format) && IsDateNumberFormat(format)))
                result.Add(index);
        }
        return result;
    }

    private static bool IsDateNumberFormat(string format)
    {
        var cleaned = Regex.Replace(format, "\"[^\"]*\"|\\.|_.|\\*.", string.Empty);
        return Regex.IsMatch(cleaned, "[ymdhis]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string[] ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null)
            return Array.Empty<string>();

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Root?.Elements(ns + "si")
            .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value)))
            .ToArray() ?? [];
    }

    private static string? SharedStringValue(string? indexText, IReadOnlyList<string> sharedStrings)
    {
        if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index < 0 || index >= sharedStrings.Count)
            throw new InvalidDataException("The workbook contains an invalid shared-string reference.");
        return sharedStrings[index];
    }

    private static int ExcelColumnIndex(string cellReference)
    {
        var value = 0;
        var foundColumn = false;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
                break;
            foundColumn = true;
            value = checked(value * 26 + (char.ToUpperInvariant(character) - 'A' + 1));
        }
        if (!foundColumn)
            throw new InvalidDataException($"Invalid Excel cell reference: {cellReference}.");
        return value - 1;
    }

    private static object? ParseExcelValue(string? value, string type)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Enum.TryParse<RptColType>(type, out var columnType))
            throw new InvalidDataException("The workbook contains an unsupported data type.");

        return columnType switch
        {
            RptColType.WholeNumber => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                ? whole
                : throw new InvalidDataException($"'{value}' is not a valid whole number."),
            RptColType.FractionalNumber or RptColType.Money => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? number
                : throw new InvalidDataException($"'{value}' is not a valid number."),
            RptColType.Boolean => value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1"
                ? true
                : value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0"
                    ? false
                    : throw new InvalidDataException($"'{value}' is not a valid Boolean value."),
            RptColType.Date or RptColType.DateTime => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)
                ? date
                : throw new InvalidDataException($"'{value}' is not a valid date or date-time value."),
            _ => value
        };
    }

    private static async Task<List<string>> GetColumnNamesAsync(DbConnection connection, string table, CancellationToken ct)
    {
        var columns = new List<string>();
        if (DatabaseRuntime.IsSqlite())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)})";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                columns.Add(reader.GetString(1));
            return columns;
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT column_name FROM information_schema.columns WHERE table_schema=current_schema() AND table_name=@table ORDER BY ordinal_position";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = table;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                columns.Add(reader.GetString(0));
        }
        return columns;
    }

    private static async Task<List<string>> GetPrimaryKeyColumnsAsync(DbConnection connection, string table, CancellationToken ct)
    {
        var keys = new List<string>();
        await using var command = connection.CreateCommand();
        if (DatabaseRuntime.IsSqlite())
        {
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)})";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (reader.GetInt32(5) > 0)
                    keys.Add(reader.GetString(1));
        }
        else
        {
            command.CommandText = """
                SELECT kcu.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON tc.constraint_name = kcu.constraint_name
                 AND tc.table_schema = kcu.table_schema
                 AND tc.table_name = kcu.table_name
                WHERE tc.constraint_type = 'PRIMARY KEY'
                  AND tc.table_schema = current_schema()
                  AND tc.table_name = @table
                ORDER BY kcu.ordinal_position
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = table;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                keys.Add(reader.GetString(0));
        }
        return keys;
    }

    private static async Task<int> UpsertExcelRowAsync(
        DbConnection connection,
        DbTransaction transaction,
        string table,
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> primaryKeys,
        CancellationToken ct)
    {
        var selected = columns.Where(row.ContainsKey).ToArray();
        selected = selected.Where(column =>
            !primaryKeys.Contains(column, StringComparer.OrdinalIgnoreCase) || row[column] != null).ToArray();
        var nonKeys = selected.Where(column => !primaryKeys.Contains(column, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (selected.Length == 0)
            throw new InvalidDataException("A row contains no importable fields.");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var placeholders = selected.Select((_, index) => $"@p{index}").ToArray();
        const string prefix = "INSERT";
        var suffix = nonKeys.Length == 0
            ? " ON CONFLICT DO NOTHING"
            : DatabaseRuntime.IsPostgreSql()
                ? " ON CONFLICT DO UPDATE SET " + string.Join(",", nonKeys.Select(column =>
                    $"{QuoteIdentifier(column)}=EXCLUDED.{QuoteIdentifier(column)}"))
                : " ON CONFLICT DO UPDATE SET " + string.Join(",", nonKeys.Select(column =>
                    $"{QuoteIdentifier(column)}=excluded.{QuoteIdentifier(column)}"));
        command.CommandText = $"{prefix} INTO {QuoteIdentifier(table)} ({string.Join(",", selected.Select(QuoteIdentifier))}) VALUES ({string.Join(",", placeholders)}){suffix}";
        for (var i = 0; i < selected.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = placeholders[i];
            parameter.Value = row[selected[i]] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static DbConnection CreateConnection()
    {
        var connectionString = DatabaseRuntime.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The active database connection is not configured.");

        if (DatabaseRuntime.IsSqlite())
            return new SqliteConnection(connectionString);
        if (DatabaseRuntime.IsPostgreSql())
            return new NpgsqlConnection(connectionString);
        throw new NotSupportedException("The configured database provider cannot export tabular data.");
    }

    private static async Task<HashSet<string>> GetTableNamesAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DatabaseRuntime.IsSqlite()
            ? "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"
            : "SELECT table_name FROM information_schema.tables WHERE table_schema=current_schema() AND table_type='BASE TABLE'";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
            names.Add(reader.GetString(0));
        return names;
    }

    private static bool IsCompanyCodeScopedTable(string tableName)
    {
        // Must stay aligned with the legacy company_id convention in RecycleBinSvc.
        // Unknown tables default to internal-ID mapping (fail-closed when unresolved).
        return tableName.Equals("sales_documents", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("purchase_documents", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("materials", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("material_groups", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("customers", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("suppliers", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("accounts", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("chart_of_accounts", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("branches", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("warehouses", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("brands", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("product_categories", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("units_of_measure", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("employees", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<object?> FindCompanyIdAsync(DbConnection connection, string companyCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(companyCode))
            return null;

        var tables = await GetTableNamesAsync(connection, ct);
        var companyTable = tables.FirstOrDefault(x => string.Equals(x, "companies", StringComparison.OrdinalIgnoreCase));

        // Some development/tenant databases are already scoped to one company and
        // intentionally do not carry the global companies registry. In that model,
        // company_id values in tenant tables are the active company code itself.
        if (companyTable == null)
            return companyCode.Trim();

        var companyColumns = await GetColumnNamesAsync(connection, companyTable, ct);
        var idColumn = companyColumns.FirstOrDefault(x => x.Equals("company_id", StringComparison.OrdinalIgnoreCase));
        var codeColumn = companyColumns.FirstOrDefault(x => x.Equals("company_code", StringComparison.OrdinalIgnoreCase));
        if (idColumn == null || codeColumn == null)
            throw new InvalidOperationException(
                $"The '{companyTable}' table exists in the current database but does not contain the required company_id and company_code columns. Check the database schema or apply the pending migrations.");

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {QuoteIdentifier(idColumn)} FROM {QuoteIdentifier(companyTable)} " +
            $"WHERE TRIM({QuoteIdentifier(codeColumn)}) = @companyCode OR TRIM({QuoteIdentifier(idColumn)}) = @companyCode LIMIT 1";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@companyCode";
        parameter.Value = companyCode.Trim();
        command.Parameters.Add(parameter);
        var value = await command.ExecuteScalarAsync(ct);
        if (value is null or DBNull)
        {
            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(companyTable)}";
            var countValue = await countCommand.ExecuteScalarAsync(ct);
            var registeredCompanies = countValue is null or DBNull
                ? 0L
                : Convert.ToInt64(countValue, CultureInfo.InvariantCulture);

            if (registeredCompanies == 0)
                throw new InvalidOperationException(
                    $"The '{companyTable}' registry is empty. Initialize/register company '{companyCode}' before importing datasets that use internal company IDs. Legacy company-code datasets can still be imported without this registry.");

            throw new InvalidOperationException(
                $"Company '{companyCode}' is not registered in the current database. Verify that the selected company and database belong together, or run the company/database initialization step.");
        }
        return value;
    }

    private static string BuildFilter(DataExportOption option)
    {
        if (string.Equals(option.TableName, "pos_transactions", StringComparison.OrdinalIgnoreCase))
        {
            return option.Filter switch
            {
                DataExportFilter.Sales => " WHERE " + TransactionTypeFilter(returns: false),
                DataExportFilter.Returns => " WHERE " + TransactionTypeFilter(returns: true),
                _ => string.Empty
            };
        }

        if (string.Equals(option.TableName, "pos_transaction_items", StringComparison.OrdinalIgnoreCase))
        {
            return option.Filter switch
            {
                DataExportFilter.Sales => " WHERE pos_transaction_id IN (SELECT pos_transaction_id FROM pos_transactions WHERE " + TransactionTypeFilter(returns: false) + ")",
                DataExportFilter.Returns => " WHERE pos_transaction_id IN (SELECT pos_transaction_id FROM pos_transactions WHERE " + TransactionTypeFilter(returns: true) + ")",
                _ => string.Empty
            };
        }

        if (option.Filter == DataExportFilter.OpeningStock &&
            (string.Equals(option.TableName, "stock_movements", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(option.TableName, "material_stock_movements", StringComparison.OrdinalIgnoreCase)))
            return " WHERE LOWER(CAST(movement_type AS TEXT)) LIKE '%open%' OR CAST(movement_type AS TEXT) LIKE '%افتتاح%' OR CAST(movement_type AS TEXT) LIKE '%أول%'";

        return string.Empty;
    }

    private static bool IsCompanyIndependent(string tableName)
        => tableName.Equals("companies", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("currencies", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("account_types", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("units_of_measure", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("tax_codes", StringComparison.OrdinalIgnoreCase);

    private static string TransactionTypeFilter(bool returns)
        => returns
            ? "(LOWER(CAST(transaction_type AS TEXT)) LIKE '%return%' OR LOWER(CAST(transaction_type AS TEXT)) LIKE '%refund%' OR CAST(transaction_type AS TEXT) LIKE '%مرتجع%')"
            : "(LOWER(CAST(transaction_type AS TEXT)) NOT LIKE '%return%' AND LOWER(CAST(transaction_type AS TEXT)) NOT LIKE '%refund%' AND CAST(transaction_type AS TEXT) NOT LIKE '%مرتجع%')";

    private static string QuoteIdentifier(string value)
        => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static RptColType GetColumnType(string columnName, Type valueType)
    {
        var name = columnName.ToLowerInvariant();
        if (valueType == typeof(bool) || name.StartsWith("is_", StringComparison.Ordinal) || name.StartsWith("allow_", StringComparison.Ordinal))
            return RptColType.Boolean;
        if (valueType == typeof(DateTime) || name.EndsWith("_at", StringComparison.Ordinal) || name.EndsWith("_date", StringComparison.Ordinal))
            return name.EndsWith("_at", StringComparison.Ordinal) ? RptColType.DateTime : RptColType.Date;
        if (valueType == typeof(byte) || valueType == typeof(short) || valueType == typeof(int) || valueType == typeof(long))
            return RptColType.WholeNumber;
        if (valueType == typeof(decimal) || valueType == typeof(double) || valueType == typeof(float))
            return name.Contains("amount", StringComparison.Ordinal) || name.Contains("price", StringComparison.Ordinal) || name.Contains("cost", StringComparison.Ordinal) || name.Contains("balance", StringComparison.Ordinal) || name.Contains("total", StringComparison.Ordinal)
                ? RptColType.Money
                : RptColType.FractionalNumber;
        return RptColType.Text;
    }

    private static string Humanize(string value)
    {
        var words = Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2").Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words.ToLowerInvariant());
    }

    private static string ToSnakeCase(string value)
        => Regex.Replace(value, "([a-z0-9])([A-Z])", "$1_$2").Replace('-', '_').ToLowerInvariant();
}
