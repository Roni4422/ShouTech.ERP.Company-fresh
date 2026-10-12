// ========================================================================
// FILE: Common/Common.cs
// PROJECT: SHOUTECH ERP — shared helpers, lists, JSON, windowing
// Merged: AppFormat, AppLang, ArabCountries, ArabCurr, DatabaseProviderNames,
//         EntBase, JsonDefs, StatBarEn, ShellWinDock, WinMgr
// ========================================================================

#nullable enable

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System;
using ShouTech.App.Services;
using ShouTech.App.ViewModels;
using ShouTech.Domain.Entities;

namespace ShouTech.App.Common
{

    // ========== AppFormat.cs ==========
internal static class AppFormat
    {
        public static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string FormatTime(System.DateTime value) =>
            value.ToString("HH:mm:ss", Invariant);

        public static string FormatDate(System.DateTime value) =>
            value.ToString("dd/MM/yyyy", Invariant);

        public static string FormatDateTime(System.DateTime value) =>
            value.ToString("G", Invariant);

        public static string FormatTimestamp(System.DateTime value) =>
            value.ToString("yyyyMMdd_HHmmss", Invariant);

        public static string FormatNumber(int value) =>
            value.ToString("N0", Invariant);

        public static string FormatNumber(decimal value, string format) =>
            value.ToString(format, Invariant);

        public static string Format(string format, object? arg0) =>
            string.Format(Invariant, format, arg0);
    }

    // ========== AppLang.cs ==========
public sealed class AppLang
    {
        public required string Code { get; init; }
        public required string NameAr { get; init; }

        public string Display => NameAr;

        public static IReadOnlyList<AppLang> All { get; } =
        [
            new() { Code = "ar-SY", NameAr = "العربية (سوريا)" },
            new() { Code = "ar-SA", NameAr = "العربية (السعودية)" },
            new() { Code = "ar-AE", NameAr = "العربية (الإمارات)" },
            new() { Code = "ar-EG", NameAr = "العربية (مصر)" },
            new() { Code = "en-US", NameAr = "English (US)" },
        ];
    }

    // ========== ArabCountries.cs ==========
public sealed class ArabCountry
    {
        public required string Code { get; init; }
        public required string NameAr { get; init; }
        public required string NameEn { get; init; }

        public string Display => NameAr;

        public static IReadOnlyList<ArabCountry> All { get; } =
        [
            new() { Code = "SY", NameAr = "سوريا", NameEn = "Syria" },
            new() { Code = "SA", NameAr = "المملكة العربية السعودية", NameEn = "Saudi Arabia" },
            new() { Code = "AE", NameAr = "الإمارات العربية المتحدة", NameEn = "United Arab Emirates" },
            new() { Code = "KW", NameAr = "الكويت", NameEn = "Kuwait" },
            new() { Code = "QA", NameAr = "قطر", NameEn = "Qatar" },
            new() { Code = "BH", NameAr = "البحرين", NameEn = "Bahrain" },
            new() { Code = "OM", NameAr = "عُمان", NameEn = "Oman" },
            new() { Code = "JO", NameAr = "الأردن", NameEn = "Jordan" },
            new() { Code = "EG", NameAr = "مصر", NameEn = "Egypt" },
            new() { Code = "IQ", NameAr = "العراق", NameEn = "Iraq" },
            new() { Code = "LB", NameAr = "لبنان", NameEn = "Lebanon" },
            new() { Code = "MA", NameAr = "المغرب", NameEn = "Morocco" },
            new() { Code = "TN", NameAr = "تونس", NameEn = "Tunisia" },
            new() { Code = "DZ", NameAr = "الجزائر", NameEn = "Algeria" },
            new() { Code = "YE", NameAr = "اليمن", NameEn = "Yemen" },
            new() { Code = "LY", NameAr = "ليبيا", NameEn = "Libya" },
            new() { Code = "SD", NameAr = "السودان", NameEn = "Sudan" },
            new() { Code = "PS", NameAr = "فلسطين", NameEn = "Palestine" },
            new() { Code = "TR", NameAr = "تركيا", NameEn = "Turkey" },
        ];
    }

    // ========== ArabCurr.cs ==========
public sealed class ArabCurr
    {
        public required string Code { get; init; }
        public required string NameAr { get; init; }
        public required string Symbol { get; init; }

        public string Display => $"{NameAr} ({Code})";

        public static IReadOnlyList<ArabCurr> All { get; } =
        [
            new() { Code = "SYP", NameAr = "ليرة سورية", Symbol = "ل.س" },
            new() { Code = "SAR", NameAr = "ريال سعودي", Symbol = "ر.س" },
            new() { Code = "AED", NameAr = "درهم إماراتي", Symbol = "د.إ" },
            new() { Code = "KWD", NameAr = "دينار كويتي", Symbol = "د.ك" },
            new() { Code = "QAR", NameAr = "ريال قطري", Symbol = "ر.ق" },
            new() { Code = "BHD", NameAr = "دينار بحريني", Symbol = "د.ب" },
            new() { Code = "OMR", NameAr = "ريال عماني", Symbol = "ر.ع" },
            new() { Code = "JOD", NameAr = "دينار أردني", Symbol = "د.أ" },
            new() { Code = "EGP", NameAr = "جنيه مصري", Symbol = "ج.م" },
            new() { Code = "IQD", NameAr = "دينار عراقي", Symbol = "د.ع" },
            new() { Code = "LBP", NameAr = "ليرة لبنانية", Symbol = "ل.ل" },
            new() { Code = "MAD", NameAr = "درهم مغربي", Symbol = "د.م" },
            new() { Code = "TND", NameAr = "دينار تونسي", Symbol = "د.ت" },
            new() { Code = "DZD", NameAr = "دينار جزائري", Symbol = "د.ج" },
            new() { Code = "YER", NameAr = "ريال يمني", Symbol = "ر.ي" },
            new() { Code = "LYD", NameAr = "دينار ليبي", Symbol = "د.ل" },
            new() { Code = "SDG", NameAr = "جنيه سوداني", Symbol = "ج.س" },
            new() { Code = "TRY", NameAr = "ليرة تركية", Symbol = "₺" },
        ];
    }

    // ========== DatabaseProviderNames.cs ==========
public static class DatabaseProviderNames
    {
        public const string PostgreSql = "PostgreSQL";
        public const string Sqlite = "SQLite";

        public static string Normalize(string? provider)
        {
            var trimmed = provider?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;

            if (string.Equals(trimmed, PostgreSql, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "postgres", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "pgsql", StringComparison.OrdinalIgnoreCase))
                return PostgreSql;

            if (string.Equals(trimmed, Sqlite, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "sqlite3", StringComparison.OrdinalIgnoreCase))
                return Sqlite;

            return trimmed;
        }

        public static bool IsSupported(string? provider) =>
            string.Equals(Normalize(provider), PostgreSql, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Normalize(provider), Sqlite, StringComparison.OrdinalIgnoreCase);

        public static bool IsCoreImplemented(string? provider) => IsSupported(provider);

        public static void EnsureCoreImplementation(string? provider)
        {
            if (!IsSupported(provider))
                throw new NotSupportedException(
                    $"Unsupported database provider '{provider}'. Choose {Sqlite} or {PostgreSql}.");
        }
    }

    // ========== EntBase.cs ==========
/// <summary>
/// Base class for all domain entities (GUID + RowVersion + SoftDelete + Audit).
/// </summary>
public abstract class EntBase
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    [Timestamp]
    public byte[] RowVersion { get; protected set; } = Array.Empty<byte>();

    public bool IsDeleted { get; protected set; }
    public DateTime? DeletedAt { get; protected set; }
    public Guid? DeletedBy { get; protected set; }

    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public Guid? CreatedBy { get; protected set; }
    public DateTime? ModifiedAt { get; protected set; }
    public Guid? ModifiedBy { get; protected set; }

    public virtual void SoftDelete(Guid deletedBy)
    {
        if (IsDeleted) return;
        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        DeletedBy = deletedBy;
        ModifiedAt = DeletedAt;
        ModifiedBy = deletedBy;
    }

    public virtual void Restore(Guid restoredBy)
    {
        if (!IsDeleted) return;
        IsDeleted = false;
        DeletedAt = null;
        DeletedBy = null;
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = restoredBy;
    }

    protected void Touch(Guid modifiedBy)
    {
        ModifiedAt = DateTime.UtcNow;
        ModifiedBy = modifiedBy;
    }
}

    // ========== JsonDefs.cs ==========
internal static class JsonDefs
    {
        public static JsonSerializerOptions Indented { get; } = new()
        {
            WriteIndented = true
        };

        public static JsonSerializerOptions AppSetngRead { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    // ========== StatBarEn.cs ==========
/// <summary>Fixed English text for the main window status bar (never localized).</summary>
    internal static class StatBarEn
    {
        public const string Ready = "Ready";
        public const string Dashboard = "Executive Dashboard";
        public const string Loading = "Loading...";
        public const string About = "About";
        public const string Save = "Saving...";
        public const string Undo = "Undo";
        public const string Redo = "Redo";
        public const string Print = "Printing...";
        public const string Search = "Search...";
        public const string SaveSuccess = "Saved successfully";
        public const string OperationFailed = "Operation failed";
        public const string ViewerMode = "Read-only mode";
        public const string DarkMode = "Dark mode";
        public const string LightMode = "Light mode";
        public const string DefaultCompany = "Main Company";
        public const string Version = "Version 10.6.0";
        public const string UserLabel = "User";

        public static string RoleLabel(UsrAccLvl level) => level switch
        {
            UsrAccLvl.Administrator => "Administrator",
            UsrAccLvl.Viewer => "Viewer",
            _ => "User"
        };

        public static string ModuleName(string moduleKey) => moduleKey switch
        {
            "File" => "File",
            "Inventory" => "Inventory",
            "Accounting" => "Accounting",
            "Vouchers" => "Vouchers",
            "Invoices" => "Invoices",
            "PointOfSale" => "Point of Sale",
            "Orders" => "Orders",
            "Customers" => "Customers",
            "SalesRepresentative" => "Sales Representative",
            "Manufacturing" => "Manufacturing",
            "Banks" => "Banks",
            "Reports" => "Reports",
            "Tools" => "Tools",
            "Administration" => "Administration",
            "View" => "View",
            "Session" => "Session",
            "Help" => "Help",
            _ => "General"
        };

        public static string BuildUserDisplay(string username, string fullName, UsrAccLvl accessLevel)
        {
            var role = RoleLabel(accessLevel);
            if (string.IsNullOrWhiteSpace(username))
                return role;

            if (string.IsNullOrWhiteSpace(fullName) || fullName.Equals(username, System.StringComparison.OrdinalIgnoreCase))
                return $"{UserLabel}: {username} — {role}";

            return $"{UserLabel}: {username} — {role}";
        }
    }

    // ========== ShellWinDock.cs ==========
/// <summary>Minimize shell child windows to the main window status bar; restore on demand.</summary>
    public static class ShellWinDock
    {
        public static void Attach(Window child, Window shellHost)
        {
            ArgumentNullException.ThrowIfNull(child);
            ArgumentNullException.ThrowIfNull(shellHost);

            child.Owner = null;
            child.StateChanged += (_, _) => OnStateChanged(child, shellHost);
            child.Closed += (_, _) => Detach(child, shellHost);
        }

        private static void OnStateChanged(Window child, Window shellHost)
        {
            if (child.WindowState != WindowState.Minimized)
                return;

            if (shellHost.DataContext is not MainShVM viewModel)
                return;

            Dock(child, viewModel);
        }

        public static void Dock(Window window, MainShVM viewModel)
        {
            var title = string.IsNullOrWhiteSpace(window.Title) ? "نافذة" : window.Title;

            window.WindowState = WindowState.Normal;
            window.ShowInTaskbar = false;
            window.Hide();

            viewModel.DockWindow(title, window);
        }

        public static void Restore(DockedWinItem item, MainShVM viewModel)
        {
            if (item.Window == null)
                return;

            item.Window.ShowInTaskbar = true;
            item.Window.Show();
            item.Window.WindowState = WindowState.Normal;
            item.Window.Activate();
            viewModel.UndockWindow(item);
        }

        private static void Detach(Window child, Window shellHost)
        {
            if (shellHost.DataContext is not MainShVM viewModel)
                return;

            viewModel.RemoveDockedWindow(child);
        }
    }

    // ========== WinMgr.cs ==========
/// <summary>Opens top-level windows in standard Windows SDI style (peer windows, taskbar, activate on focus).</summary>
    public static class WinMgr
    {
        private static int _cascadeOffset;

        public static void ShowIndependent(Window window, Window? shellHost = null)
        {
            ArgumentNullException.ThrowIfNull(window);

            window.Owner = null;
            window.ShowInTaskbar = true;
            window.ShowActivated = true;

            if (shellHost != null && shellHost.IsLoaded)
            {
                PositionNearShell(window, shellHost);
                ShellWinDock.Attach(window, shellHost);
            }
            else if (shellHost != null)
            {
                ShellWinDock.Attach(window, shellHost);
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            window.Show();
            window.Activate();
        }

        private static void PositionNearShell(Window window, Window shellHost)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;

            var slot = _cascadeOffset++ % 8;
            var offset = 24 * slot;
            var desiredLeft = shellHost.Left + shellHost.Width - window.Width - 32 - offset;
            var desiredTop = shellHost.Top + 72 + offset;

            if (desiredLeft < shellHost.Left + 24)
                desiredLeft = shellHost.Left + 24 + offset;

            if (desiredTop + window.Height > shellHost.Top + shellHost.Height - 48)
                desiredTop = shellHost.Top + shellHost.Height - window.Height - 48;

            window.Left = Math.Max(0, desiredLeft);
            window.Top = Math.Max(0, desiredTop);
        }

        public static void BringToFront(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);

            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Show();
            window.Activate();
            window.Focus();
        }
    }

}
