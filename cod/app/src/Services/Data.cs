// ========================================================================
// FILE: Services/Data.cs
// PROJECT: SHOUTECH ERP — Data access helpers, logging, audit, SQLite bootstrap
// Merged: DataServ, DatabaseRuntime, AuditService, LogServ, SqliteSchemaBootstrapper
// VisuId.cs left separate (visual identity / themes)
// ========================================================================

#nullable enable

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using Microsoft.Data.Sqlite;
using ShouTech.App.Common;
using ShouTech.App.Configuration;
using ShouTech.Domain.Interfaces;

namespace ShouTech.App.Services
{

    // ========== DataServ.cs ==========
public sealed class DataServ : IDataServ
    {
        public Task<int> GetTotalCustomersAsync() => Task.FromResult(1284);

        public Task<int> GetTotalProductsAsync() => Task.FromResult(3567);

        public Task<int> GetTotalPendingApprovalsAsync() => Task.FromResult(12);

        public Task<List<RecentDocument>> GetRecentDocumentsAsync(int count)
        {
            var docs = new List<RecentDocument>
            {
                new() { DocumentNumber = "INV-2026-00142", DocumentType = "فاتورة مبيعات", CustomerName = "شركة النور", TotalAmount = 15420.50m, Status = "معتمد", Date = DateTime.Now.AddHours(-2) },
                new() { DocumentNumber = "PO-2026-00089", DocumentType = "طلب شراء", CustomerName = "مورد الخليج", TotalAmount = 8750.00m, Status = "معلق", Date = DateTime.Now.AddHours(-5) },
                new() { DocumentNumber = "JV-2026-00301", DocumentType = "سند قيد", CustomerName = "-", TotalAmount = 3200.00m, Status = "مرحّل", Date = DateTime.Now.AddDays(-1) },
                new() { DocumentNumber = "SO-2026-00256", DocumentType = "طلبية بيع", CustomerName = "متجر الأمل", TotalAmount = 4890.75m, Status = "قيد التنفيذ", Date = DateTime.Now.AddDays(-1) },
                new() { DocumentNumber = "RC-2026-00017", DocumentType = "سند قبض", CustomerName = "عميل نقدي", TotalAmount = 1200.00m, Status = "مكتمل", Date = DateTime.Now.AddDays(-2) }
            };

            return Task.FromResult(docs.GetRange(0, Math.Min(count, docs.Count)));
        }
    }

    // ========== DatabaseRuntime.cs ==========
internal static class DatabaseRuntime
{
    private const string DefaultSqliteFileName = "shoutech_erp.sqlite3";

    public static string GetProvider()
    {
        var configured = AppCfgSvc.Current?.ConnectionStrings?
            .FirstOrDefault(item => item.Key.Equals("Default", StringComparison.OrdinalIgnoreCase))
            .Value?.Provider;
        var fallback = string.IsNullOrWhiteSpace(configured)
            ? AppCfgSvc.Current?.Database?.DefaultProvider
            : configured;
        if (string.IsNullOrWhiteSpace(fallback))
            fallback = Environment.GetEnvironmentVariable("SHOUTECH_DB_PROVIDER");
        return CoerceSupportedProvider(fallback, AppCfgSvc.Current?.Database?.DefaultProvider);
    }

    public static string? GetConnectionString()
    {
        var connection = AppCfgSvc.Current?.ConnectionStrings?
            .FirstOrDefault(item => item.Key.Equals("Default", StringComparison.OrdinalIgnoreCase))
            .Value?.ConnectionString;
        if (string.IsNullOrWhiteSpace(connection))
            connection = AppCfgSvc.Current?.ConnectionStrings?.Values?.FirstOrDefault()?.ConnectionString;
        if (string.IsNullOrWhiteSpace(connection))
            connection = Environment.GetEnvironmentVariable("SHOUTECH_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection))
            return null;

        return IsSqlite()
            ? NormalizeSqliteConnectionString(connection)
            : connection.Trim();
    }

    public static bool IsSqlite(string? provider = null) =>
        string.Equals(DatabaseProviderNames.Normalize(provider ?? GetProvider()), DatabaseProviderNames.Sqlite, StringComparison.OrdinalIgnoreCase);

    public static bool IsPostgreSql(string? provider = null) =>
        string.Equals(DatabaseProviderNames.Normalize(provider ?? GetProvider()), DatabaseProviderNames.PostgreSql, StringComparison.OrdinalIgnoreCase);

    public static bool LooksLikeConnectionString(string? value)
    {
        var trimmed = value?.Trim();
        return !string.IsNullOrWhiteSpace(trimmed) && trimmed.Contains('=') && trimmed.Contains(';');
    }

    public static string NormalizeSqliteConnectionString(string sqlitePathOrConnectionString)
    {
        if (string.IsNullOrWhiteSpace(sqlitePathOrConnectionString))
            throw new ArgumentException("A SQLite path or connection string is required.", nameof(sqlitePathOrConnectionString));

        if (!LooksLikeConnectionString(sqlitePathOrConnectionString))
            return BuildSqliteConnectionStringFromPath(sqlitePathOrConnectionString.Trim());

        var builder = new SqliteConnectionStringBuilder(sqlitePathOrConnectionString.Trim());
        if (string.Equals(builder.DataSource, ":memory:", StringComparison.Ordinal)
            || builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return builder.ToString();

        builder.DataSource = Path.GetFullPath(builder.DataSource);
        if (builder.Mode == SqliteOpenMode.ReadOnly)
            return builder.ToString();

        builder.Mode = SqliteOpenMode.ReadWriteCreate;
        builder.Cache = SqliteCacheMode.Shared;
        return builder.ToString();
    }

    public static string? GetSqliteDataSource(string? sqlitePathOrConnectionString)
    {
        if (string.IsNullOrWhiteSpace(sqlitePathOrConnectionString))
            return null;

        if (!LooksLikeConnectionString(sqlitePathOrConnectionString))
            return Path.GetFullPath(sqlitePathOrConnectionString.Trim());

        var builder = new SqliteConnectionStringBuilder(sqlitePathOrConnectionString.Trim());
        return string.Equals(builder.DataSource, ":memory:", StringComparison.Ordinal)
            || builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? builder.DataSource
            : Path.GetFullPath(builder.DataSource);
    }

    private static string BuildSqliteConnectionStringFromPath(string path)
    {
        var fullPath = Path.GetFullPath(Path.HasExtension(path)
            ? path
            : Path.Combine(path, DefaultSqliteFileName));
        return new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    private static string CoerceSupportedProvider(string? provider, string? fallbackProvider)
    {
        var normalized = DatabaseProviderNames.Normalize(provider);
        if (DatabaseProviderNames.IsSupported(normalized))
            return normalized;
        var fallback = DatabaseProviderNames.Normalize(fallbackProvider);
        return DatabaseProviderNames.IsSupported(fallback)
            ? fallback
            : DatabaseProviderNames.Sqlite;
    }
}

    // ========== AuditService.cs ==========
/// <summary>
/// Central audit service for application lifecycle, startup files,
/// user operations, and security-related events.
///
/// All events are forwarded to ILogServ.
/// Operational events are written to log.log; errors are isolated in errors.log.
/// </summary>
public sealed class AuditService : IAuditService
{
private readonly ILogServ _logger;

    public AuditService(ILogServ logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Records application startup together with basic environment information.
    /// </summary>
    public Task LogSystemStartAsync()
    {
        var osUser = Environment.UserName;
        var machine = Environment.MachineName;

        var version =
            System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetName()
                .Version?
                .ToString()
            ?? "unknown";

        _logger.LogLifecycle(
            "SystemStart",
            $"Application started | OS User: {osUser} | Machine: {machine} | Version: {version}");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Records application shutdown.
    /// </summary>
    public Task LogSystemStopAsync()
    {
        _logger.LogLifecycle(
            "SystemStop",
            "Application shutdown");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a file accessed during application startup.
    /// </summary>
    public Task LogStartupFileAsync(
        string filePath,
        string purpose,
        bool success = true,
        string? reason = null)
    {
        _logger.LogFileAccess(
            filePath,
            purpose,
            success,
            reason);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a normal operation performed by a logged-in user.
    /// Sensitive values must never be passed through this method.
    /// </summary>
    public Task LogUserActionAsync(
        string username,
        string action,
        string details = "")
    {
        var message = string.IsNullOrWhiteSpace(details)
            ? username
            : $"{username} | {details}";

        _logger.LogOperation(
            "User",
            action,
            message);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Records a security-related event such as denied permission,
    /// integrity-check failure, license tampering, or repeated failed logins.
    /// Passwords, tokens, keys, and other secrets must never be included.
    /// </summary>
    public Task LogSecurityViolationAsync(
        string eventType,
        string details,
        string? username = null)
    {
        _logger.LogSecurity(
            eventType,
            details,
            username);

        return Task.CompletedTask;
    }
}

    // ========== LogServ.cs ==========
/// <summary>
    /// Central application logger.
    ///
    /// log.log:
    ///     Operational events, lifecycle, authentication, and diagnostics.
    ///
    /// errors.log:
    ///     System and user error entries only; errors are not duplicated in log.log.
    ///
    /// Logging is synchronous and write-through.
    /// </summary>
    public sealed class LogServ : ILogServ
    {
        private readonly string _logDir;
        private readonly string _logPath;
        private readonly string _errorsLogPath;
        private readonly object _lock = new();

        private const long MaxLogFileBytes = 5L * 1024 * 1024;
        private const double TrimFraction = 0.30;

        private static readonly UTF8Encoding Utf8NoBom =
            new(encoderShouldEmitUTF8Identifier: false);

        public LogServ()
        {
            _logDir = ResolveLogDirectory();

            Directory.CreateDirectory(_logDir);

            _logPath = Path.Combine(_logDir, "log.log");
            _errorsLogPath = Path.Combine(_logDir, "errors.log");

            Debug.WriteLine($"LOG DIRECTORY = {_logDir}");

            WriteHeader();
        }

        public string LogFilePath => _logPath;

        private static string ResolveLogDirectory()
        {
            var current = new DirectoryInfo(
                AppDomain.CurrentDomain.BaseDirectory);

            while (current != null)
            {
                if (!string.Equals(
                        current.Name,
                        "bin",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        current.Name,
                        "obj",
                        StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = Path.Combine(
                        current.FullName,
                        "log");

                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                current = current.Parent;
            }

            // Clean-install fallback.
            // This is used only when no project/application
            // log directory exists above the runtime directory.
            var fallbackRoot =
                Directory.GetParent(
                    AppDomain.CurrentDomain.BaseDirectory)?
                .FullName
                ?? AppDomain.CurrentDomain.BaseDirectory;

            return Path.Combine(
                fallbackRoot,
                "log");
        }

        private void WriteHeader()
        {
            try
            {
                lock (_lock)
                {
                    if (!File.Exists(_logPath))
                    {
                        WritePhysical(
                            _logPath,
                            BuildHeader(
                                "Application Operational Log"));
                    }

                    if (!File.Exists(_errorsLogPath))
                    {
                        WritePhysical(
                            _errorsLogPath,
                            BuildHeader(
                                "Error Log (SYS-ERROR / USR-ERROR only)"));
                    }
                }
            }
            catch
            {
                // Logging must never stop application startup.
            }
        }

        private static string BuildHeader(string title)
        {
            return
                Environment.NewLine +
                new string('=', 78) +
                Environment.NewLine +
                $"ShouTech ERP - {title}" +
                Environment.NewLine +
                $"Started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}" +
                Environment.NewLine +
                $"Machine: {Environment.MachineName}" +
                Environment.NewLine +
                $"Process: {Process.GetCurrentProcess().ProcessName} ({Environment.ProcessId})" +
                Environment.NewLine +
                new string('=', 78) +
                Environment.NewLine;
        }

        // ================================================================
        // General logging
        // ================================================================

        public void LogInfo(
            string category,
            string message)
        {
            Write(
                "INFO",
                category,
                message);
        }

        public void LogWarning(
            string category,
            string message)
        {
            Write(
                "WARN",
                category,
                message);
        }

        public void LogError(
            string category,
            string message,
            Exception? ex = null)
        {
            var detail = ex == null
                ? message
                : $"{message} | Exception={ex.GetType().FullName}: {ex.Message}" +
                  $"{Environment.NewLine}StackTrace: {ex.StackTrace}";

            Write(
                "ERROR",
                category,
                detail);
        }

        public void LogDebug(
            string category,
            string message)
        {
            Write(
                "DEBUG",
                category,
                message);
        }

        // ================================================================
        // Lifecycle
        // ================================================================

        public void LogLifecycle(
            string eventName,
            string message = "")
        {
            Write(
                "LIFE",
                "Lifecycle",
                string.IsNullOrWhiteSpace(message)
                    ? eventName
                    : $"{eventName} | {message}");
        }

        // ================================================================
        // Operations
        // ================================================================

        public void LogOperation(
            string category,
            string action,
            string message = "")
        {
            Write(
                "OP",
                category,
                string.IsNullOrWhiteSpace(message)
                    ? action
                    : $"{action} | {message}");
        }

        // ================================================================
        // Authentication
        // ================================================================

        public void LogAuthAttempt(
            string username,
            bool success,
            string? reason = null)
        {
            var details = success
                ? $"Login success for user: {username}"
                : $"Login failed for user: {username} - reason: {reason ?? "unknown"}";

            Write(
                "AUTH",
                "Authentication",
                details);
        }

        // ================================================================
        // File access
        // ================================================================

        public void LogFileAccess(
            string filePath,
            string purpose,
            bool success = true,
            string? reason = null)
        {
            var status = success
                ? "OK"
                : "FAIL";

            var details = success
                ? $"Loaded [{purpose}] => {filePath}"
                : $"Failed to load [{purpose}] => {filePath} - reason: {reason ?? "unknown"}";

            Write(
                success ? "INFO" : "WARN",
                "Startup.Files",
                $"[{status}] {details}");
        }

        // ================================================================
        // Security
        // ================================================================

        public void LogSecurity(
            string eventType,
            string message,
            string? username = null)
        {
            var actor = string.IsNullOrWhiteSpace(username)
                ? "unknown"
                : username;

            var details =
                $"[{eventType}] user={actor} | {message}";

            Write(
                "SECURITY",
                "Security",
                details);
        }

        // ================================================================
        // Application errors
        // ================================================================

        public void LogAppError(
			string code,
			Exception? ex = null,
			string? context = null)
		{
			var normalizedCode =
				string.IsNullOrWhiteSpace(code)
					? "SYS-ERROR"
					: code.Trim();

			var isUserError =
				normalizedCode.StartsWith("USR-", StringComparison.OrdinalIgnoreCase) ||
				normalizedCode.StartsWith("USR_", StringComparison.OrdinalIgnoreCase) ||
				normalizedCode.StartsWith("USER-", StringComparison.OrdinalIgnoreCase) ||
				normalizedCode.StartsWith("USER_", StringComparison.OrdinalIgnoreCase);

			var level =
				isUserError
					? "USR-ERROR"
					: "SYS-ERROR";

			var detail =
				$"[{normalizedCode}]";

			if (!string.IsNullOrWhiteSpace(context))
			{
				detail += $" | {context}";
			}

			if (ex != null)
			{
				detail +=
					$" | Exception={ex.GetType().FullName}: {ex.Message}" +
					$"{Environment.NewLine}StackTrace: {ex.StackTrace}";
			}

			Write(
				level,
				"AppError",
				detail);
		}

        // ================================================================
        // Central writer
        // ================================================================

        private void Write(
            string level,
            string category,
            string message)
        {
            try
            {
                var cleanMessage =
                    message ?? string.Empty;

                var company =
                    string.IsNullOrWhiteSpace(
                        AppState.CurrentCompanyCode)
                        ? "-"
                        : AppState.CurrentCompanyCode;

                var user =
                    AppState.CurrentUser?.Username;

                if (string.IsNullOrWhiteSpace(user))
                {
                    user = "-";
                }

                var line =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] " +
                    $"[{level}] " +
                    $"[{category}] " +
                    $"[Company={company}] " +
                    $"[User={user}] " +
                    $"{cleanMessage}" +
                    Environment.NewLine;

                lock (_lock)
                {
                    // Error records go to errors.log and are mirrored to log.log so
                    // data-exchange failures remain visible in the main operational log.
                    if (level == "SYS-ERROR" || level == "USR-ERROR" || level == "ERROR")
                    {
                        var errorLevel = level == "USR-ERROR" ? "USR-ERROR" : "SYS-ERROR";
                        var errorLine = level == "ERROR"
                            ? line.Replace("[ERROR]", $"[{errorLevel}]", StringComparison.Ordinal)
                            : line;
                        WritePhysical(_errorsLogPath, errorLine);
                        WritePhysical(_logPath, errorLine);
                    }
                    else
                    {
                        WritePhysical(_logPath, line);
                    }
                }
            }
            catch
            {
                // Logging must never crash the application.
            }
        }

        // ================================================================
        // Physical writer
        // ================================================================

        private static void WritePhysical(
            string path,
            string content)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);

            TrimIfTooLarge(path);

            using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                options: FileOptions.WriteThrough);

            using var writer =
                new StreamWriter(
                    stream,
                    Utf8NoBom);

            writer.Write(content);
            writer.Flush();

            stream.Flush(
                flushToDisk: true);
        }

        // ================================================================
        // Log rotation
        // ================================================================

        private static void TrimIfTooLarge(
            string path)
        {
            try
            {
                var info = new FileInfo(path);

                if (!info.Exists ||
                    info.Length < MaxLogFileBytes)
                {
                    return;
                }

                var lines =
                    File.ReadAllLines(
                        path,
                        Utf8NoBom);

                if (lines.Length < 100)
                {
                    return;
                }

                var dropCount =
                    Math.Max(
                        1,
                        (int)(lines.Length * TrimFraction));

                var kept =
                    lines.Skip(dropCount);

                var marker =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] " +
                    "[SYSTEM] " +
                    "[LogRotation] " +
                    "[Company=-] " +
                    "[User=-] " +
                    $"--- Removed oldest {dropCount} lines because the log exceeded " +
                    $"{MaxLogFileBytes / (1024 * 1024)} MB " +
                    $"(previous size: {info.Length / 1024} KB) ---" +
                    Environment.NewLine;

                File.WriteAllLines(
                    path,
                    new[] { marker }.Concat(kept),
                    Utf8NoBom);
            }
            catch
            {
                // Rotation must never crash logging.
            }
        }
    }

    // ========== SqliteSchemaBootstrapper.cs ==========
internal static class SqliteSchemaBootstrapper
{
    public static void EnsureDatabase(string connectionString)
    {
        var normalized = DatabaseRuntime.NormalizeSqliteConnectionString(connectionString);
        var dataSource = DatabaseRuntime.GetSqliteDataSource(normalized);
        if (!string.IsNullOrWhiteSpace(dataSource)
            && !string.Equals(dataSource, ":memory:", StringComparison.Ordinal)
            && !dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(dataSource);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection(normalized);
        connection.Open();

        if (string.Equals(
                Environment.GetEnvironmentVariable("SHOUTECH_DEV_RESET_SCHEMA"),
                "1", StringComparison.Ordinal))
        {
            using var resetCommand = connection.CreateCommand();
            resetCommand.CommandText = """
                PRAGMA foreign_keys = OFF;
                DROP TABLE IF EXISTS stock_movements;
                DROP TABLE IF EXISTS stock_balances;
                DROP TABLE IF EXISTS products;
                DROP TABLE IF EXISTS product_categories;
                DROP TABLE IF EXISTS brands;
                DROP TABLE IF EXISTS warehouses;
                DROP TABLE IF EXISTS units_of_measure;
                PRAGMA foreign_keys = ON;
                """;
            resetCommand.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;

            -- ═══════════════════════════════════════════════════════════
            -- [CORE] Companies
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS companies (
                company_id TEXT NOT NULL PRIMARY KEY,
                company_code TEXT NOT NULL UNIQUE,
                company_name_ar TEXT NOT NULL,
                company_name_en TEXT NOT NULL,
                email TEXT,
                phone TEXT,
                mobile TEXT,
                address_line1 TEXT,
                commercial_registration TEXT,
                currency_code TEXT NOT NULL DEFAULT 'USD',
                language_code TEXT NOT NULL DEFAULT 'ar',
                coa_template_code TEXT NOT NULL DEFAULT 'SIMPLE',
                is_active INTEGER NOT NULL DEFAULT 1,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            -- ═══════════════════════════════════════════════════════════
            -- [CORE] Users
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS users (
                user_id TEXT NOT NULL PRIMARY KEY,
                company_id TEXT NOT NULL,
                username TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                password_salt TEXT NOT NULL,
                full_name TEXT NOT NULL,
                full_name_ar TEXT NOT NULL,
                full_name_en TEXT NOT NULL,
                email TEXT,
                must_change_password INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(company_id, username),
                FOREIGN KEY(company_id) REFERENCES companies(company_id)
            );

            -- ═══════════════════════════════════════════════════════════
            -- [CORE] Fiscal Years
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS fiscal_years (
                fiscal_year_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                company_id TEXT NOT NULL,
                fiscal_year INTEGER NOT NULL,
                database_name TEXT NOT NULL,
                start_date TEXT NOT NULL,
                end_date TEXT NOT NULL,
                is_current INTEGER NOT NULL DEFAULT 1,
                is_closed INTEGER NOT NULL DEFAULT 0,
                UNIQUE(company_id, fiscal_year),
                FOREIGN KEY(company_id) REFERENCES companies(company_id)
            );

            -- ═══════════════════════════════════════════════════════════
            -- [CORE] Roles
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS roles (
                role_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                company_id TEXT NOT NULL,
                role_code TEXT NOT NULL,
                role_name TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                UNIQUE(company_id, role_code),
                FOREIGN KEY(company_id) REFERENCES companies(company_id)
            );

            -- ═══════════════════════════════════════════════════════════
            -- [CORE] Permissions
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS permissions (
                permission_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                permission_code TEXT NOT NULL UNIQUE,
                permission_name TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            -- ═══════════════════════════════════════════════════════════
            -- [CORE] User Roles (M:N)
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS user_roles (
                user_id TEXT NOT NULL,
                role_id INTEGER NOT NULL,
                PRIMARY KEY (user_id, role_id),
                FOREIGN KEY(user_id) REFERENCES users(user_id),
                FOREIGN KEY(role_id) REFERENCES roles(role_id)
            );

            -- ═══════════════════════════════════════════════════════════
            -- [CORE] Role Permissions (M:N)
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS role_permissions (
                role_id INTEGER NOT NULL,
                permission_id INTEGER NOT NULL,
                PRIMARY KEY (role_id, permission_id),
                FOREIGN KEY(role_id) REFERENCES roles(role_id),
                FOREIGN KEY(permission_id) REFERENCES permissions(permission_id)
            );

            -- ═══════════════════════════════════════════════════════════
            -- [CORE] Legacy Permissions Seed (kept for backward compat)
            -- ═══════════════════════════════════════════════════════════
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('COMPANY_VIEW', 'View companies');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('COMPANY_CREATE', 'Create companies');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('COMPANY_EDIT', 'Edit companies');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('COMPANY_DELETE', 'Delete companies');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('USER_VIEW', 'View users');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('USER_CREATE', 'Create users');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('USER_EDIT', 'Edit users');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('USER_DELETE', 'Delete users');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('ROLE_VIEW', 'View roles');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('ROLE_EDIT', 'Manage roles');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('PERMISSION_VIEW', 'View permissions');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('SETTINGS_EDIT', 'Manage settings');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('BACKUP_RUN', 'Run backups');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('BACKUP_RESTORE', 'Restore backups');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('ACCOUNTING_VIEW', 'View accounting');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('ACCOUNTING_EDIT', 'Manage accounting');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('JOURNAL_POST', 'Post journal entries');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('INVENTORY_VIEW', 'View inventory');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('INVENTORY_EDIT', 'Manage inventory');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('SALES_VIEW', 'View sales');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('SALES_EDIT', 'Manage sales');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('PURCHASE_VIEW', 'View purchases');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('PURCHASE_EDIT', 'Manage purchases');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('REPORT_VIEW', 'View reports');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('AUDIT_VIEW', 'View audit log');
            INSERT OR IGNORE INTO permissions (permission_code, permission_name) VALUES ('SYSTEM_VIEW', 'View system status');

            -- ═══════════════════════════════════════════════════════════
            -- [INV] Units of Measure
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS units_of_measure (
                uom_id TEXT NOT NULL PRIMARY KEY,
                company_id TEXT NOT NULL,
                uom_code TEXT NOT NULL,
                uom_name_ar TEXT NOT NULL,
                uom_name_en TEXT,
                base_uom_id TEXT,
                conversion_factor REAL NOT NULL DEFAULT 1,
                is_base_unit INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                deleted_at TEXT,
                deleted_by TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                created_by TEXT,
                modified_at TEXT,
                modified_by TEXT,
                UNIQUE(company_id, uom_code),
                FOREIGN KEY(company_id) REFERENCES companies(company_id)
            );

            CREATE INDEX IF NOT EXISTS ix_uom_company
                ON units_of_measure(company_id, is_active) WHERE is_deleted = 0;

            -- ═══════════════════════════════════════════════════════════
            -- [INV] Product Categories (hierarchical)
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS product_categories (
                category_id TEXT NOT NULL PRIMARY KEY,
                company_id TEXT NOT NULL,
                parent_category_id TEXT,
                category_code TEXT NOT NULL,
                category_name_ar TEXT NOT NULL,
                category_name_en TEXT,
                category_level INTEGER NOT NULL DEFAULT 1,
                icon_path TEXT,
                image_path TEXT,
                is_active INTEGER NOT NULL DEFAULT 1,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                deleted_at TEXT,
                deleted_by TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                created_by TEXT,
                modified_at TEXT,
                modified_by TEXT,
                UNIQUE(company_id, category_code),
                FOREIGN KEY(company_id) REFERENCES companies(company_id),
                FOREIGN KEY(parent_category_id) REFERENCES product_categories(category_id)
            );

            CREATE INDEX IF NOT EXISTS ix_categories_company
                ON product_categories(company_id, is_active) WHERE is_deleted = 0;

            -- ═══════════════════════════════════════════════════════════
            -- [INV] Brands
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS brands (
                brand_id TEXT NOT NULL PRIMARY KEY,
                company_id TEXT NOT NULL,
                brand_code TEXT NOT NULL,
                brand_name_ar TEXT NOT NULL,
                brand_name_en TEXT,
                logo_path TEXT,
                website TEXT,
                description TEXT,
                is_active INTEGER NOT NULL DEFAULT 1,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                deleted_at TEXT,
                deleted_by TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                created_by TEXT,
                modified_at TEXT,
                modified_by TEXT,
                UNIQUE(company_id, brand_code),
                FOREIGN KEY(company_id) REFERENCES companies(company_id)
            );

            CREATE INDEX IF NOT EXISTS ix_brands_company
                ON brands(company_id, is_active) WHERE is_deleted = 0;

                        -- ═══════════════════════════════════════════════════════════
            -- [INV] Warehouses
            -- The warehouses table is created by MatRepo.EnsureWarehouseSchema
            -- immediately after this script runs. Do not redefine it here to
            -- avoid schema drift between the two definitions.
            -- ═══════════════════════════════════════════════════════════

            -- ═══════════════════════════════════════════════════════════
            -- [INV] Products (45 fields — full version)
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS products (
                product_id TEXT NOT NULL PRIMARY KEY,
                company_id TEXT NOT NULL,

                -- Basic
                product_code TEXT NOT NULL,
                product_name_ar TEXT NOT NULL,
                product_name_en TEXT,
                short_name TEXT,
                description TEXT,

                -- Classification
                category_id TEXT,
                brand_id TEXT,

                -- UOM
                primary_uom_id TEXT NOT NULL,
                sale_uom_id TEXT,
                purchase_uom_id TEXT,

                -- Identification
                barcode TEXT,
                sku TEXT,
                quick_code TEXT,

                -- Pricing
                sale_price REAL NOT NULL DEFAULT 0,
                purchase_price REAL NOT NULL DEFAULT 0,
                cost_price REAL NOT NULL DEFAULT 0,
                min_sale_price REAL,
                max_sale_price REAL,
                last_purchase_price REAL,
                last_sale_price REAL,
                profit_margin REAL,

                -- Tax (ZATCA-ready)
                tax_category TEXT NOT NULL DEFAULT 'STANDARD'
                    CHECK (tax_category IN ('STANDARD', 'ZERO', 'EXEMPT')),
                tax_rate REAL NOT NULL DEFAULT 15,
                tax_exempt_reason TEXT,
                zatca_item_code TEXT,

                -- Stock Settings
                is_stock_item INTEGER NOT NULL DEFAULT 1,
                is_service_item INTEGER NOT NULL DEFAULT 0,
                is_pos_item INTEGER NOT NULL DEFAULT 1,
                is_serialized INTEGER NOT NULL DEFAULT 0,
                is_batch_tracked INTEGER NOT NULL DEFAULT 0,
                is_expiry_tracked INTEGER NOT NULL DEFAULT 0,
                expiry_days INTEGER,
                is_composite_product INTEGER NOT NULL DEFAULT 0,

                reorder_level REAL NOT NULL DEFAULT 0,
                reorder_quantity REAL NOT NULL DEFAULT 0,
                safety_stock REAL NOT NULL DEFAULT 0,
                max_stock REAL,

                -- Dimensions
                weight REAL,
                length REAL,
                width REAL,
                height REAL,

                -- Media
                main_image_path TEXT,

                -- AI-ready
                demand_velocity INTEGER,
                product_score REAL,
                is_recommended INTEGER NOT NULL DEFAULT 0,

                -- Status
                is_active INTEGER NOT NULL DEFAULT 1,
                is_blocked INTEGER NOT NULL DEFAULT 0,
                block_reason TEXT,
                discontinued_date TEXT,

                -- Audit
                is_deleted INTEGER NOT NULL DEFAULT 0,
                deleted_at TEXT,
                deleted_by TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                created_by TEXT,
                modified_at TEXT,
                modified_by TEXT,

                UNIQUE(company_id, product_code),
                FOREIGN KEY(company_id) REFERENCES companies(company_id),
                FOREIGN KEY(category_id) REFERENCES product_categories(category_id),
                FOREIGN KEY(brand_id) REFERENCES brands(brand_id),
                FOREIGN KEY(primary_uom_id) REFERENCES units_of_measure(uom_id),
                FOREIGN KEY(sale_uom_id) REFERENCES units_of_measure(uom_id),
                FOREIGN KEY(purchase_uom_id) REFERENCES units_of_measure(uom_id)
            );

            CREATE INDEX IF NOT EXISTS ix_products_company_active
                ON products(company_id, is_active) WHERE is_deleted = 0;

            CREATE INDEX IF NOT EXISTS ix_products_barcode
                ON products(barcode) WHERE barcode IS NOT NULL AND is_deleted = 0;

            CREATE INDEX IF NOT EXISTS ix_products_name_ar
                ON products(product_name_ar) WHERE is_deleted = 0;

            CREATE INDEX IF NOT EXISTS ix_products_category
                ON products(category_id, is_active) WHERE is_deleted = 0;

            CREATE INDEX IF NOT EXISTS ix_products_pos
                ON products(company_id, is_pos_item, is_active) WHERE is_deleted = 0;

            -- ═══════════════════════════════════════════════════════════
            -- [ACC] Journal Entries
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS journal_entries (
                journal_entry_id TEXT NOT NULL PRIMARY KEY,
                company_id TEXT NOT NULL,
                entry_date TEXT NOT NULL,
                description TEXT NOT NULL,
                status INTEGER NOT NULL DEFAULT 0 CHECK (status IN (0, 1, 2)),
                created_by TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                posted_by TEXT,
                posted_at TEXT,
                reversed_by TEXT,
                reversed_at TEXT
            );

            -- ═══════════════════════════════════════════════════════════
            -- [ACC] Journal Entry Lines
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS journal_entry_lines (
                journal_entry_line_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                journal_entry_id TEXT NOT NULL,
                line_number INTEGER NOT NULL,
                account_id TEXT NOT NULL,
                debit REAL NOT NULL DEFAULT 0,
                credit REAL NOT NULL DEFAULT 0,
                cost_center TEXT,
                note TEXT,
                UNIQUE(journal_entry_id, line_number),
                CHECK (debit >= 0 AND credit >= 0 AND ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))),
                FOREIGN KEY(journal_entry_id) REFERENCES journal_entries(journal_entry_id)
            );

            -- ═══════════════════════════════════════════════════════════
            -- [ACC] Journal Audit Events
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS journal_audit_events (
                journal_audit_event_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                journal_entry_id TEXT NOT NULL,
                event_type INTEGER NOT NULL CHECK (event_type IN (1, 2, 3)),
                actor TEXT NOT NULL,
                event_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                details TEXT,
                FOREIGN KEY(journal_entry_id) REFERENCES journal_entries(journal_entry_id)
            );

            -- ═══════════════════════════════════════════════════════════
            -- [INV] Stock Balances (per warehouse/product)
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS stock_balances (
                stock_balance_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                company_id TEXT NOT NULL,
                warehouse_id INTEGER NOT NULL,
                product_id TEXT NOT NULL,
                available_qty REAL NOT NULL DEFAULT 0,
                last_cost REAL,
                last_updated TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                is_deleted INTEGER NOT NULL DEFAULT 0,
                UNIQUE (company_id, warehouse_id, product_id),
                FOREIGN KEY(company_id) REFERENCES companies(company_id),
                FOREIGN KEY(warehouse_id) REFERENCES warehouses(warehouse_id),
                FOREIGN KEY(product_id) REFERENCES products(product_id)
            );

            CREATE INDEX IF NOT EXISTS ix_stock_balances_product
                ON stock_balances(product_id) WHERE is_deleted = 0;

            -- ═══════════════════════════════════════════════════════════
            -- [INV] Stock Movements (ledger of all stock changes)
            -- ═══════════════════════════════════════════════════════════
            CREATE TABLE IF NOT EXISTS stock_movements (
                movement_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                company_id TEXT NOT NULL,
                movement_type TEXT NOT NULL,
                movement_date TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                warehouse_id_to INTEGER,
                product_id TEXT NOT NULL,
                quantity REAL NOT NULL CHECK (quantity > 0),
                unit_cost REAL,
                reference_table TEXT,
                notes TEXT,
                created_by TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(company_id) REFERENCES companies(company_id),
                FOREIGN KEY(warehouse_id_to) REFERENCES warehouses(warehouse_id),
                FOREIGN KEY(product_id) REFERENCES products(product_id)
            );

            CREATE INDEX IF NOT EXISTS ix_stock_movements_product
                ON stock_movements(product_id, movement_date);

            CREATE INDEX IF NOT EXISTS ix_stock_movements_date
                ON stock_movements(company_id, movement_date);
            """;
        command.ExecuteNonQuery();

        // Enterprise permission schema (groups, user_permissions, expanded fields)
        EnsureEnterprisePermissionSchema(connection);
		// Warehouse schema is owned by MatRepo.Ensuring it here guarantees the
        // table exists before any dependent table (stock_balances,
        // stock_movements) is referenced at insert time.
        MatRepo.EnsureWarehouseSchema(connection);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Enterprise Permission Schema Extension
    // ═══════════════════════════════════════════════════════════════════
    private static void EnsureEnterprisePermissionSchema(SqliteConnection connection)
    {
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS permission_groups (
                group_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                group_code TEXT NOT NULL UNIQUE,
                name_ar TEXT NOT NULL,
                name_en TEXT NOT NULL,
                display_order INTEGER NOT NULL DEFAULT 0,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS user_permissions (
                user_id TEXT NOT NULL,
                permission_id INTEGER NOT NULL,
                effect INTEGER NOT NULL CHECK(effect IN (0,1)),
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY(user_id, permission_id),
                FOREIGN KEY(user_id) REFERENCES users(user_id),
                FOREIGN KEY(permission_id) REFERENCES permissions(permission_id)
            );

            CREATE INDEX IF NOT EXISTS ix_user_permissions_permission
                ON user_permissions(permission_id);
            """);

        // Ensure columns exist on permissions (migration-safe)
        EnsureColumn(connection, "permissions", "group_code", "TEXT NOT NULL DEFAULT 'SYSTEM'");
        EnsureColumn(connection, "permissions", "feature_code", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "permissions", "operation_code", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "permissions", "name_ar", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "permissions", "name_en", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "permissions", "description", "TEXT");
        EnsureColumn(connection, "permissions", "display_order", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "permissions", "is_sensitive", "INTEGER NOT NULL DEFAULT 0");

        // Permission groups
        var groups = new (string Code, string Ar, string En, int Order)[]
        {
            ("SYSTEM", "النظام", "System", 1),
            ("COMPANY", "الشركات", "Companies", 2),
            ("INVENTORY", "المخزون", "Inventory", 3),
            ("ACCOUNTING", "المحاسبة", "Accounting", 4),
            ("VOUCHERS", "السندات", "Vouchers", 5),
            ("SALES", "المبيعات", "Sales", 6),
            ("PURCHASING", "المشتريات", "Purchasing", 7),
            ("POS", "نقاط البيع", "Point of Sale", 8),
            ("CUSTOMERS", "العملاء", "Customers", 9),
            ("REPRESENTATIVES", "المندوبون", "Representatives", 10),
            ("MANUFACTURING", "التصنيع", "Manufacturing", 11),
            ("BANKING", "البنوك", "Banking", 12),
            ("DELIVERY", "التوصيل", "Delivery", 13),
            ("REPORTING", "التقارير", "Reports", 14),
            ("DATA", "البيانات", "Data", 15),
            ("BACKUP", "النسخ الاحتياطي", "Backup", 16),
            ("AUDIT", "التدقيق", "Audit", 17),
            ("LICENSING", "الترخيص", "Licensing", 18),
            ("UPDATES", "التحديثات", "Updates", 19),
            ("AI", "الذكاء الاصطناعي", "AI", 20),
            ("HELP", "المساعدة", "Help", 21)
        };

        foreach (var g in groups)
            Execute(connection,
                "INSERT OR IGNORE INTO permission_groups(group_code,name_ar,name_en,display_order) VALUES($c,$a,$e,$o);",
                ("$c", g.Code), ("$a", g.Ar), ("$e", g.En), ("$o", g.Order));

        // Protected permission codes
        var protectedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PermCat.Company.Create, PermCat.Company.Update, PermCat.Company.Delete,
            PermCat.System.UsersView, PermCat.System.UsersCreate, PermCat.System.UsersUpdate,
            PermCat.System.UsersDisable, PermCat.System.UsersDelete,
            PermCat.System.RolesView, PermCat.System.RolesCreate, PermCat.System.RolesUpdate, PermCat.System.RolesDelete,
            PermCat.System.PermissionsView, PermCat.System.PermissionsManage, PermCat.System.PermissionTemplatesManage,
            PermCat.System.Admin, PermCat.System.FiscalRollForward
        };

        // Prefix → group mapping
        var groupByPrefix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SYS"] = "SYSTEM", ["ORG"] = "COMPANY", ["INV"] = "INVENTORY", ["ACC"] = "ACCOUNTING",
            ["VCH"] = "VOUCHERS", ["SALES"] = "SALES", ["PURCH"] = "PURCHASING", ["POS"] = "POS",
            ["CRM"] = "CUSTOMERS", ["REP"] = "REPRESENTATIVES", ["MFG"] = "MANUFACTURING",
            ["BANK"] = "BANKING", ["DELIVERY"] = "DELIVERY", ["REPORT"] = "REPORTING", ["DATA"] = "DATA",
            ["BACKUP"] = "BACKUP", ["AUDIT"] = "AUDIT", ["LIC"] = "LICENSING", ["UPDATE"] = "UPDATES",
            ["AI"] = "AI", ["HELP"] = "HELP"
        };

        // Seed all PermCat codes
        var order = 0;
        foreach (var code in PermCat.All)
        {
            var parts = code.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var prefix = parts.ElementAtOrDefault(0) ?? "SYS";
            var feature = parts.ElementAtOrDefault(1) ?? prefix;
            var operation = parts.ElementAtOrDefault(2) ?? (feature is "ADMIN" or "MAINTENANCE" ? "MANAGE" : "VIEW");
            var group = groupByPrefix.TryGetValue(prefix, out var gc) ? gc : "SYSTEM";
            var featureName = feature.Replace('_', ' ');
            var operationName = operation.Replace('_', ' ');
            var ar = $"{featureName} - {operationName}";
            var en = $"{featureName} - {operationName}";
            var sensitive = protectedCodes.Contains(code)
                || operation is "DELETE" or "REVERSE" or "POST" or "MANAGE" or "RESTORE" or "REOPEN" or "OVERRIDE";
            order++;

            Execute(connection, """
                INSERT INTO permissions(
                    permission_code, permission_name, group_code, feature_code, operation_code,
                    name_ar, name_en, description, display_order, is_sensitive)
                VALUES($c, $n, $g, $f, $o, $ar, $en, $d, $ord, $sens)
                ON CONFLICT(permission_code) DO UPDATE SET
                    group_code = excluded.group_code,
                    feature_code = excluded.feature_code,
                    operation_code = excluded.operation_code,
                    name_ar = excluded.name_ar,
                    name_en = excluded.name_en,
                    description = excluded.description,
                    display_order = excluded.display_order,
                    is_sensitive = excluded.is_sensitive,
                    is_active = 1;
                """,
                ("$c", code), ("$n", en), ("$g", group), ("$f", feature), ("$o", operation),
                ("$ar", ar), ("$en", en), ("$d", "Enterprise application permission"),
                ("$ord", order), ("$sens", sensitive ? 1 : 0));
        }

        // ROLE_USER — everything except protected system permissions
        Execute(connection, """
            DELETE FROM role_permissions
            WHERE role_id IN (SELECT role_id FROM roles WHERE role_code='ROLE_USER');

            INSERT OR IGNORE INTO role_permissions(role_id, permission_id)
            SELECT r.role_id, p.permission_id FROM roles r CROSS JOIN permissions p
            WHERE r.role_code='ROLE_USER' AND p.is_active=1 AND p.permission_code NOT IN
            (
                'ORG.COMPANY.CREATE','ORG.COMPANY.UPDATE','ORG.COMPANY.DELETE',
                'COMPANY_CREATE','COMPANY_EDIT','COMPANY_DELETE',
                'USER_VIEW','USER_CREATE','USER_EDIT','USER_DELETE',
                'ROLE_VIEW','ROLE_EDIT','PERMISSION_VIEW','SYS.ADMIN',
                'SYS.USERS.VIEW','SYS.USERS.CREATE','SYS.USERS.UPDATE','SYS.USERS.DISABLE','SYS.USERS.DELETE',
                'SYS.ROLES.VIEW','SYS.ROLES.CREATE','SYS.ROLES.UPDATE','SYS.ROLES.DELETE',
                'SYS.PERMISSIONS.VIEW','SYS.PERMISSIONS.MANAGE','SYS.PERMISSION_TEMPLATES.MANAGE',
                'ACC.FISCAL.ROLL_FORWARD'
            );
            """);

        // ROLE_VIEWER — read-only permissions
        Execute(connection, """
            DELETE FROM role_permissions
            WHERE role_id IN (SELECT role_id FROM roles WHERE role_code='ROLE_VIEWER');

            INSERT OR IGNORE INTO role_permissions(role_id, permission_id)
            SELECT r.role_id, p.permission_id FROM roles r CROSS JOIN permissions p
            WHERE r.role_code='ROLE_VIEWER' AND p.is_active=1
              AND (p.operation_code='VIEW' OR p.permission_code IN ('REPORT.VIEW','AUDIT.VIEW'));
            """);

        // ROLE_ADMINISTRATOR — all permissions
        Execute(connection, """
            INSERT OR IGNORE INTO role_permissions(role_id, permission_id)
            SELECT r.role_id, p.permission_id FROM roles r CROSS JOIN permissions p
            WHERE r.role_code='ROLE_ADMINISTRATOR' AND p.is_active=1;
            """);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helper: add column if not exists (migration-safe)
    // ═══════════════════════════════════════════════════════════════════
    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        Execute(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helper: execute SQL with parameters
    // ═══════════════════════════════════════════════════════════════════
    private static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}

}
