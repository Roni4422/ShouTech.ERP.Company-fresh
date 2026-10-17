using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShouTech.App.Localization
{
    /// <summary>Stable identifiers shared by application code and the JSON error catalog.</summary>
    public static class ErrorCodes
    {
        public const string DashboardDatabaseLoadFailed = "DSH-DB-001";
        public const string ReportSaveFailed = "RPT-SAVE-001";
        public const string ReportSaveAsFailed = "RPT-SAVE-002";
        public const string ReportFormulaCompileFailed = "RPT-FORMULA-001";
        public const string ReportEditNotAllowed = "RPT-OWNER-001";
        public const string DuplicateReportColumnKey = "RPT-VALIDATE-001";
        public const string EmptyReportFormula = "RPT-VALIDATE-002";
        public const string InvalidReportFormula = "RPT-VALIDATE-003";
        public const string ReportNameRequired = "RPT-VALIDATE-004";
        public const string ExcelReportExportFailed = "XPT-EXP-001";
        public const string PdfReportExportFailed = "XPT-EXP-002";
        public const string EncryptedReportExportFailed = "XPT-EXP-003";
        public const string ReportPrintFailed = "XPT-EXP-004";
        public const string BackupEncryptionKeyUnavailable = "BKP-KEY-001";
        public const string BackupCreationFailed = "BKP-CREATE-001";
        public const string BackupRestoreFailed = "BKP-RESTORE-001";
        public const string BackgroundBackupFailed = "BKP-ROTATE-001";
        public const string BackupCycleFailed = "BKP-CYCLE-001";
        public const string BackupCreationUiFailed = "BKP-UI-001";
        public const string BackupRestoreUiFailed = "BKP-UI-002";
        public const string BackupFolderOpenFailed = "BKP-UI-003";
        public const string RestoreLoginHandoffFailed = "BKP-UI-004";
        public const string DataExchangeDatasetLoadFailed = "DEX-UI-001";
        public const string DataExchangeWorkbookPreviewFailed = "DEX-UI-002";
        public const string DataExchangeOperationFailed = "DEX-UI-003";
        public const string DataExchangeRowsFailed = "DEX-UI-004";
        public const string DeletedCompanyLoadFailed = "RCO-LOAD-001";
        public const string DeletedCompanyRestoreFailed = "RCO-RESTORE-001";
        public const string DeletedCompanyRestoreRejected = "RCO-RESTORE-002";
        public const string DeletedCompanySelectionRequired = "RCO-SELECT-001";
        public const string CompanyEditLoadFailed = "COM-EDIT-001";
        public const string CompanyProfileSaveFailed = "COM-SAVE-001";
        public const string CompanyIndexRemovalFailed = "COM-INDEX-DELETE-001";
        public const string CompanySoftDeleteFailed = "COM-DELETE-001";
        public const string CompanyReindexFailed = "COM-REINDEX-001";
        public const string CompanyDefaultUpdateFailed = "COM-DEFAULT-001";
        public const string CompanyListLoadFailed = "COM-LOAD-001";
        public const string CompanyLoginOpenFailed = "COM-LOGIN-001";
        public const string CompanyDefaultWarehouseFailed = "CO-CREATE-WH-001";
        public const string CompanyDatabaseRegistrationFailed = "CO-CREATE-DB-001";
        public const string CompanyCreationFailed = "CO-CREATE-001";
        public const string BarcodePrintDialogFailed = "LBL-PRINT-001";
        public const string BarcodePrintFailed = "LBL-PRINT-002";
        public const string CashDrawerPrinterNotConfigured = "POS-DRAWER-001";
        public const string CashDrawerOpenFailed = "POS-DRAWER-002";
        public const string ApplicationSchemaBootstrapFailed = "APP-START-001";
        public const string UnhandledApplicationException = "APP-START-002";
        public const string ApplicationStartupFailed = "APP-START-003";
        public const string SplashInitializationFailed = "APP-START-004";
        public const string CompanySchemaInitializationFailed = "APP-DB-001";
        public const string SchemaRefreshFailed = "APP-DB-002";
    }

    public static class ErrorCatalog
    {
        private sealed class RawEntry
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = "System";

            [JsonPropertyName("name")]
            public string Name { get; set; } = "Unknown";

            [JsonPropertyName("message")]
            public string Message { get; set; } = string.Empty;

            [JsonPropertyName("messageKey")]
            public string MessageKey { get; set; } = string.Empty;

            [JsonPropertyName("severity")]
            public string Severity { get; set; } = "Error";

            [JsonPropertyName("catalogRef")]
            public string CatalogRef { get; set; } = string.Empty;

            [JsonPropertyName("location")]
            public string Location { get; set; } = string.Empty;
        }

        private static readonly Lazy<IReadOnlyDictionary<string, RawEntry>> Catalog = new(Load);

        public static string Get(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "Unknown application error.";

            var normalizedCode = code.Trim();
            return Catalog.Value.TryGetValue(normalizedCode, out var entry)
                ? ResolveMessage(entry)
                : $"An unregistered application error occurred. Error code: {normalizedCode}.";
        }

        public static ErrorDefinition Resolve(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return new ErrorDefinition(
                    "ERROR",
                    "UnknownError",
                    "Unknown application error.",
                    "Critical",
                    "SYS.0000",
                    false,
                    "APPLICATION.GENERAL");
            }

            var normalizedCode = code.Trim();

            if (Catalog.Value.TryGetValue(normalizedCode, out var entry))
            {
                return new ErrorDefinition(
                    normalizedCode,
                    entry.Name,
                    ResolveMessage(entry),
                    string.IsNullOrWhiteSpace(entry.Severity) ? "Error" : entry.Severity,
                    string.IsNullOrWhiteSpace(entry.CatalogRef) ? normalizedCode : entry.CatalogRef,
                    string.Equals(entry.Type, "User", StringComparison.OrdinalIgnoreCase),
                    string.IsNullOrWhiteSpace(entry.Location) ? InferLocation(normalizedCode, entry.CatalogRef) : entry.Location);
            }

            // Code not registered in Errors.json yet - fall back gracefully instead of
            // throwing, and infer System/User only from a conventional prefix.
            var isUserError =
                normalizedCode.StartsWith("USR_", StringComparison.OrdinalIgnoreCase) ||
                normalizedCode.StartsWith("USR-", StringComparison.OrdinalIgnoreCase) ||
                normalizedCode.StartsWith("USER_", StringComparison.OrdinalIgnoreCase) ||
                normalizedCode.StartsWith("USER-", StringComparison.OrdinalIgnoreCase);

            return new ErrorDefinition(
                normalizedCode,
                "UnregisteredError",
                "The error code is not registered in the error catalog.",
                "Error",
                "CAT.UNREGISTERED",
                isUserError,
                InferLocation(normalizedCode, string.Empty));
        }


        // Error messages are operational diagnostics and remain English-only,
        // regardless of the language selected for the application UI.
        private static string ResolveMessage(RawEntry entry) =>
            string.IsNullOrWhiteSpace(entry.Message)
                ? $"An error occurred. Error code: {entry.Name}."
                : entry.Message;

        private static string InferLocation(string code, string catalogRef)
        {
            if (!string.IsNullOrWhiteSpace(catalogRef))
                return catalogRef.Trim();

            var separator = code.IndexOf('-');
            return separator > 0 ? code[..separator] + ".GENERAL" : "APPLICATION.GENERAL";
        }

        private static Dictionary<string, RawEntry> Load()
        {
            try
            {
                var path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Resources",
                    "Errors",
                    "Errors.json");

                if (!File.Exists(path))
                    return new Dictionary<string, RawEntry>(StringComparer.OrdinalIgnoreCase);

                var json = File.ReadAllText(path);

                return JsonSerializer.Deserialize<Dictionary<string, RawEntry>>(json)
                    ?? new Dictionary<string, RawEntry>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, RawEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public sealed class ErrorDefinition
    {
        public string Code { get; }
        public string Name { get; }
        public string Message { get; }
        public string Severity { get; }
        public string CatalogRef { get; }
        public bool IsUserError { get; }
        public string Location { get; }

        public ErrorDefinition(
            string code,
            string name,
            string message,
            string severity,
            string catalogRef,
            bool isUserError,
            string location)
        {
            Code = code;
            Name = name;
            Message = message;
            Severity = severity;
            CatalogRef = catalogRef;
            IsUserError = isUserError;
            Location = location;
        }
    }
}

