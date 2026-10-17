using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShouTech.App.Views;

/// <summary>
/// Phase 7 - Feedback window (Help > Suggestions).
/// Lets users compose structured feedback and copy it for the support channel.
/// Works fully offline; nothing leaves the machine automatically.
/// </summary>
public sealed class FdbWin : BaseWin
{
    private readonly ComboBox _category = new() { Width = 200, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 180,
        FontSize = 14,
        Padding = new Thickness(8)
    };

    private FdbWin()
    {
        Title = "Feedback";
        Width = 520;
        Height = 420;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        BuildUi();
    }

    protected override void ApplyLocalizedText()
    {
        Title = L("Help.Suggestions", "Feedback");
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
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

        _category.Items.Add("🐞 Bug report");
        _category.Items.Add("💡 Suggestion");
        _category.Items.Add("❓ Question");
        _category.SelectedIndex = 0;
        _category.SetResourceReference(ComboBox.ForegroundProperty, "TextBrush");
        Grid.SetRow(_category, 0);
        root.Children.Add(_category);

        var textCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 10),
            BorderThickness = new Thickness(1),
            Child = _text
        };
        textCard.SetResourceReference(Border.BackgroundProperty, "ShellContentBackgroundBrush");
        textCard.SetResourceReference(Border.BorderBrushProperty, "ShellContentBorderBrush");
        _text.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
        Grid.SetRow(textCard, 1);
        root.Children.Add(textCard);

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var copyButton = new Button
        {
            Content = "📋 Copy feedback",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1)
        };
        copyButton.SetResourceReference(Button.BackgroundProperty, "AccentPrimary");
        copyButton.SetResourceReference(Button.ForegroundProperty, "TextBrush");
        copyButton.SetResourceReference(Button.BorderBrushProperty, "ShellContentBorderBrush");
        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(
                    string.Concat(
                        "Category: ", _category.SelectedItem ?? string.Empty,
                        Environment.NewLine, Environment.NewLine,
                        _text.Text));
            }
            catch
            {
                // Clipboard can be locked by another process.
            }
        };
        buttonPanel.Children.Add(copyButton);

        var note = new TextBlock
        {
            Text = "Copy the text and send it to the ShouTech support channel.",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.75
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        buttonPanel.Children.Add(note);

        Grid.SetRow(buttonPanel, 2);
        root.Children.Add(buttonPanel);

        Content = root;
    }

    /// <summary>Single-instance opener used by RibbonWinDisp.</summary>
    public static void Open(Window? owner)
    {
        RibbonWinDisp.ShowSingleton(owner, new FdbWin());
    }
}
