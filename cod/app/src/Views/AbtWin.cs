using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ShouTech.App.Views;

/// <summary>
/// Phase 7 - modern About window (Help > About).
/// Replaces the old MessageBox with a branded, theme-aware dialog.
/// </summary>
public sealed class AbtWin : BaseWin
{
    private TextBlock? _titleText;
    private TextBlock? _versionText;
    private TextBlock? _taglineText;

    private AbtWin()
    {
        Title = "About";
        Width = 460;
        Height = 380;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        BuildUi();
    }

    protected override void ApplyLocalizedText()
    {
        Title = L("Help.About", "About");
        if (_titleText != null)
            _titleText.Text = L("Application.Title", "ShouTech ERP");
        if (_versionText != null)
            _versionText.Text = L("Shell.VersionLabel", "Version 0.4");
        if (_taglineText != null)
            _taglineText.Text = L("Shell.Tagline", "Enterprise ERP - CRM - POS - AI, works offline");
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
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

        var card = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24),
            BorderThickness = new Thickness(1)
        };
        card.SetResourceReference(Border.BackgroundProperty, "ShellContentBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "ShellContentBorderBrush");
        card.SetResourceReference(Border.ForegroundProperty, "TextBrush");
        Grid.SetRow(card, 0);

        var stack = new StackPanel();

        var logo = new TextBlock
        {
            Text = "🏢",
            FontSize = 54,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 8)
        };
        logo.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(logo);

        _titleText = new TextBlock
        {
            Text = L("Application.Title", "ShouTech ERP"),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(_titleText);

        _versionText = new TextBlock
        {
            Text = L("Shell.VersionLabel", "Version 0.4"),
            FontSize = 14,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _versionText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(_versionText);

        _taglineText = new TextBlock
        {
            Text = L("Shell.Tagline", "Enterprise ERP - CRM - POS - AI, works offline"),
            FontSize = 13,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        _taglineText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(_taglineText);

        var copyright = new TextBlock
        {
            Text = "© 2026 ShouTech Software. All rights reserved.",
            FontSize = 12,
            Margin = new Thickness(0, 18, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        copyright.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(copyright);

        var license = new TextBlock
        {
            Text = "Commercial license - see LICENSE.txt",
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        license.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        stack.Children.Add(license);

        card.Child = stack;
        root.Children.Add(card);

        var okButton = new Button
        {
            Content = "OK",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Width = 110,
            Padding = new Thickness(0, 8, 0, 8),
            Margin = new Thickness(0, 14, 0, 0),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
            BorderThickness = new Thickness(1),
            IsDefault = true,
            IsCancel = true
        };
        okButton.SetResourceReference(Button.BackgroundProperty, "AccentPrimary");
        okButton.SetResourceReference(Button.ForegroundProperty, "TextBrush");
        okButton.SetResourceReference(Button.BorderBrushProperty, "ShellContentBorderBrush");
        okButton.Click += (_, _) => Close();
        Grid.SetRow(okButton, 1);
        root.Children.Add(okButton);

        Content = root;
    }

    /// <summary>Single-instance opener used by RibbonWinDisp.</summary>
    public static void Open(Window? owner)
    {
        RibbonWinDisp.ShowSingleton(owner, new AbtWin());
    }
}
