using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShouTech.App.Views;

/// <summary>
/// Phase 7 - Information window (View > Info window).
/// Shows live session, company, and environment details in a modern card layout.
/// </summary>
public sealed class InfoWin : BaseWin
{
    private readonly Grid _rows = new();

    private InfoWin()
    {
        Title = "Information";
        Width = 520;
        Height = 520;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        BuildUi();
    }

    protected override void ApplyLocalizedText()
    {
        Title = L("View.InfoWindow", "Information");
        RefreshValues();
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
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var icon = new TextBlock
        {
            Text = "ℹ️",
            FontSize = 26,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        header.Children.Add(icon);

        var title = new TextBlock
        {
            Text = L("View.InfoWindow", "Information"),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        header.Children.Add(title);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            Child = _rows
        };
        card.SetResourceReference(Border.BackgroundProperty, "ShellContentBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "ShellContentBorderBrush");
        card.SetResourceReference(Border.ForegroundProperty, "TextBrush");
        scroll.Content = card;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        _rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        _rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddSection("Session");
        AddRow("Company", () => AppState.CurrentCompanyDisplayName ?? "-");
        AddRow("User", () => CurrentUserDisplay);
        AddRow("Database", () => AppState.CurrentDatabaseName ?? "-");
        AddRow("Machine", () => Environment.MachineName);
        AddRow("Operating system", () => Environment.OSVersion.ToString());
        AddRow("Runtime", () => string.Concat(".NET ", Environment.Version.ToString()));

        AddSection("Shortcuts");
        AddRow("Print", () => "Ctrl+P");
        AddRow("Search", () => "Ctrl+F");
        AddRow("Calculator", () => "F9");
        AddRow("Close window", () => "Esc");

        Content = root;
        RefreshValues();
    }

    private void AddSection(string caption)
    {
        var row = _rows.RowDefinitions.Count;
        _rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

        var text = new TextBlock
        {
            Text = caption,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 14, 0, 6)
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        Grid.SetRow(text, row);
        Grid.SetColumnSpan(text, 2);
        _rows.Children.Add(text);
    }

    private void AddRow(string label, Func<string> value)
    {
        var row = _rows.RowDefinitions.Count;
        _rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Margin = new Thickness(0, 4, 12, 4),
            Opacity = 0.78
        };
        labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, 0);
        _rows.Children.Add(labelBlock);

        var valueBlock = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };
        valueBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        Grid.SetRow(valueBlock, row);
        Grid.SetColumn(valueBlock, 1);
        _rows.Children.Add(valueBlock);
        valueBlock.Tag = value;
    }

    private void RefreshValues()
    {
        foreach (var child in _rows.Children)
        {
            if (child is TextBlock block && block.Tag is Func<string> value)
                block.Text = TryGet(value);
        }
    }

    private static string TryGet(Func<string> value)
    {
        try
        {
            return value() ?? string.Empty;
        }
        catch
        {
            return "-";
        }
    }

    /// <summary>Single-instance opener used by RibbonWinDisp.</summary>
    public static void Open(Window? owner)
    {
        RibbonWinDisp.ShowSingleton(owner, new InfoWin());
    }
}
