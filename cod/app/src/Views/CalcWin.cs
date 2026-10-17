using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShouTech.App.Views;

/// <summary>
/// Phase 7 - modern ribbon calculator (Tools > Calculator).
/// Code-built, theme-aware, fully keyboard-driven, works offline.
/// </summary>
public sealed class CalcWin : BaseWin
{
    private readonly TextBox _display = new()
    {
        IsReadOnly = true,
        FontSize = 30,
        FontWeight = FontWeights.SemiBold,
        TextAlignment = TextAlignment.Right,
        VerticalContentAlignment = VerticalAlignment.Center,
        Padding = new Thickness(10, 6, 10, 6),
        BorderThickness = new Thickness(0)
    };

    private readonly ListBox _history = new()
    {
        FontSize = 13,
        BorderThickness = new Thickness(0)
    };

    private double _accumulator;
    private string _pendingOperator = string.Empty;
    private bool _freshEntry = true;
    private double _memory;

    private CalcWin()
    {
        Title = "Calculator";
        Width = 380;
        Height = 560;
        MinWidth = 320;
        MinHeight = 460;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        BuildUi();
    }

    protected override void ApplyLocalizedText()
    {
        Title = L("Tools.Calculator", "Calculator");
    }

    private static string L(string key, string fallback)
    {
        try
        {
            var s = LocSvc?.GetString(key);
            return string.IsNullOrWhiteSpace(s) || string.Equals(s, key, StringComparison.Ordinal)
                ? fallback
                : s;
        }
        catch
        {
            return fallback;
        }
    }

    private void BuildUi()
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(130) });

        var header = MakeHeader("🧮", "Tools.Calculator", "Calculator");
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var displayBorder = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 10, 0, 10),
            BorderThickness = new Thickness(1),
            Child = _display
        };
        displayBorder.SetResourceReference(Border.BackgroundProperty, "ShellContentBackgroundBrush");
        displayBorder.SetResourceReference(Border.BorderBrushProperty, "ShellContentBorderBrush");
        displayBorder.SetResourceReference(Border.ForegroundProperty, "TextBrush");
        _display.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
        Grid.SetRow(displayBorder, 1);
        root.Children.Add(displayBorder);

        var pad = BuildPad();
        Grid.SetRow(pad, 2);
        root.Children.Add(pad);

        var historyCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 10, 0, 0),
            BorderThickness = new Thickness(1),
            Child = _history
        };
        historyCard.SetResourceReference(Border.BackgroundProperty, "ShellContentBackgroundBrush");
        historyCard.SetResourceReference(Border.BorderBrushProperty, "ShellContentBorderBrush");
        historyCard.SetResourceReference(Border.ForegroundProperty, "TextBrush");
        _history.SetResourceReference(ListBox.ForegroundProperty, "TextBrush");
        Grid.SetRow(historyCard, 3);
        root.Children.Add(historyCard);

        Content = root;

        PreviewKeyDown += OnPreviewKeyDown;
    }

    private static Border MakeHeader(string glyph, string key, string fallback)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = 26,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        panel.Children.Add(icon);

        var title = new TextBlock
        {
            Text = L(key, fallback),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        panel.Children.Add(title);

        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(1),
            Child = panel
        };
        border.SetResourceReference(Border.BackgroundProperty, "ShellContentBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ShellContentBorderBrush");
        border.SetResourceReference(Border.ForegroundProperty, "TextBrush");
        return border;
    }

    private UIElement BuildPad()
    {
        var grid = new Grid();
        for (var c = 0; c < 4; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < 6; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var keys = new string[]
        {
            "MC", "MR", "M+", "C",
            "7", "8", "9", "/",
            "4", "5", "6", "*",
            "1", "2", "3", "-",
            "0", ".", "=", "+"
        };

        for (var i = 0; i < keys.Length; i++)
        {
            var caption = keys[i];
            var button = MakeKey(caption);
            Grid.SetRow(button, i / 4);
            Grid.SetColumn(button, i % 4);
            grid.Children.Add(button);
        }

        return grid;
    }

    private Button MakeKey(string caption)
    {
        var isOp = caption == "/" || caption == "*" || caption == "-" || caption == "+" || caption == "=";
        var isFn = caption == "C" || caption == "MC" || caption == "MR" || caption == "M+";

        var button = new Button
        {
            Content = caption,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(3),
            Padding = new Thickness(0, 8, 0, 8),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1)
        };

        button.SetResourceReference(Button.BackgroundProperty, "BackgroundBrush");
        button.SetResourceReference(Button.ForegroundProperty, "TextBrush");
        button.SetResourceReference(Button.BorderBrushProperty, "ShellContentBorderBrush");

        if (isOp || isFn)
            button.SetResourceReference(Button.BackgroundProperty, "ShellRibbonHoverBrush");

        if (caption == "=")
            button.FontWeight = FontWeights.Bold;

        button.Click += (_, _) => Press(caption);
        return button;
    }

    private void Press(string caption)
    {
        switch (caption)
        {
            case "C": ClearAll(); break;
            case "MC": _memory = 0; break;
            case "MR": SetDisplay(_memory); _freshEntry = true; break;
            case "M+": _memory = ParseDisplay(); break;
            case "=": Evaluate(); break;
            case "/":
            case "*":
            case "-":
            case "+": PushOperator(caption); break;
            case ".": Dot(); break;
            default: Digit(caption); break;
        }
    }

    private void Digit(string d)
    {
        _display.Text = _freshEntry || _display.Text == "0" ? d : _display.Text + d;
        _freshEntry = false;
    }

    private void Dot()
    {
        if (_freshEntry)
        {
            _display.Text = "0.";
            _freshEntry = false;
            return;
        }

        if (!_display.Text.Contains(".", StringComparison.Ordinal))
            _display.Text = _display.Text + ".";
    }

    private void ClearAll()
    {
        _accumulator = 0;
        _pendingOperator = string.Empty;
        _freshEntry = true;
        SetDisplay(0);
    }

    private void PushOperator(string op)
    {
        if (!_freshEntry && _pendingOperator.Length > 0)
            _accumulator = Compute(_accumulator, ParseDisplay(), _pendingOperator);
        else if (_freshEntry && _pendingOperator.Length == 0)
            _accumulator = ParseDisplay();

        if (_pendingOperator.Length == 0 || !_freshEntry)
            SetDisplay(_accumulator);

        _pendingOperator = op;
        _freshEntry = true;
    }

    private void Evaluate()
    {
        if (_pendingOperator.Length == 0)
            return;

        var right = ParseDisplay();
        var result = Compute(_accumulator, right, _pendingOperator);
        AddHistory(string.Concat(
            Format(_accumulator), " ", _pendingOperator, " ", Format(right), " = ", Format(result)));

        SetDisplay(result);
        _accumulator = result;
        _pendingOperator = string.Empty;
        _freshEntry = true;
    }

    private static double Compute(double a, double b, string op) => op switch
    {
        "+" => a + b,
        "-" => a - b,
        "*" => a * b,
        "/" => b == 0 ? double.NaN : a / b,
        _ => b
    };

    private double ParseDisplay()
    {
        if (double.TryParse(_display.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
            return v;
        return 0;
    }

    private void SetDisplay(double value)
    {
        _display.Text = double.IsNaN(value) ? "Cannot divide by zero" : Format(value);
    }

    private static string Format(double value) =>
        value.ToString("G15", CultureInfo.InvariantCulture);

    private void AddHistory(string entry)
    {
        _history.Items.Insert(0, entry);
        while (_history.Items.Count > 40)
            _history.Items.RemoveAt(_history.Items.Count - 1);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key;

        if (key >= Key.D0 && key <= Key.D9)
        {
            Press(((int)key - (int)Key.D0).ToString(CultureInfo.InvariantCulture));
            e.Handled = true;
        }
        else if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            Press(((int)key - (int)Key.NumPad0).ToString(CultureInfo.InvariantCulture));
            e.Handled = true;
        }
        else
        {
            switch (key)
            {
                case Key.Add: Press("+"); e.Handled = true; break;
                case Key.Subtract: Press("-"); e.Handled = true; break;
                case Key.Multiply: Press("*"); e.Handled = true; break;
                case Key.Divide: Press("/"); e.Handled = true; break;
                case Key.Enter: Press("="); e.Handled = true; break;
                case Key.Escape: Press("C"); e.Handled = true; break;
                case Key.Decimal:
                case Key.OemPeriod: Press("."); e.Handled = true; break;
                case Key.Back:
                    if (!_freshEntry && _display.Text.Length > 1)
                        _display.Text = _display.Text.Substring(0, _display.Text.Length - 1);
                    else
                        SetDisplay(0);
                    e.Handled = true;
                    break;
            }
        }
    }

    /// <summary>Single-instance opener used by RibbonWinDisp.</summary>
    public static void Open(Window? owner)
    {
        RibbonWinDisp.ShowSingleton(owner, new CalcWin());
    }
}
