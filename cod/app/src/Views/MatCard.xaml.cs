using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using ShouTech.App.Localization;
using ShouTech.App.Services;

namespace ShouTech.App.Views;

// ====================================================================
//  MatUnitRow — one row in the Units & Prices grid
// ====================================================================
public sealed class MatUnitRow : INotifyPropertyChanged
{
    private int _lineNo;
    private string _unitName = "";
    private decimal? _factor;
    private decimal? _priceSale, _pricePurchase, _priceWholesale, _priceRetail,
                     _priceSpecial, _priceExport, _priceDistributor;
    private string _barcode = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public int LineNo
    {
        get => _lineNo;
        set { if (_lineNo == value) return; _lineNo = value; Notify(nameof(LineNo)); }
    }

    public string UnitName
    {
        get => _unitName;
        set { var v = value ?? ""; if (_unitName == v) return; _unitName = v; Notify(nameof(UnitName)); }
    }

    public decimal? Factor
    {
        get => _factor;
        set { _factor = (value is null || value <= 0) ? null : value; Notify(nameof(Factor)); }
    }

    public decimal? PriceSale        { get => _priceSale;        set { if (_priceSale == value) return;        _priceSale = value;        Notify(nameof(PriceSale)); } }
    public decimal? PricePurchase    { get => _pricePurchase;    set { if (_pricePurchase == value) return;    _pricePurchase = value;    Notify(nameof(PricePurchase)); } }
    public decimal? PriceWholesale   { get => _priceWholesale;   set { if (_priceWholesale == value) return;   _priceWholesale = value;   Notify(nameof(PriceWholesale)); } }
    public decimal? PriceRetail      { get => _priceRetail;      set { if (_priceRetail == value) return;      _priceRetail = value;      Notify(nameof(PriceRetail)); } }
    public decimal? PriceSpecial     { get => _priceSpecial;     set { if (_priceSpecial == value) return;     _priceSpecial = value;     Notify(nameof(PriceSpecial)); } }
    public decimal? PriceExport      { get => _priceExport;      set { if (_priceExport == value) return;      _priceExport = value;      Notify(nameof(PriceExport)); } }
    public decimal? PriceDistributor { get => _priceDistributor; set { if (_priceDistributor == value) return; _priceDistributor = value; Notify(nameof(PriceDistributor)); } }

    public string Barcode
    {
        get => _barcode;
        set { var v = value ?? ""; if (_barcode == v) return; _barcode = v; Notify(nameof(Barcode)); }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasContent =>
        !string.IsNullOrWhiteSpace(UnitName)
        || Factor.HasValue
        || PriceSale.HasValue || PricePurchase.HasValue || PriceWholesale.HasValue
        || PriceRetail.HasValue || PriceSpecial.HasValue || PriceExport.HasValue
        || PriceDistributor.HasValue || !string.IsNullOrWhiteSpace(Barcode);

    private void Notify(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ====================================================================
//  MatComboOption — combo item with a stable key + localized display
// ====================================================================
public sealed class MatComboOption
{
    public MatComboOption(string key, string display)
    {
        Key = key ?? string.Empty;
        Display = display ?? string.Empty;
    }

    public string Key { get; }
    public string Display { get; }

    public override string ToString() => Display;
}

public sealed class MatCategoryOption
{
    public MatCategoryOption(string key, string display, string code = "", string nameAr = "", string nameEn = "")
    {
        Key = key;
        Display = display;
        Code = code;
        NameAr = nameAr;
        NameEn = nameEn;
    }

    public string Key { get; }
    public string Display { get; }
    public string Code { get; }
    public string NameAr { get; }
    public string NameEn { get; }

    public override string ToString() => Display;
}

// ====================================================================
//  MatCard — Material Card window
// ====================================================================
public partial class MatCard : BaseWin
{
    // ---------- State ----------
    private readonly ObservableCollection<MatUnitRow> _units = new();
    private readonly ObservableCollection<MatAttrRow> _attrs = new();
    private List<MatRepo.MatRec> _cache = new();
    private List<MatCategoryOption> _categoryOptions = new();

    private int _index = -1;
    private bool _isNew;
    private bool _isEditing;
    private long _currentId;
    private MatRepo.MatRec? _loaded;

    private static readonly Regex NumericInputRegex =
        new(@"^[0-9]*\.?[0-9]*$", RegexOptions.Compiled);

    // ================================================================
    //  Static constructor: numeric input filter for cells
    // ================================================================
    static MatCard()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGridCell),
            TextCompositionManager.PreviewTextInputEvent,
            new TextCompositionEventHandler(OnAnyDataGridCellPreviewTextInput),
            handledEventsToo: true);

        // Keep numeric text entry Western even when the active keyboard layout
        // produces Arabic-Indic or Eastern Arabic/Persian digits.
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            TextCompositionManager.PreviewTextInputEvent,
            new TextCompositionEventHandler(OnTextBoxPreviewTextInput),
            handledEventsToo: true);
    }

    private static string ToWesternDigits(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '\u0660' && chars[i] <= '\u0669')
                chars[i] = (char)('0' + chars[i] - '\u0660');
            else if (chars[i] >= '\u06F0' && chars[i] <= '\u06F9')
                chars[i] = (char)('0' + chars[i] - '\u06F0');
        }
        return new string(chars);
    }

    private static void OnTextBoxPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox || Window.GetWindow(textBox) is not MatCard)
            return;

        var western = ToWesternDigits(e.Text);
        if (string.Equals(western, e.Text, StringComparison.Ordinal)) return;

        e.Handled = true;
        var start = textBox.SelectionStart;
        var length = textBox.SelectionLength;
        textBox.SelectedText = western;
        textBox.CaretIndex = start + western.Length;
    }

    private static void OnAnyDataGridCellPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not DataGridCell cell) return;
        if (Window.GetWindow(cell) is not MatCard) return;

        var column = cell.Column;
        if (column == null || !IsNumericColumn(column)) return;

        var text = e.Text;
        if (string.IsNullOrEmpty(text)) return;

        // Validate after translating Arabic-Indic digits to Western digits.
        if (!NumericInputRegex.IsMatch(ToWesternDigits(text)))
            e.Handled = true;
    }

    private static bool IsNumericColumn(DataGridColumn? column)
    {
        if (column == null) return false;

        var binding = (column as DataGridBoundColumn)?.Binding as System.Windows.Data.Binding;
        var path = binding?.Path?.Path ?? "";

        return path is nameof(MatUnitRow.Factor)
            or nameof(MatUnitRow.PriceSale)
            or nameof(MatUnitRow.PricePurchase)
            or nameof(MatUnitRow.PriceWholesale)
            or nameof(MatUnitRow.PriceRetail)
            or nameof(MatUnitRow.PriceSpecial)
            or nameof(MatUnitRow.PriceExport)
            or nameof(MatUnitRow.PriceDistributor);
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T t) return t;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    // ================================================================
    //  Constructor
    // ================================================================
    public MatCard()
    {
        InitializeComponent();
        VisuId.ApplyWindow(this);
        ApplyDirection();

        try
        {
            AppServiceHost.Localization.LanguageChanged += OnLanguageChanged;
            Closed += (_, _) =>
            {
                try { AppServiceHost.Localization.LanguageChanged -= OnLanguageChanged; }
                catch { }
                RestoreOwnerWindow();
            };
        }
        catch { }

        try
        {
            // Unit rows are added through the explicit toolbar so the grid's
            // placeholder row does not look like an incomplete business row.
            DgUnits.CanUserAddRows = false;
            DgAttributes.CanUserAddRows = true;

            DgUnits.ItemsSource = _units;
            DgAttributes.ItemsSource = _attrs;
            _units.CollectionChanged += (_, _) => RenumberUnits();

            InitCombos();
            EnsureUnitRows(UnitWorkspaceRows);
            ReloadCache();

            if (_cache.Count > 0)
            {
                _index = 0;
                LoadCurrent();
            }
            else
            {
                ClearForm();
                SetEditMode(false);
            }

            ShowSection(0);
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Failed to load material data:\n" +
                ex.GetType().Name + ": " + ex.Message +
                (ex.InnerException != null ? "\n" + ex.InnerException.Message : ""),
                "Material Card",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            try
            {
                _cache = new();
                _index = -1;
                ClearForm();
                SetEditMode(false);
                ShowSection(0);
            }
            catch { }
        }
    }

    // ================================================================
    //  Restore owner window when this dialog closes
    // ================================================================
    private void RestoreOwnerWindow()
    {
        try
        {
            var owner = Owner;
            if (owner == null) return;

            if (owner.WindowState == WindowState.Minimized)
                owner.WindowState = WindowState.Normal;

            owner.Activate();
        }
        catch { }
    }

    // ================================================================
    //  Localization + direction
    // ================================================================
    private void ApplyDirection()
    {
        var direction = IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        FlowDirection = direction;
        if (TxtName != null)
        {
            TxtName.FlowDirection = direction;
            TxtName.TextAlignment = IsRtl ? TextAlignment.Right : TextAlignment.Left;
        }

        try { UiLoczr.ApplyToWindow(this); } catch { }

        try
        {
            NumberSubstitution.SetCultureSource(this, NumberCultureSource.Override);
            NumberSubstitution.SetCultureOverride(this, CultureInfo.GetCultureInfo("en-US"));
            NumberSubstitution.SetSubstitution(this, System.Windows.Media.NumberSubstitutionMethod.European);
        }
        catch { }

        RefreshLocalizedTexts();
        UpdateNavigationArrows();
    }

    private static bool IsRtl => LangDirSv.IsRtl(App.CurrentLanguageCode);

    private static string L(string key, string fallback) => Loc.Get(key, fallback);

    private void RefreshLocalizedTexts()
    {
        Title = L("Inventory.MaterialCard", "Material Card");

        if (DgUnits != null && DgUnits.Columns.Count >= 11)
        {
            DgUnits.Columns[0].Header  = "#";
            DgUnits.Columns[1].Header  = L("MatCard.Unit",        "Unit");
            DgUnits.Columns[2].Header  = L("MatCard.Factor",      "Factor");
            DgUnits.Columns[3].Header  = L("MatCard.Sale",        "Sale");
            DgUnits.Columns[4].Header  = L("MatCard.Purchase",    "Purchase");
            DgUnits.Columns[5].Header  = L("MatCard.Wholesale",   "Wholesale");
            DgUnits.Columns[6].Header  = L("MatCard.Retail",      "Retail");
            DgUnits.Columns[7].Header  = L("MatCard.Special",     "Special");
            DgUnits.Columns[8].Header  = L("MatCard.Export",      "Export");
            DgUnits.Columns[9].Header  = L("MatCard.Distributor", "Distributor");
            DgUnits.Columns[10].Header = L("MatCard.Barcode",     "Barcode");
        }

        if (DgAttributes != null && DgAttributes.Columns.Count >= 3)
        {
            DgAttributes.Columns[0].Header = L("MatCard.AttrGroup", "Group");
            DgAttributes.Columns[1].Header = L("MatCard.AttrKey",   "Key");
            DgAttributes.Columns[2].Header = L("MatCard.AttrValue", "Value");
        }

        RefreshComboDisplays();
        InitCategories(GetComboKey(CmbCategory));
        InitCategories();
    }

    private void UpdateNavigationArrows()
    {
        var rtl = IsRtl;
        BtnNavFirst.Content = rtl ? "▶|" : "|◀";
        BtnNavPrev.Content  = rtl ? "▶"  : "◀";
        BtnNavNext.Content  = rtl ? "◀"  : "▶";
        BtnNavLast.Content  = rtl ? "|◀" : "▶|";

        BtnNavFirst.FlowDirection = FlowDirection.LeftToRight;
        BtnNavPrev.FlowDirection  = FlowDirection.LeftToRight;
        BtnNavNext.FlowDirection  = FlowDirection.LeftToRight;
        BtnNavLast.FlowDirection  = FlowDirection.LeftToRight;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        try { Dispatcher.Invoke(ApplyDirection); }
        catch { ApplyDirection(); }
    }

    // ================================================================
    //  Combos
    // ================================================================
    private void InitCombos()
    {
        CmbItemType.ItemsSource = new[]
        {
            new MatComboOption("Stock",    L("MatCard.ItemType.Stock",    "Stock")),
            new MatComboOption("Service",  L("MatCard.ItemType.Service",  "Service")),
            new MatComboOption("NonStock", L("MatCard.ItemType.NonStock", "Non-stock")),
            new MatComboOption("Kit",      L("MatCard.ItemType.Kit",      "Kit"))
        };

        CmbStatus.ItemsSource = new[]
        {
            new MatComboOption("Active",   L("MatCard.Status.Active",   "Active")),
            new MatComboOption("Inactive", L("MatCard.Status.Inactive", "Inactive"))
        };

        CmbCostMethod.ItemsSource = new[]
        {
            new MatComboOption("WeightedAvg",  L("MatCard.CostMethod.WeightedAvg",  "Weighted Average")),
            new MatComboOption("FIFO",         L("MatCard.CostMethod.FIFO",         "FIFO")),
            new MatComboOption("LIFO",         L("MatCard.CostMethod.LIFO",         "LIFO")),
            new MatComboOption("Standard",     L("MatCard.CostMethod.Standard",     "Standard")),
            new MatComboOption("LastPurchase", L("MatCard.CostMethod.LastPurchase", "Last Purchase"))
        };

        CmbAbcClass.ItemsSource = new[]
        {
            new MatComboOption("A", "A"),
            new MatComboOption("B", "B"),
            new MatComboOption("C", "C")
        };

        CmbPriceCurrency.ItemsSource = new[]
        {
            new MatComboOption("SYP", "SYP"),
            new MatComboOption("USD", "USD"),
            new MatComboOption("EUR", "EUR"),
            new MatComboOption("TRY", "TRY"),
            new MatComboOption("SAR", "SAR"),
            new MatComboOption("AED", "AED")
        };

        CmbColor.ItemsSource = BuildColorOptions();
        CmbColor.SelectedIndex = 0;

        InitGroups();
        InitWarehouses();
        RefreshComboDisplays();
        InitCategories();
    }

    private void InitCategories(string? preferredValue = null)
    {
        preferredValue ??= GetComboKey(CmbCategory);
        var categories = MatRepo.LoadProductCategories();
        var isArabic = string.Equals(App.CurrentLanguageCode?.Trim(), "ar", StringComparison.OrdinalIgnoreCase);

        _categoryOptions = categories
            .Where(category => !string.IsNullOrWhiteSpace(category.Id))
            .Select(category =>
            {
                var name = isArabic
                    ? (string.IsNullOrWhiteSpace(category.NameAr) ? category.NameEn : category.NameAr)
                    : (string.IsNullOrWhiteSpace(category.NameEn) ? category.NameAr : category.NameEn);
                var display = string.IsNullOrWhiteSpace(name)
                    ? category.Code
                    : string.IsNullOrWhiteSpace(category.Code) ? name : $"{category.Code} - {name}";
                return new MatCategoryOption(category.Id, display, category.Code, category.NameAr, category.NameEn);
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferredValue) && !_categoryOptions.Any(option =>
                string.Equals(option.Key, preferredValue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.Code, preferredValue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.NameAr, preferredValue, StringComparison.OrdinalIgnoreCase)
                || string.Equals(option.NameEn, preferredValue, StringComparison.OrdinalIgnoreCase)))
        {
            _categoryOptions.Add(new MatCategoryOption(preferredValue, preferredValue));
        }

        CmbCategory.ItemsSource = _categoryOptions;
        SetCategory(preferredValue);
    }

    private void SetCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            CmbCategory.SelectedIndex = -1;
            return;
        }

        var option = _categoryOptions.FirstOrDefault(item =>
            string.Equals(item.Key, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Code, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.NameAr, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.NameEn, value, StringComparison.OrdinalIgnoreCase));
        CmbCategory.SelectedValue = option?.Key;
    }

    // ----------------------------------------------------------------
    //  Color palette - user-selectable, persisted in materials.color
    // ----------------------------------------------------------------
    private static readonly string[] ColorKeys =
    {
        "None", "White", "Black", "Red", "Green", "Blue",
        "Yellow", "Orange", "Brown", "Gray", "Silver", "Gold"
    };

    private static List<MatComboOption> BuildColorOptions() =>
        ColorKeys
            .Select(k => new MatComboOption(k, L("MatCard.Color." + k, k)))
            .ToList();

    // ----------------------------------------------------------------
    //  Warehouses - default-warehouse combo
    // ----------------------------------------------------------------
    private void InitWarehouses()
    {
        var warehouses = new List<MatComboOption>();

        try
        {
            var list = MatRepo.LoadWarehouses();

            if (list != null)
            {
                var lang = App.CurrentLanguageCode?.Trim().ToLowerInvariant();

                foreach (var w in list)
                {
                    if (w == null || w.IsDeleted) continue;

                    var code = w.Code.ToString(CultureInfo.InvariantCulture);

                    var name = lang == "ar"
                        ? (string.IsNullOrWhiteSpace(w.NameAr) ? w.NameEn : w.NameAr)
                        : (string.IsNullOrWhiteSpace(w.NameEn) ? w.NameAr : w.NameEn);

                    var display = string.IsNullOrWhiteSpace(name)
                        ? code
                        : code + " - " + name;

                    warehouses.Add(new MatComboOption(code, display));
                }
            }
        }
        catch
        {
            // The warehouse store may be unavailable; the combo simply stays
            // editable so the user can still type a warehouse code manually.
        }

        CmbDefaultWarehouse.ItemsSource = warehouses;
    }

    private void InitGroups()
    {
        var groups = new List<MatComboOption>();

        try
        {
            var list = MatRepo.LoadGroups();

            if (list != null)
            {
                var lang = App.CurrentLanguageCode?.Trim().ToLowerInvariant();

                foreach (var item in list)
                {
                    if (item == null || item.IsDeleted) continue;

                    var code = item.Code;
                    if (string.IsNullOrWhiteSpace(code)) continue;

                    var name = lang == "ar"
                        ? (string.IsNullOrWhiteSpace(item.Name) ? item.NameEn : item.Name)
                        : (string.IsNullOrWhiteSpace(item.NameEn) ? item.Name : item.NameEn);

                    var display = string.IsNullOrWhiteSpace(name) ? code : code + " - " + name;
                    groups.Add(new MatComboOption(code, display));
                }
            }
        }
        catch { }

        if (groups.Count == 0)
        {
            try
            {
                groups = MatRepo.LoadAll()
                    .Select(x => x.GroupCode)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => new MatComboOption(x!, x!))
                    .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .OrderBy(x => x.Key)
                    .ToList();
            }
            catch { groups = new(); }
        }

        CmbGroup.ItemsSource = groups;
    }

    private void RefreshComboDisplays()
    {
        RefreshStaticCombo(CmbItemType, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Stock"]    = L("MatCard.ItemType.Stock",    "Stock"),
            ["Service"]  = L("MatCard.ItemType.Service",  "Service"),
            ["NonStock"] = L("MatCard.ItemType.NonStock", "Non-stock"),
            ["Kit"]      = L("MatCard.ItemType.Kit",      "Kit")
        });

        RefreshStaticCombo(CmbStatus, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Active"]   = L("MatCard.Status.Active",   "Active"),
            ["Inactive"] = L("MatCard.Status.Inactive", "Inactive")
        });

        RefreshStaticCombo(CmbCostMethod, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["WeightedAvg"]  = L("MatCard.CostMethod.WeightedAvg",  "Weighted Average"),
            ["FIFO"]         = L("MatCard.CostMethod.FIFO",         "FIFO"),
            ["LIFO"]         = L("MatCard.CostMethod.LIFO",         "LIFO"),
            ["Standard"]     = L("MatCard.CostMethod.Standard",     "Standard"),
            ["LastPurchase"] = L("MatCard.CostMethod.LastPurchase", "Last Purchase")
        });

        var colorLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in ColorKeys)
            colorLabels[key] = L("MatCard.Color." + key, key);

        RefreshStaticCombo(CmbColor, colorLabels);
    }

    private static void RefreshStaticCombo(ComboBox combo, IReadOnlyDictionary<string, string> labels)
    {
        var selected = GetComboKey(combo);
        var options = labels.Select(x => new MatComboOption(x.Key, x.Value)).ToList();

        combo.ItemsSource = options;

        if (!string.IsNullOrWhiteSpace(selected)) SetCombo(combo, selected);
    }

    // ================================================================
    //  Grid row management
    // ================================================================
    private const int UnitWorkspaceRows = 200;

    private void EnsureUnitRows(int count)
    {
        while (_units.Count < count)
        {
            _units.Add(new MatUnitRow
            {
                LineNo = _units.Count + 1,
                UnitName = "",
                Factor = null,
                Barcode = ""
            });
        }
        RenumberUnits();
    }

    private void RenumberUnits()
    {
        for (var i = 0; i < _units.Count; i++) _units[i].LineNo = i + 1;
    }

    // ================================================================
    //  Sections
    // ================================================================
    private void ShowSection(int index)
    {
        PanelDesc.Visibility     = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelUnits.Visibility    = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelStock.Visibility    = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelAccounts.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelExtra.Visibility    = index == 4 ? Visibility.Visible : Visibility.Collapsed;

        var on  = (Style)FindResource("SecBtnOn");
        var off = (Style)FindResource("SecBtn");

        NavDesc.Style     = index == 0 ? on : off;
        NavUnits.Style    = index == 1 ? on : off;
        NavStock.Style    = index == 2 ? on : off;
        NavAccounts.Style = index == 3 ? on : off;
        NavExtra.Style    = index == 4 ? on : off;
    }

    private void NavSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var index))
            ShowSection(index);
    }

    // ================================================================
    //  Cache + navigation
    // ================================================================
    private void ReloadCache()
    {
        try { _cache = MatRepo.LoadAll(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            _cache = new();
        }
        UpdateNav();
    }

    private void UpdateNav()
    {
        TxtNavPos.Text = _cache.Count == 0 ? "0 / 0" : $"{_index + 1} / {_cache.Count}";
    }

    private void LoadCurrent()
    {
        if (_index < 0 || _index >= _cache.Count) { ClearForm(); return; }

        try
        {
            MatRepo.MatRec? full = null;
            try { full = MatRepo.LoadById(_cache[_index].Id, true); }
            catch { }

            var m = full ?? _cache[_index];

            _loaded = m;
            _currentId = m.Id;
            _isNew = false;

            BindToUi(m);
            SetEditMode(false);
            UpdateNav();
            UpdateStatusBar();

            TxtMeta.Text = "ID: " + m.Id;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Unable to load material:\n" + ex.Message,
                Title, MessageBoxButton.OK, MessageBoxImage.Error);

            ClearForm();
            SetEditMode(false);
        }
    }

    // ================================================================
    //  Clear / Bind / Collect
    // ================================================================
    private void ClearForm()
    {
        _currentId = 0;
        _loaded = null;

        TxtCode.Clear();
        TxtItemNo.Clear();
        TxtName.Clear();
        TxtNameEn.Clear();
        CmbCategory.SelectedIndex = -1;
        TxtBrand.Clear();
        TxtModel.Clear();
        TxtSizeSpec.Clear();
        TxtManufacturer.Clear();
        TxtOriginCountry.Clear();
        TxtSource.Clear();
        TxtAltCode.Clear();
        TxtGtin.Clear();
        TxtSupplierCode.Clear();
        TxtNotes.Clear();

        TxtMinQty.Text = "0";
        TxtMaxQty.Text = "0";
        TxtCost.Text = "0";
        TxtTaxRate.Text = "0";
        TxtQtyOnHand.Text = "0";
        TxtReorderQty.Text = "0";
        TxtSafetyStock.Text = "0";
        TxtShelfBin.Clear();
        TxtLeadTimeDays.Text = "0";
        TxtWeightNet.Text = "0";
        TxtWeightGross.Text = "0";
        TxtShelfLifeDays.Text = "0";
        TxtImagePath.Clear();

        TxtAccInventory.Clear();
        TxtAccSales.Clear();
        TxtAccSalesReturn.Clear();
        TxtAccCogs.Clear();
        TxtAccPurchase.Clear();
        TxtAccPurchaseReturn.Clear();
        TxtAccExpense.Clear();
        TxtAccWip.Clear();

        ChkInactive.IsChecked = false;
        ChkIsStocked.IsChecked = true;
        ChkIsPurchased.IsChecked = true;
        ChkIsSold.IsChecked = true;
        ChkIsProduced.IsChecked = false;
        ChkIsService.IsChecked = false;
        ChkHasSerial.IsChecked = false;
        ChkHasExpiry.IsChecked = false;
        ChkHasBatch.IsChecked = false;
        ChkIsTaxable.IsChecked = false;
        ChkPriceIncludesTax.IsChecked = false;
        ChkAllowNegative.IsChecked = false;
        ChkTrackLocation.IsChecked = false;
        ChkQualityCheck.IsChecked = false;
        ChkAllowSale.IsChecked = true;
        ChkAllowPurchase.IsChecked = true;
        ChkAllowReturn.IsChecked = true;
        ChkSalesBlocked.IsChecked = false;

        SetCombo(CmbStatus, "Active");
        SetCombo(CmbItemType, "Stock");
        SetCombo(CmbColor, "None");

        CmbGroup.SelectedIndex = -1;
        CmbAbcClass.SelectedIndex = -1;
        CmbPriceCurrency.SelectedIndex = -1;
        CmbCostMethod.SelectedIndex = -1;
        CmbDefaultWarehouse.SelectedIndex = -1;

        _units.Clear();
        EnsureUnitRows(UnitWorkspaceRows);
        _attrs.Clear();

        ImgPreview.Source = null;
        TxtMeta.Text = "";
    }

    private static decimal D(string? value)
    {
        if (decimal.TryParse(value?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            return result;

        if (decimal.TryParse(value?.Trim(), NumberStyles.Any, CultureInfo.CurrentCulture, out result))
            return result;

        return 0;
    }

    private static int I(string? value)
    {
        return int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static string S(string? value) => value?.Trim() ?? "";

    private static string? SN(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void BindToUi(MatRepo.MatRec m)
    {
        TxtCode.Text = m.Code;
        TxtItemNo.Text = m.Id > 0 ? m.Id.ToString(CultureInfo.InvariantCulture) : "";
        TxtName.Text = m.Name;
        TxtNameEn.Text = m.NameEn;

        SetCombo(CmbGroup, m.GroupCode);
        SetCombo(CmbStatus, m.Status);
        SetCombo(CmbItemType, m.ItemType);

        ChkInactive.IsChecked =
            string.Equals(m.Status, "Inactive", StringComparison.OrdinalIgnoreCase);

        SetCategory(m.Category);
        TxtBrand.Text = m.Brand ?? "";
        TxtModel.Text = m.Model ?? "";

        SetCombo(CmbColor,
            string.IsNullOrWhiteSpace(m.Color) ? "None" : m.Color);

        TxtSizeSpec.Text = m.SizeSpec ?? "";
        TxtManufacturer.Text = m.Manufacturer ?? "";
        TxtOriginCountry.Text = m.OriginCountry ?? "";
        TxtSource.Text = m.Source ?? "";
        TxtAltCode.Text = m.AltCode ?? "";
        TxtGtin.Text = m.Gtin ?? "";
        TxtSupplierCode.Text = m.SupplierCode ?? "";
        SetCombo(CmbAbcClass, m.AbcClass);
        TxtNotes.Text = m.Notes ?? "";

        ChkIsStocked.IsChecked = m.IsStocked;
        ChkIsPurchased.IsChecked = m.IsPurchased;
        ChkIsSold.IsChecked = m.IsSold;
        ChkIsProduced.IsChecked = m.IsProduced;
        ChkIsService.IsChecked = m.IsService;

        TxtMinQty.Text = m.MinQty.ToString(CultureInfo.InvariantCulture);
        TxtMaxQty.Text = m.MaxQty.ToString(CultureInfo.InvariantCulture);
        SetCombo(CmbPriceCurrency, m.PriceCurrency);
        TxtCost.Text = m.Cost.ToString(CultureInfo.InvariantCulture);
        SetCombo(CmbCostMethod, m.CostMethod);
        TxtTaxRate.Text = m.TaxRate.ToString(CultureInfo.InvariantCulture);
        ChkIsTaxable.IsChecked = m.IsTaxable;
        ChkPriceIncludesTax.IsChecked = m.PriceIncludesTax;
        ChkHasSerial.IsChecked = m.HasSerial;
        ChkHasExpiry.IsChecked = m.HasExpiry;
        ChkHasBatch.IsChecked = m.HasBatch;
        TxtShelfLifeDays.Text = m.ShelfLifeDays.ToString(CultureInfo.InvariantCulture);

        _units.Clear();

        var restoredUnits = false;
        if (!string.IsNullOrWhiteSpace(m.UnitsJson))
        {
            try
            {
                var savedUnits = JsonSerializer.Deserialize<List<MatUnitRow>>(m.UnitsJson);
                if (savedUnits is { Count: > 0 })
                {
                    foreach (var unit in savedUnits)
                        _units.Add(unit);
                    RenumberUnits();
                    restoredUnits = true;
                }
            }
            catch (JsonException)
            {
                // Unrecognized payloads fall back to the legacy unit columns below.
            }
        }

        decimal? N(decimal v) => v == 0 ? (decimal?)null : v;

        void AddUnit(string? name, decimal factor, string? barcode, bool first)
        {
            if (string.IsNullOrWhiteSpace(name) && !first) return;

            _units.Add(new MatUnitRow
            {
                LineNo = _units.Count + 1,
                UnitName = name ?? "",
                Factor = factor <= 0 ? (decimal?)null : factor,
                Barcode = barcode ?? "",
                PriceSale        = first ? N(m.PriceList)        : null,
                PricePurchase    = first ? N(m.Cost)             : null,
                PriceRetail      = first ? N(m.PriceRetail)      : null,
                PriceWholesale   = first ? N(m.PriceWholesale)   : null,
                PriceSpecial     = first ? N(m.PriceSpecial)     : null,
                PriceExport      = first ? N(m.PriceExport)      : null,
                PriceDistributor = first ? N(m.PriceDistributor) : null
            });
        }

        if (!restoredUnits)
        {
            AddUnit(m.Unit1, m.Unit1Factor, m.Unit1Barcode, true);
            AddUnit(m.Unit2, m.Unit2Factor, m.Unit2Barcode, false);
            AddUnit(m.Unit3, m.Unit3Factor, m.Unit3Barcode, false);
            AddUnit(m.Unit4, m.Unit4Factor, m.Unit4Barcode, false);
            AddUnit(m.Unit5, m.Unit5Factor, m.Unit5Barcode, false);
        }
        EnsureUnitRows(UnitWorkspaceRows);

        TxtQtyOnHand.Text = m.QtyOnHand.ToString(CultureInfo.InvariantCulture);
        TxtReorderQty.Text = m.ReorderQty.ToString(CultureInfo.InvariantCulture);
        TxtSafetyStock.Text = m.SafetyStock.ToString(CultureInfo.InvariantCulture);
        SetCombo(CmbDefaultWarehouse, m.DefaultWarehouse);
        TxtShelfBin.Text = m.ShelfBin ?? "";
        TxtLeadTimeDays.Text = m.LeadTimeDays.ToString(CultureInfo.InvariantCulture);
        TxtWeightNet.Text = m.WeightNet.ToString(CultureInfo.InvariantCulture);
        TxtWeightGross.Text = m.WeightGross.ToString(CultureInfo.InvariantCulture);
        ChkAllowNegative.IsChecked = m.AllowNegative;
        ChkTrackLocation.IsChecked = m.TrackLocation;
        ChkQualityCheck.IsChecked = m.QualityCheckRequired;

        TxtAccInventory.Text = m.AccInventory ?? "";
        TxtAccSales.Text = m.AccSales ?? "";
        TxtAccSalesReturn.Text = m.AccSalesReturn ?? "";
        TxtAccCogs.Text = m.AccCogs ?? "";
        TxtAccPurchase.Text = m.AccPurchase ?? "";
        TxtAccPurchaseReturn.Text = m.AccPurchaseReturn ?? "";
        TxtAccExpense.Text = m.AccExpense ?? "";
        TxtAccWip.Text = m.AccWip ?? "";
        ChkAllowSale.IsChecked = m.AllowSale;
        ChkAllowPurchase.IsChecked = m.AllowPurchase;
        ChkAllowReturn.IsChecked = m.AllowReturn;
        ChkSalesBlocked.IsChecked = m.SalesBlocked;

        TxtImagePath.Text = m.ImagePath ?? "";
        LoadImage(m.ImagePath);

        _attrs.Clear();

        if (m.Attributes != null)
        {
            foreach (var attribute in m.Attributes)
                _attrs.Add(attribute);
        }
    }

    private MatRepo.MatRec Collect()
    {
        var m = _loaded != null ? CloneSparse(_loaded) : new MatRepo.MatRec();

        m.Id = _currentId;

        m.Code = S(TxtCode.Text);
        m.NameAr = S(TxtName.Text);
        m.NameEn = S(TxtNameEn.Text);
        m.GroupCode = GetComboKey(CmbGroup);

        m.Status = ChkInactive.IsChecked == true
            ? "Inactive"
            : GetComboKey(CmbStatus) ?? "Active";

        m.ItemType = GetComboKey(CmbItemType) ?? "Stock";

        m.Category = SN(GetComboKey(CmbCategory));
        m.Brand = SN(TxtBrand.Text);
        m.Model = SN(TxtModel.Text);

        var colorKey = GetComboKey(CmbColor);
        m.Color = string.Equals(colorKey, "None", StringComparison.OrdinalIgnoreCase)
            ? null
            : SN(colorKey);

        m.SizeSpec = SN(TxtSizeSpec.Text);
        m.Manufacturer = SN(TxtManufacturer.Text);
        m.OriginCountry = SN(TxtOriginCountry.Text);
        m.Source = SN(TxtSource.Text);
        m.AltCode = SN(TxtAltCode.Text);
        m.Gtin = SN(TxtGtin.Text);
        m.SupplierCode = SN(TxtSupplierCode.Text);
        m.AbcClass = SN(GetComboKey(CmbAbcClass));
        m.Notes = S(TxtNotes.Text);

        m.IsStocked = ChkIsStocked.IsChecked == true;
        m.IsPurchased = ChkIsPurchased.IsChecked == true;
        m.IsSold = ChkIsSold.IsChecked == true;
        m.IsProduced = ChkIsProduced.IsChecked == true;
        m.IsService = ChkIsService.IsChecked == true;

        m.MinQty = D(TxtMinQty.Text);
        m.MaxQty = D(TxtMaxQty.Text);
        m.PriceCurrency = S(GetComboKey(CmbPriceCurrency));
        m.Cost = D(TxtCost.Text);
        m.CostMethod = GetComboKey(CmbCostMethod) ?? "WeightedAvg";
        m.TaxRate = D(TxtTaxRate.Text);
        m.IsTaxable = ChkIsTaxable.IsChecked == true;
        m.PriceIncludesTax = ChkPriceIncludesTax.IsChecked == true;
        m.HasSerial = ChkHasSerial.IsChecked == true;
        m.HasExpiry = ChkHasExpiry.IsChecked == true;
        m.HasBatch = ChkHasBatch.IsChecked == true;
        m.ShelfLifeDays = I(TxtShelfLifeDays.Text);

        // Persist every populated row. The legacy Unit1..Unit5 fields are kept only
        // for backward compatibility; UnitsJson is the authoritative unlimited list.
        var active = _units.Where(u => u.HasContent).ToList();
        for (var i = 0; i < active.Count; i++) active[i].LineNo = i + 1;
        if (active.Count == 0) active.Add(new MatUnitRow { LineNo = 1 });

        MatUnitRow? U(int index) => index < active.Count ? active[index] : null;

        var u1 = U(0); var u2 = U(1); var u3 = U(2); var u4 = U(3); var u5 = U(4);

        decimal Z(decimal? v) => v ?? 0m;

        m.Unit1 = u1?.UnitName; m.Unit1Factor = Z(u1?.Factor); m.Unit1Barcode = SN(u1?.Barcode);
        m.Unit2 = u2?.UnitName; m.Unit2Factor = Z(u2?.Factor); m.Unit2Barcode = SN(u2?.Barcode);
        m.Unit3 = u3?.UnitName; m.Unit3Factor = Z(u3?.Factor); m.Unit3Barcode = SN(u3?.Barcode);
        m.Unit4 = u4?.UnitName; m.Unit4Factor = Z(u4?.Factor); m.Unit4Barcode = SN(u4?.Barcode);
        m.Unit5 = u5?.UnitName; m.Unit5Factor = Z(u5?.Factor); m.Unit5Barcode = SN(u5?.Barcode);

        m.DefaultUnit = m.Unit1;

        m.PriceList        = Z(u1?.PriceSale);
        m.PriceRetail      = Z(u1?.PriceRetail);
        m.PriceWholesale   = Z(u1?.PriceWholesale);
        m.PriceSpecial     = Z(u1?.PriceSpecial);
        m.PriceExport      = Z(u1?.PriceExport);
        m.PriceDistributor = Z(u1?.PriceDistributor);
        m.PriceMin = 0;
        m.UnitsJson = JsonSerializer.Serialize(active);

        if (u1?.PricePurchase is not null) m.Cost = u1.PricePurchase.Value;

        m.ReorderQty = D(TxtReorderQty.Text);
        m.SafetyStock = D(TxtSafetyStock.Text);
        m.DefaultWarehouse = SN(GetComboKey(CmbDefaultWarehouse));
        m.ShelfBin = SN(TxtShelfBin.Text);
        m.LeadTimeDays = I(TxtLeadTimeDays.Text);
        m.WeightNet = D(TxtWeightNet.Text);
        m.WeightGross = D(TxtWeightGross.Text);
        m.AllowNegative = ChkAllowNegative.IsChecked == true;
        m.TrackLocation = ChkTrackLocation.IsChecked == true;
        m.QualityCheckRequired = ChkQualityCheck.IsChecked == true;

        m.AccInventory = SN(TxtAccInventory.Text);
        m.AccSales = SN(TxtAccSales.Text);
        m.AccSalesReturn = SN(TxtAccSalesReturn.Text);
        m.AccCogs = SN(TxtAccCogs.Text);
        m.AccPurchase = SN(TxtAccPurchase.Text);
        m.AccPurchaseReturn = SN(TxtAccPurchaseReturn.Text);
        m.AccExpense = SN(TxtAccExpense.Text);
        m.AccWip = SN(TxtAccWip.Text);
        m.AllowSale = ChkAllowSale.IsChecked == true;
        m.AllowPurchase = ChkAllowPurchase.IsChecked == true;
        m.AllowReturn = ChkAllowReturn.IsChecked == true;
        m.SalesBlocked = ChkSalesBlocked.IsChecked == true;

        m.ImagePath = SN(TxtImagePath.Text);

        m.Attributes = _attrs.Where(a => !string.IsNullOrWhiteSpace(a.Key)).ToList();

        return m;
    }

    private static MatRepo.MatRec CloneSparse(MatRepo.MatRec src)
    {
        return new MatRepo.MatRec
        {
            Id = src.Id,
            NamePrintAr = src.NamePrintAr, NamePrintEn = src.NamePrintEn,
            ShortName = src.ShortName, SearchName = src.SearchName,
            OldCode = src.OldCode, CustomerCode = src.CustomerCode,
            ManufacturerCode = src.ManufacturerCode, HsCode = src.HsCode,
            SkuExternal = src.SkuExternal, PosName = src.PosName,
            SubCategory = src.SubCategory, Grade = src.Grade,
            Class1 = src.Class1, Class2 = src.Class2, Class3 = src.Class3,
            Class4 = src.Class4, Class5 = src.Class5,
            ProductLine = src.ProductLine,
            Season = src.Season, LifecycleStatus = src.LifecycleStatus,
            MakeOrBuy = src.MakeOrBuy, StockStage = src.StockStage,
            WeightUnit = src.WeightUnit, Volume = src.Volume, VolumeUnit = src.VolumeUnit,
            Length = src.Length, Width = src.Width, Height = src.Height, DimUnit = src.DimUnit,
            Area = src.Area, AreaUnit = src.AreaUnit,
            PackQty = src.PackQty, UnitsPerParcel = src.UnitsPerParcel,
            BrandOwner = src.BrandOwner, WarrantyMonths = src.WarrantyMonths,
            CatalogueRef = src.CatalogueRef, WebUrl = src.WebUrl, Tags = src.Tags,
            PurchaseUnit = src.PurchaseUnit, SalesUnit = src.SalesUnit,
            StandardCost = src.StandardCost, LastPurchaseCost = src.LastPurchaseCost,
            AvgCost = src.AvgCost, CostCurrency = src.CostCurrency,
            OverheadPct = src.OverheadPct, LandedCost = src.LandedCost,
            LastPurchaseDate = src.LastPurchaseDate,
            PriceOnline = src.PriceOnline, MarginTargetPct = src.MarginTargetPct,
            DiscountMaxPct = src.DiscountMaxPct, AllowInvoiceDisc = src.AllowInvoiceDisc,
            LotSize = src.LotSize, OrderMultiple = src.OrderMultiple,
            EconomicOrderQty = src.EconomicOrderQty,
            ReplenishmentMethod = src.ReplenishmentMethod,
            AlertBeforeExpiryDays = src.AlertBeforeExpiryDays, HasLot = src.HasLot,
            TaxCode = src.TaxCode, TaxAccount = src.TaxAccount, TaxType = src.TaxType,
            IsZeroRated = src.IsZeroRated, CommissionPct = src.CommissionPct,
            BonusQty = src.BonusQty, BonusItemCode = src.BonusItemCode,
            MinSaleQty = src.MinSaleQty, MaxSaleQty = src.MaxSaleQty,
            ImagePath2 = src.ImagePath2, AttachmentPath = src.AttachmentPath,
            Description = src.Description,
            InternalNotes = src.InternalNotes, SpecSheet = src.SpecSheet,
            LabelText = src.LabelText, UnitsJson = src.UnitsJson,
            PricesJson = src.PricesJson, AttributesJson = src.AttributesJson,
            ExtraBarcodes = src.ExtraBarcodes?.ToList() ?? new(),
            PriceLines = src.PriceLines?.ToList() ?? new(),
            Suppliers = src.Suppliers?.ToList() ?? new()
        };
    }

    private static string? GetComboKey(ComboBox? combo)
    {
        if (combo == null) return null;
        if (combo.SelectedItem is MatComboOption option) return option.Key;
        if (combo.SelectedItem is MatCategoryOption categoryOption) return categoryOption.Key;
        if (combo.SelectedItem != null)
        {
            var selected = combo.SelectedItem.ToString();
            return string.IsNullOrWhiteSpace(selected) ? null : ExtractCode(selected);
        }
        return string.IsNullOrWhiteSpace(combo.Text) ? null : ExtractCode(combo.Text);
    }

    private static void SetCombo(ComboBox? combo, string? value)
    {
        if (combo == null) return;

        if (string.IsNullOrWhiteSpace(value)) { combo.SelectedIndex = -1; combo.Text = ""; return; }

        var key = value.Trim();
        var match = combo.Items.Cast<object>().FirstOrDefault(item =>
        {
            if (item is MatComboOption option)
                return string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase);
            return string.Equals(item?.ToString(), key, StringComparison.OrdinalIgnoreCase);
        });

        if (match != null) { combo.SelectedItem = match; return; }
        combo.SelectedIndex = -1;
        combo.Text = key;
    }

    private static string? ExtractCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        var index = value.IndexOf(" -", StringComparison.Ordinal);
        return index > 0 ? value[..index].Trim() : value;
    }

    // ================================================================
    //  Edit mode
    // ================================================================
    private void SetEditMode(bool editing)
    {
        _isEditing = editing;
        var readOnly = !editing;

        foreach (var tb in FindVisualChildren<TextBox>(this))
        {
            if (tb.Name == "TxtQtyOnHand") { tb.IsReadOnly = true; continue; }
            if (tb.Name == "TxtCode" && !_isNew) { tb.IsReadOnly = true; continue; }
            if (tb.Name == "TxtGoto") { tb.IsReadOnly = false; continue; }
            tb.IsReadOnly = readOnly;
        }

        foreach (var cb in FindVisualChildren<ComboBox>(this))
            cb.IsEnabled = editing;

        foreach (var ck in FindVisualChildren<CheckBox>(this)) ck.IsEnabled = editing;

        DgUnits.IsReadOnly = readOnly;
        DgAttributes.IsReadOnly = readOnly;

        BtnSave.IsEnabled = editing;
        BtnUnitAdd.IsEnabled = editing;
        BtnUnitRemove.IsEnabled = editing;
        BtnCancel.IsEnabled = editing;
        BtnEdit.IsEnabled = !editing && _currentId != 0;
        BtnDelete.IsEnabled = !editing && _currentId != 0;
        BtnNew.IsEnabled = !editing;
    }

    private void AddUnit_Click(object sender, RoutedEventArgs e)
    {
        if (!_isEditing) return;

        DgUnits.CommitEdit(DataGridEditingUnit.Cell, true);
        DgUnits.CommitEdit(DataGridEditingUnit.Row, true);
        var row = new MatUnitRow { LineNo = _units.Count + 1 };
        _units.Add(row);
        DgUnits.SelectedItem = row;
        DgUnits.ScrollIntoView(row);
        if (DgUnits.Columns.Count > 1)
        {
            DgUnits.CurrentCell = new DataGridCellInfo(row, DgUnits.Columns[1]);
            DgUnits.BeginEdit();
        }
    }

    private void RemoveUnit_Click(object sender, RoutedEventArgs e)
    {
        if (!_isEditing || DgUnits.SelectedItem is not MatUnitRow selected) return;

        DgUnits.CommitEdit(DataGridEditingUnit.Cell, true);
        DgUnits.CommitEdit(DataGridEditingUnit.Row, true);
        _units.Remove(selected);
        EnsureUnitRows(UnitWorkspaceRows);
        RenumberUnits();
    }

    // ================================================================
    //  Commands
    // ================================================================
    private void New_Click(object sender, RoutedEventArgs e)
    {
        _isNew = true;
        _currentId = 0;
        _loaded = null;

        ClearForm();

        try { TxtCode.Text = ToWesternDigits(MatRepo.NextCode()); }
        catch { TxtCode.Text = "0001"; }

        TxtCode.IsReadOnly = false;

        SetEditMode(true);
        ShowSection(0);
        TxtName.Focus();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_currentId == 0) return;
        SetEditMode(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_isNew) { ClearForm(); SetEditMode(false); }
        else LoadCurrent();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_isEditing) return;

        if (string.IsNullOrWhiteSpace(TxtCode.Text) ||
            string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show(this,
                L("MatCard.ErrRequired", "Code and name are required."),
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            DgUnits.CommitEdit(DataGridEditingUnit.Cell, true);
            DgUnits.CommitEdit(DataGridEditingUnit.Row, true);
            DgAttributes.CommitEdit(DataGridEditingUnit.Cell, true);
            DgAttributes.CommitEdit(DataGridEditingUnit.Row, true);

            var record = Collect();
            var saved = MatRepo.Save(record, _isNew);

            _isNew = false;
            _currentId = saved.Id;

            ReloadCache();
            _index = Math.Max(0, _cache.FindIndex(x => x.Id == saved.Id));
            LoadCurrent();

            MessageBox.Show(this,
                L("MatCard.Saved", "Saved successfully."),
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExcelExport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DgUnits.CommitEdit(DataGridEditingUnit.Cell, true);
            DgUnits.CommitEdit(DataGridEditingUnit.Row, true);
            DgAttributes.CommitEdit(DataGridEditingUnit.Cell, true);
            DgAttributes.CommitEdit(DataGridEditingUnit.Row, true);
            var record = Collect();
            var dialog = new SaveFileDialog { Title = "Export Material Card", Filter = "Excel Workbook (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", AddExtension = true, FileName = string.IsNullOrWhiteSpace(record.Code) ? "Material.xlsx" : record.Code + ".xlsx" };
            if (dialog.ShowDialog(this) != true) return;
            MaterialExcelExchange.Export(dialog.FileName, record);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExcelImport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Title = "Import Material Card", Filter = "Excel Workbook (*.xlsx)|*.xlsx", Multiselect = false };
            if (dialog.ShowDialog(this) != true) return;
            var imported = MaterialExcelExchange.Import(dialog.FileName);
            if (imported == null) { MessageBox.Show(this, "The workbook does not contain a valid ShouTech Material Card map.", Title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var existing = imported.Id > 0 ? MatRepo.LoadById(imported.Id, true) : null;
            if (existing == null && !string.IsNullOrWhiteSpace(imported.Code)) existing = MatRepo.LoadAll().FirstOrDefault(x => string.Equals(x.Code, imported.Code, StringComparison.OrdinalIgnoreCase));
            imported.Id = existing?.Id ?? 0;
            _currentId = imported.Id;
            _isNew = existing == null;
            _loaded = imported;
            _isEditing = true;
            LoadCurrent();
            MessageBox.Show(this, _isNew ? "Material imported into the form. Review it and click Save to create the material." : "Material imported into the form. Review the changes and click Save to update the material.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_currentId == 0) return;

        if (MessageBox.Show(this,
                L("MatCard.ConfirmDelete", "Delete or disable this material?"),
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            MatRepo.SoftDelete(_currentId);
            ReloadCache();

            if (_cache.Count == 0) { ClearForm(); SetEditMode(false); }
            else { _index = Math.Min(_index, _cache.Count - 1); LoadCurrent(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NavFirst_Click(object sender, RoutedEventArgs e)
    {
        if (_cache.Count == 0) return;
        _index = 0;
        LoadCurrent();
    }

    private void NavPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0) { _index--; LoadCurrent(); }
    }

    private void NavNext_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _cache.Count - 1) { _index++; LoadCurrent(); }
    }

    private void NavLast_Click(object sender, RoutedEventArgs e)
    {
        if (_cache.Count == 0) return;
        _index = _cache.Count - 1;
        LoadCurrent();
    }

    // ================================================================
    //  Quick navigation (search)
    // ================================================================
    private void Goto_Click(object sender, RoutedEventArgs e)
    {
        NavigateToSearchQuery();
    }

    private void TxtGoto_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            NavigateToSearchQuery();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            TxtGoto.Clear();
        }
    }

    private void NavigateToSearchQuery()
    {
        var query = TxtGoto.Text?.Trim() ?? "";

        if (_cache.Count == 0)
        {
            MessageBox.Show(this,
                L("MatCard.NoMaterials", "No materials loaded."),
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var items = _cache.Select(m =>
        {
            string code     = m.Code         ?? "";
            string name     = m.Name         ?? "";
            string nameEn   = m.NameEn       ?? "";
            string group    = m.GroupCode    ?? "";
            string gtin     = m.Gtin         ?? "";
            string altCode  = m.AltCode      ?? "";
            string supplier = m.SupplierCode ?? "";

            return new SerWinItem
            {
                Payload    = m,
                Id         = m.Id,
                SearchText = string.Join(" ", code, name, nameEn, group, gtin, altCode, supplier),
                Values = new Dictionary<string, object?>
                {
                    ["Code"]   = m.Code,
                    ["Name"]   = m.Name,
                    ["NameEn"] = m.NameEn,
                    ["Group"]  = m.GroupCode,
                    ["Status"] = m.Status
                }
            };
        }).ToList();

        var cols = new List<SerWinColumn>
        {
            new() { Key = "Code",   Header = L("MatCard.Code",        "Code"),   Width = 90 },
            new() { Key = "Name",   Header = L("MatCard.Name",        "Name"),   Fill = true },
            new() { Key = "NameEn", Header = L("MatCard.LatinName",   "Latin"),  Width = 180 },
            new() { Key = "Group",  Header = L("MatCard.Group",       "Group"),  Width = 90 },
            new() { Key = "Status", Header = L("MatCard.StatusLabel", "Status"), Width = 80 }
        };

        var picked = SerWin.Pick(
            owner: this,
            title: L("Inventory.SearchMaterial", "Search Material"),
            items: items,
            columns: cols,
            initialQuery: query);

        if (picked?.Payload is not MatRepo.MatRec mat) return;

        var idx = _cache.FindIndex(x => x.Id == mat.Id);

        if (idx < 0)
        {
            ReloadCache();
            idx = _cache.FindIndex(x => x.Id == mat.Id);
        }

        if (idx >= 0)
        {
            _index = idx;
            LoadCurrent();
            TxtCode.Focus();
        }
    }

    // ================================================================
    //  Color VisuId
    // ================================================================
    private void Color_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The selected color is persisted with the material record on save
        // (see Collect); no live UI side effects are required here.
    }

    // ================================================================
    //  Status bar (company + user)
    // ================================================================
    private void UpdateStatusBar()
    {
        try
        {
            var company = GetCurrentCompanyName();
            var user = GetCurrentUsername();

            if (TxtStatusCompany != null)
                TxtStatusCompany.Text = string.IsNullOrWhiteSpace(company) ? "—" : company;

            if (TxtStatusUser != null)
                TxtStatusUser.Text = string.IsNullOrWhiteSpace(user) ? "—" : user;
        }
        catch { }
    }

    private static string GetCurrentCompanyName()
    {
        try
        {
            var t = typeof(AppState);
            foreach (var name in new[] { "CurrentCompanyName", "CompanyName" })
            {
                var p = t.GetProperty(name,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static);
                if (p != null)
                {
                    var v = p.GetValue(null)?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v!;
                }
            }
        }
        catch { }
        return AppState.CurrentCompanyCode ?? "";
    }

    private static string GetCurrentUsername()
    {
        try
        {
            var t = typeof(AppState);
            foreach (var name in new[]
            {
                "CurrentUsername", "CurrentUser", "Username",
                "UserName", "LoggedUser", "CurrentUserName"
            })
            {
                var p = t.GetProperty(name,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static);
                if (p != null)
                {
                    var v = p.GetValue(null)?.ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v!;
                }
            }
        }
        catch { }
        return "";
    }

    // ================================================================
    //  Image
    // ================================================================
    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            TxtImagePath.Text = dialog.FileName;
            LoadImage(dialog.FileName);
        }
    }

    private void LoadImage(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ImgPreview.Source = null;
                return;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();

            ImgPreview.Source = bitmap;
        }
        catch
        {
            ImgPreview.Source = null;
        }
    }

    // ================================================================
    //  Print barcode
    // ================================================================
    private void PrintBarcode_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditing)
        {
            DgUnits.CommitEdit(DataGridEditingUnit.Cell, true);
            DgUnits.CommitEdit(DataGridEditingUnit.Row, true);
        }

        var code = _units.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.Barcode))?.Barcode;

        if (string.IsNullOrWhiteSpace(code)) code = TxtGtin.Text;
        if (string.IsNullOrWhiteSpace(code)) code = TxtCode.Text;
        code = code?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show(this,
                L("MatCard.NoBarcode", "No barcode or code available to print."),
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = !string.IsNullOrWhiteSpace(TxtName.Text)
            ? TxtName.Text.Trim()
            : TxtNameEn.Text?.Trim() ?? "";

        var price = _units.FirstOrDefault(u => u.PriceSale is > 0)?.PriceSale;
        var currency = GetComboKey(CmbPriceCurrency);

        var printed = ShouTech.App.Common.BarcodeLabelPrinter.Print(
            this,
            L("MatCard.BarcodePrint", "Print barcode") + " - " + code,
            name,
            code,
            ShouTech.App.Common.BarcodeLabelPrinter.FormatPrice(price, currency));

        if (printed)
        {
            MessageBox.Show(this,
                L("MatCard.BarcodePrinted", "The barcode label was sent to the printer."),
                Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ================================================================
    //  Close
    // ================================================================
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ================================================================
    //  Visual tree helpers
    // ================================================================
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) yield break;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T item) yield return item;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
    protected override void ApplyLocalizedText()
    {
        RefreshLocalizedTexts();
    }

}


internal static class MaterialExcelExchange
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace CT = "http://schemas.openxmlformats.org/package/2006/content-types";
    private sealed record Map(string Key, string Sheet, string Cell, string Type, string Property);
    private sealed record Schema(string DisplayName, string Key, string Sheet, string Address, string Type, string Property, bool Required, bool ReadOnly);

    public static void Export(string path, ShouTech.App.Services.MatRepo.MatRec record)
    {
        var fields = typeof(ShouTech.App.Services.MatRepo.MatRec).GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanRead && IsScalar(p.PropertyType)).ToList();
        var maps = fields.Select((p, i) => new Map($"Material.{p.Name}", "Material", $"B{i + 3}", TypeName(p.PropertyType), p.Name)).ToList();
        var unitColumns = new[] { "LineNo", "UnitName", "Factor", "PriceSale", "PricePurchase", "PriceWholesale", "PriceRetail", "PriceSpecial", "PriceExport", "PriceDistributor", "Barcode" };
        maps.AddRange(unitColumns.Select((x, i) => new Map($"Units.{x}", "Units", Column(i + 1) + "1", "Column", x)));
        var attrProps = typeof(MatAttrRow).GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanRead && IsScalar(p.PropertyType)).ToArray();
        maps.AddRange(attrProps.Select((p, i) => new Map($"Attributes.{p.Name}", "Attributes", Column(i + 1) + "1", "Column", p.Name)));
        var unitRows = string.IsNullOrWhiteSpace(record.UnitsJson) ? new List<MatUnitRow>() : JsonSerializer.Deserialize<List<MatUnitRow>>(record.UnitsJson) ?? new();
        var schema = BuildSchema(fields, unitColumns, attrProps, maps);
        var sheets = new[] { ("Material", MaterialSheet(record, fields)), ("Units", UnitsSheet(unitRows, unitColumns)), ("Attributes", AttributesSheet(record.Attributes ?? new(), attrProps)), ("__Schema", SchemaSheet(schema)), ("__Map", MapSheet(maps)) };
        using var fs = File.Create(path); using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Put(zip, "[Content_Types].xml", ContentTypes(sheets.Length)); Put(zip, "_rels/.rels", RootRels()); Put(zip, "xl/workbook.xml", Workbook(sheets.Select((x, i) => (x.Item1, i + 1)))); Put(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(sheets.Length));
        for (var i = 0; i < sheets.Length; i++) Put(zip, $"xl/worksheets/sheet{i + 1}.xml", sheets[i].Item2);
    }

    public static ShouTech.App.Services.MatRepo.MatRec? Import(string path)
    {
        using var zip = ZipFile.OpenRead(path); var wb = XDocument.Parse(Read(zip, "xl/workbook.xml")); var rel = XDocument.Parse(Read(zip, "xl/_rels/workbook.xml.rels"));
        var rels = rel.Root?.Elements(PR + "Relationship").ToDictionary(x => (string)x.Attribute("Id")!, x => (string)x.Attribute("Target")!) ?? new();
        var sheets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in wb.Descendants(Main + "sheet")) { var n = (string?)x.Attribute("name"); var id = (string?)x.Attribute(R + "id"); if (n == null || id == null || !rels.TryGetValue(id, out var target)) continue; sheets[n] = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target.TrimStart('/'); }
        var maps = new List<Map>();
        if (sheets.TryGetValue("__Schema", out var schemaPath))
        {
            var schemaRows = Rows(zip, schemaPath);
            if (schemaRows.Count >= 2)
            {
                var h = schemaRows[0];
                int I(string n) => h.FindIndex(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
                var ik = I("FieldKey"); var isheet = I("Sheet"); var iaddr = I("Address"); var itype = I("DataType"); var iprop = I("PropertyName");
                if (ik >= 0 && isheet >= 0 && iaddr >= 0 && iprop >= 0)
                    maps = schemaRows.Skip(1)
                        .Where(r => r.Count > Math.Max(iprop, Math.Max(iaddr, Math.Max(isheet, ik))))
                        .Select(r => new Map(r[ik], r[isheet], r[iaddr], itype >= 0 ? r[itype] : "", r[iprop]))
                        .ToList();
            }
        }
        if (maps.Count == 0 && sheets.TryGetValue("__Map", out var mapPath))
        {
            var mapRows = Rows(zip, mapPath);
            if (mapRows.Count < 2) return null;
            var h = mapRows[0]; int I(string n) => h.FindIndex(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
            var ik=I("FieldKey"); var isheet=I("Sheet"); var icell=I("Cell"); var itype=I("Type"); var iprop=I("PropertyName"); if (ik<0||isheet<0||icell<0||iprop<0)return null;
            maps=mapRows.Skip(1).Where(r=>r.Count>Math.Max(iprop,Math.Max(icell,Math.Max(isheet,ik)))).Select(r=>new Map(r[ik],r[isheet],r[icell],itype>=0?r[itype]:"",r[iprop])).ToList();
        }
        if(!sheets.TryGetValue("Material",out var materialPath))return null; var cells=Cells(zip,materialPath); var rec=new ShouTech.App.Services.MatRepo.MatRec();
        foreach(var m in maps.Where(x=>x.Sheet=="Material"&&cells.ContainsKey(x.Cell))){var p=typeof(ShouTech.App.Services.MatRepo.MatRec).GetProperty(m.Property,BindingFlags.Instance|BindingFlags.Public|BindingFlags.IgnoreCase);if(p!=null&&p.CanWrite&&Parse(cells[m.Cell],p.PropertyType,out var v))p.SetValue(rec,v);}
        if(sheets.TryGetValue("Units",out var up)){var rows=Rows(zip,up);var list=new List<MatUnitRow>();if(rows.Count>1)foreach(var r in rows.Skip(1)){var u=new MatUnitRow();SetProps(u,rows[0],r);if(u.HasContent){u.LineNo=list.Count+1;list.Add(u);}}rec.UnitsJson=JsonSerializer.Serialize(list);}
        if(sheets.TryGetValue("Attributes",out var ap)){var rows=Rows(zip,ap);rec.Attributes=new();if(rows.Count>1)foreach(var r in rows.Skip(1)){var a=new MatAttrRow();SetProps(a,rows[0],r);rec.Attributes.Add(a);}rec.AttributesJson=JsonSerializer.Serialize(rec.Attributes);}
        return rec;
    }

    private static void SetProps(object o,List<string> headers,List<string> values){for(var i=0;i<headers.Count&&i<values.Count;i++){var p=o.GetType().GetProperty(headers[i],BindingFlags.Instance|BindingFlags.Public|BindingFlags.IgnoreCase);if(p!=null&&p.CanWrite&&Parse(values[i],p.PropertyType,out var v))p.SetValue(o,v);}}
    private static bool IsScalar(Type t){t=Nullable.GetUnderlyingType(t)??t;return t.IsPrimitive||t.IsEnum||t==typeof(string)||t==typeof(decimal)||t==typeof(DateTime)||t==typeof(Guid);}
    private static string TypeName(Type t){t=Nullable.GetUnderlyingType(t)??t;return t==typeof(decimal)||t==typeof(double)||t==typeof(float)||t==typeof(int)||t==typeof(long)?"Number":t==typeof(bool)?"Boolean":t==typeof(DateTime)?"DateTime":"Text";}
    private static string F(object v)=>v switch{decimal d=>d.ToString(CultureInfo.InvariantCulture),double d=>d.ToString(CultureInfo.InvariantCulture),float f=>f.ToString(CultureInfo.InvariantCulture),bool b=>b?"1":"0",DateTime d=>d.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture),_=>Convert.ToString(v,CultureInfo.InvariantCulture)??""};
    private static bool Parse(string s,Type target,out object? value){var n=Nullable.GetUnderlyingType(target);var t=n??target;s=s.Trim();if(s==""){value=n!=null||!t.IsValueType?null:Activator.CreateInstance(t);return true;}if(t==typeof(string)){value=s;return true;}if(t==typeof(decimal)&&decimal.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var d)){value=d;return true;}if(t==typeof(double)&&double.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var db)){value=db;return true;}if(t==typeof(float)&&float.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var f)){value=f;return true;}if(t==typeof(int)&&int.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var i)){value=i;return true;}if(t==typeof(long)&&long.TryParse(s,NumberStyles.Any,CultureInfo.InvariantCulture,out var l)){value=l;return true;}if(t==typeof(bool)){value=s is "1" or "true" or "True" or "yes" or "Yes";return true;}if(t==typeof(DateTime)&&DateTime.TryParse(s,CultureInfo.InvariantCulture,DateTimeStyles.None,out var dt)){value=dt;return true;}if(t==typeof(Guid)&&Guid.TryParse(s,out var g)){value=g;return true;}if(t.IsEnum&&Enum.TryParse(t,s,true,out var e)){value=e;return true;}value=null;return false;}
    private static string MaterialSheet(ShouTech.App.Services.MatRepo.MatRec r,List<PropertyInfo> p){var rows=new List<List<string>>{new(){"ShouTech Material Card",DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)},new(){"FieldKey","Value","Type"}};for(var i=0;i<p.Count;i++)rows.Add(new(){$"Material.{p[i].Name}",F(p[i].GetValue(r)??""),TypeName(p[i].PropertyType)});return Sheet(rows);}
    private static string UnitsSheet(List<MatUnitRow> rows,string[] cols){var d=new List<List<string>>{cols.ToList()};foreach(var u in rows)d.Add(cols.Select(c=>F(typeof(MatUnitRow).GetProperty(c)!.GetValue(u)??"")).ToList());return Sheet(d);}
    private static string AttributesSheet(List<MatAttrRow> rows,PropertyInfo[] p){var d=new List<List<string>>{p.Select(x=>x.Name).ToList()};foreach(var r in rows)d.Add(p.Select(x=>F(x.GetValue(r)??"")).ToList());return Sheet(d);}
    private static List<Schema> BuildSchema(List<PropertyInfo> fields, string[] unitColumns, PropertyInfo[] attrProps, List<Map> maps)
    {
        var result = new List<Schema>();
        foreach (var m in maps)
        {
            var display = m.Property;
            var required = m.Key.EndsWith(".Code", StringComparison.OrdinalIgnoreCase) || m.Key.EndsWith(".Name", StringComparison.OrdinalIgnoreCase);
            result.Add(new Schema(display, m.Key, m.Sheet, m.Cell, m.Type == "Column" ? "Text" : m.Type, m.Property, required, false));
        }
        return result;
    }
    private static string SchemaSheet(List<Schema> schema)
    {
        var rows = new List<List<string>>
        {
            new() { "SchemaVersion", "1.0" },
            new() { "DisplayName", "FieldKey", "Sheet", "Address", "DataType", "PropertyName", "Required", "ReadOnly" }
        };
        rows.AddRange(schema.Select(x => new List<string>
        {
            x.DisplayName, x.Key, x.Sheet, x.Address, x.Type, x.Property,
            x.Required ? "1" : "0", x.ReadOnly ? "1" : "0"
        }));
        return Sheet(rows);
    }
    private static string MapSheet(List<Map> maps){var d=new List<List<string>>{new(){"FieldKey","Sheet","Cell","Type","PropertyName"}};d.AddRange(maps.Select(m=>new List<string>{m.Key,m.Sheet,m.Cell,m.Type,m.Property}));return Sheet(d);}
    private static string Sheet(List<List<string>> rows){var sd=new XElement(Main+"sheetData",rows.Select((r,ri)=>new XElement(Main+"row",new XAttribute("r",ri+1),r.Select((v,ci)=>new XElement(Main+"c",new XAttribute("r",Cell(ci+1,ri+1)),new XAttribute("t","inlineStr"),new XElement(Main+"is",new XElement(Main+"t",v??"")))))));return new XElement(Main+"worksheet",new XAttribute(XNamespace.Xmlns+"r",R),sd).ToString(SaveOptions.DisableFormatting);}
    private static Dictionary<string,string> Cells(ZipArchive z,string path){var d=XDocument.Parse(Read(z,path));return d.Descendants(Main+"c").ToDictionary(x=>(string)x.Attribute("r")!,x=>(string?)x.Attribute("t")=="inlineStr"?string.Concat(x.Descendants(Main+"t").Select(t=>t.Value)):x.Element(Main+"v")?.Value??"",StringComparer.OrdinalIgnoreCase);}
    private static List<List<string>> Rows(ZipArchive z,string path){var c=Cells(z,path);var rr=new Dictionary<int,Dictionary<int,string>>();foreach(var x in c){var p=x.Key;var letters=new string(p.TakeWhile(char.IsLetter).ToArray());var digits=new string(p.SkipWhile(char.IsLetter).ToArray());var col=0;foreach(var ch in letters.ToUpperInvariant())col=col*26+ch-'A'+1;var row=int.Parse(digits, CultureInfo.InvariantCulture);if(!rr.ContainsKey(row))rr[row]=new();rr[row][col]=x.Value;}return rr.OrderBy(x=>x.Key).Select(x=>Enumerable.Range(1,x.Value.Keys.DefaultIfEmpty(1).Max()).Select(i=>x.Value.TryGetValue(i,out var v)?v:"").ToList()).ToList();}
    private static string Cell(int col,int row){var s="";while(col>0){col--;s=(char)('A'+col%26)+s;col/=26;}return s+row;}
    private static string Column(int col)=>Cell(col,1)[..^1];
    private static void Put(ZipArchive z,string name,string content){using var w=new StreamWriter(z.CreateEntry(name).Open(),new UTF8Encoding(false));w.Write(content);}
    private static string Read(ZipArchive z,string name){using var r=new StreamReader(z.GetEntry(name)!.Open(),Encoding.UTF8,true);return r.ReadToEnd();}
    private static string ContentTypes(int n){var x=new XElement(CT+"Types",new XElement(CT+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(CT+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),new XElement(CT+"Override",new XAttribute("PartName","/xl/workbook.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")));for(var i=1;i<=n;i++)x.Add(new XElement(CT+"Override",new XAttribute("PartName",$"/xl/worksheets/sheet{i}.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));return x.ToString(SaveOptions.DisableFormatting);}
    private static string RootRels()=>new XElement(PR+"Relationships",new XElement(PR+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type","http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),new XAttribute("Target","xl/workbook.xml"))).ToString(SaveOptions.DisableFormatting);
    private static string Workbook(IEnumerable<(string name,int id)> ss)=>new XElement(Main+"workbook",new XAttribute(XNamespace.Xmlns+"r",R),new XElement(Main+"sheets",ss.Select(s=>new XElement(Main+"sheet",new XAttribute("name",s.name),new XAttribute("sheetId",s.id),new XAttribute(R+"id",$"rId{s.id}"))))).ToString(SaveOptions.DisableFormatting);
    private static string WorkbookRels(int n){var x=new XElement(PR+"Relationships");for(var i=1;i<=n;i++)x.Add(new XElement(PR+"Relationship",new XAttribute("Id",$"rId{i}"),new XAttribute("Type","http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),new XAttribute("Target",$"worksheets/sheet{i}.xml")));return x.ToString(SaveOptions.DisableFormatting);}
}