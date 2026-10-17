using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShouTech.App.Localization
{
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

