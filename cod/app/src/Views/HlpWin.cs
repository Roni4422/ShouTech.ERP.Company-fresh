using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShouTech.App.Views;

/// <summary>
/// Phase 7 - Help documentation window (Help > Instructions).
/// Offline quick guide covering the main ERP workflows in a modern
/// topic list + content layout.
/// </summary>
public sealed class HlpWin : BaseWin
{
    private readonly ListBox _topics = new() { Width = 210 };
    private readonly TextBlock _content = new()
    {
        FontSize = 14,
        TextWrapping = TextWrapping.Wrap
    };

    private readonly List<string[]> _topicData = new();

    private HlpWin()
    {
        Title = "Instructions";
        Width = 780;
        Height = 540;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        BuildTopics();
        BuildUi();
    }

    protected override void ApplyLocalizedText()
    {
        Title = L("Help.Instructions", "Instructions");
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

    private void BuildTopics()
    {
        _topicData.Add(new[]
        {
            "Getting started",
            "Create your company from File > New, then define materials, customers, and the chart of accounts before posting.",
            "Open the dashboard for a live overview of sales, stock, and cash at any time."
        });
        _topicData.Add(new[]
        {
            "Point of sale",
            "Open POS from the Point of Sale tab. The cashier screen supports barcode scanning, quick keys, and discounts.",
            "Close the cash register at end of shift from Invoices > Close cash register."
        });
        _topicData.Add(new[]
        {
            "Invoices and returns",
            "Sales and purchase invoices are created from the Invoices tab. Returns are separate document types that reverse stock and accounts.",
            "Use Search invoices to locate, reprint, or annotate any document."
        });
        _topicData.Add(new[]
        {
            "Vouchers and accounting",
            "Journal, receipt, and payment vouchers post to the chart of accounts. Each posting validates the fiscal period and account eligibility.",
            "Periods close in chronological order; a fiscal year closes only after all its periods are closed."
        });
        _topicData.Add(new[]
        {
            "Backup and restore",
            "Tools > Backup creates a rotated backup. Restores are performed from File > Restore backup.",
            "Back up before every migration and before the fiscal year close."
        });
        _topicData.Add(new[]
        {
            "Users and permissions",
            "Administrators manage users, roles, and permission templates from Other Tools.",
            "Viewer accounts are read-only across all operational surfaces."
        });
        _topicData.Add(new[]
        {
            "Keyboard shortcuts",
            "Ctrl+P prints the active report, Ctrl+F opens search, F9 opens the calculator.",
            "Esc closes the active dialog; the ribbon collapses automatically after every action."
        });

        foreach (var topic in _topicData)
            _topics.Items.Add(topic[0]);

        _topics.SelectedIndex = 0;
        _topics.SelectionChanged += (_, _) => ShowTopic();
    }

    private void ShowTopic()
    {
        if (_topics.SelectedIndex < 0 || _topics.SelectedIndex >= _topicData.Count)
            return;

        var topic = _topicData[_topics.SelectedIndex];
        _content.Text = string.Join(Environment.NewLine + Environment.NewLine, topic);
    }

    private void BuildUi()
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var listCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 12, 0),
            BorderThickness = new Thickness(1),
            Child = _topics
        };
        listCard.SetResourceReference(Border.BackgroundProperty, "ShellContentBackgroundBrush");
        listCard.SetResourceReference(Border.BorderBrushProperty, "ShellContentBorderBrush");
        listCard.SetResourceReference(Border.ForegroundProperty, "TextBrush");
        _topics.SetResourceReference(ListBox.ForegroundProperty, "TextBrush");
        Grid.SetColumn(listCard, 0);
        root.Children.Add(listCard);

        var contentCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                Content = _content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }
        };
        contentCard.SetResourceReference(Border.BackgroundProperty, "ShellContentBackgroundBrush");
        contentCard.SetResourceReference(Border.BorderBrushProperty, "ShellContentBorderBrush");
        contentCard.SetResourceReference(Border.ForegroundProperty, "TextBrush");
        _content.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        Grid.SetColumn(contentCard, 1);
        root.Children.Add(contentCard);

        Content = root;
        ShowTopic();
    }

    /// <summary>Single-instance opener used by RibbonWinDisp.</summary>
    public static void Open(Window? owner)
    {
        RibbonWinDisp.ShowSingleton(owner, new HlpWin());
    }
}
