using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShouTech.App.Views;

/// <summary>
/// Phase 7 - on-screen keyboard (Tools > Keyboard) with EN / AR / FR layouts.
/// Touch-friendly, theme-aware, works fully offline.
/// </summary>
public sealed class KbdWin : BaseWin
{
    private readonly ComboBox _layoutPicker = new() { Width = 160, Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBox _target = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        FontSize = 15,
        MinHeight = 90,
        Padding = new Thickness(8)
    };

    private readonly Panel _keysHost = new StackPanel();

    private static readonly string[][] EnRows =
    {
        new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" },
        new[] { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" },
        new[] { "a", "s", "d", "f", "g", "h", "j", "k", "l" },
        new[] { "z", "x", "c", "v", "b", "n", "m" }
    };

    private static readonly string[][] ArRows =
    {
        new[] { "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩", "٠" },
        new[] { "ض", "ص", "ث", "ق", "ف", "غ", "ع", "ه", "خ", "ح" },
        new[] { "ش", "س", "ي", "ب", "ل", "ا", "ت", "ن", "م", "ك" },
        new[] { "ط", "د", "ز", "ر", "و", "ة", "ظ" }
    };

    private static readonly string[][] FrRows =
    {
        new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" },
        new[] { "a", "z", "e", "r", "t", "y", "u", "i", "o", "p" },
        new[] { "q", "s", "d", "f", "g", "h", "j", "k", "l", "m" },
        new[] { "w", "x", "c", "v", "b", "n" }
    };

    private KbdWin()
    {
        Title = "Keyboard";
        Width = 640;
        Height = 480;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        BuildUi();
    }

    protected override void ApplyLocalizedText()
    {
        Title = L("Tools.Keyboard", "Keyboard");
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
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

        var topPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        _layoutPicker.Items.Add("English (QWERTY)");
        _layoutPicker.Items.Add("العربية");
        _layoutPicker.Items.Add("Français (AZERTY)");
        _layoutPicker.SelectedIndex = 0;
        _layoutPicker.SelectionChanged += (_, _) => RebuildKeys();
        _layoutPicker.SetResourceReference(ComboBox.ForegroundProperty, "TextBrush");
        topPanel.Children.Add(_layoutPicker);

        var copyButton = MakeButton("📋 Copy");
        copyButton.Click += (_, _) =>
        {
            try
            {
                if (_target.Text.Length > 0)
                    Clipboard.SetText(_target.Text);
            }
            catch
            {
                // Clipboard can be locked by another process.
            }
        };
        topPanel.Children.Add(copyButton);
        Grid.SetRow(topPanel, 0);
        root.Children.Add(topPanel);

        var targetCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 10),
            BorderThickness = new Thickness(1),
            Child = _target
        };
        targetCard.SetResourceReference(Border.BackgroundProperty, "ShellContentBackgroundBrush");
        targetCard.SetResourceReference(Border.BorderBrushProperty, "ShellContentBorderBrush");
        _target.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
        Grid.SetRow(targetCard, 1);
        root.Children.Add(targetCard);

        var keysCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            Child = _keysHost
        };
        keysCard.SetResourceReference(Border.BackgroundProperty, "ShellContentBackgroundBrush");
        keysCard.SetResourceReference(Border.BorderBrushProperty, "ShellContentBorderBrush");
        Grid.SetRow(keysCard, 2);
        root.Children.Add(keysCard);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var space = MakeButton("Space");
        space.MinWidth = 180;
        space.Click += (_, _) => Type(" ");
        actionPanel.Children.Add(space);

        var back = MakeButton("⌫ Backspace");
        back.Click += (_, _) =>
        {
            if (_target.Text.Length > 0)
                _target.Text = _target.Text.Substring(0, _target.Text.Length - 1);
            _target.CaretIndex = _target.Text.Length;
        };
        actionPanel.Children.Add(back);

        var newline = MakeButton("↵ Enter");
        newline.Click += (_, _) =>
        {
            _target.AppendText(Environment.NewLine);
            _target.CaretIndex = _target.Text.Length;
        };
        actionPanel.Children.Add(newline);

        var clear = MakeButton("🗑 Clear");
        clear.Click += (_, _) => _target.Clear();
        actionPanel.Children.Add(clear);

        Grid.SetRow(actionPanel, 3);
        root.Children.Add(actionPanel);

        Content = root;
        RebuildKeys();
    }

    private void RebuildKeys()
    {
        var rows = _layoutPicker.SelectedIndex switch
        {
            1 => ArRows,
            2 => FrRows,
            _ => EnRows
        };

        _keysHost.Children.Clear();
        var stack = (StackPanel)_keysHost;

        foreach (var row in rows)
        {
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };

            foreach (var ch in row)
            {
                var key = MakeButton(ch);
                key.MinWidth = 44;
                key.Click += (_, _) => Type(ch);
                line.Children.Add(key);
            }

            stack.Children.Add(line);
        }
    }

    private void Type(string text)
    {
        var caret = _target.CaretIndex;
        _target.Text = _target.Text.Insert(caret, text);
        _target.CaretIndex = caret + text.Length;
        _target.Focus();
    }

    private Button MakeButton(string caption)
    {
        var button = new Button
        {
            Content = caption,
            FontSize = 15,
            Margin = new Thickness(2),
            Padding = new Thickness(10, 8, 10, 8),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1)
        };
        button.SetResourceReference(Button.BackgroundProperty, "BackgroundBrush");
        button.SetResourceReference(Button.ForegroundProperty, "TextBrush");
        button.SetResourceReference(Button.BorderBrushProperty, "ShellContentBorderBrush");
        return button;
    }

    /// <summary>Single-instance opener used by RibbonWinDisp.</summary>
    public static void Open(Window? owner)
    {
        RibbonWinDisp.ShowSingleton(owner, new KbdWin());
    }
}
