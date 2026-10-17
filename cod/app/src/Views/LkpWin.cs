using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ShouTech.App.Services;

namespace ShouTech.App.Views;

/// <summary>Generic editable master-data list (areas, groups, payment methods, units, ...).</summary>
public sealed class LkpWin : BaseWin
{
    private readonly string _kind;
    private readonly string _key;
    private readonly DataGrid _grid = new();
    private readonly ObservableCollection<LkpSvc.Row> _rows = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnDelete = new();

    public LkpWin(string key, string kind)
    {
        _key = key;
        _kind = kind;
        Width = 760;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _btnSave.Padding = _btnDelete.Padding = new Thickness(10, 3, 10, 3);
        _btnSave.Margin = _btnDelete.Margin = new Thickness(0, 0, 6, 8);
        _btnSave.Click += (_, _) => Save();
        _btnDelete.Click += (_, _) => DeleteSelected();

        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = true;
        _grid.ItemsSource = _rows;
        AddText("Lkp.Code", nameof(LkpSvc.Row.Code), 140);
        AddText("Lkp.NameAr", nameof(LkpSvc.Row.NameAr), 220);
        AddText("Lkp.NameEn", nameof(LkpSvc.Row.NameEn), 220);
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = T("Lkp.Active"),
            Binding = new System.Windows.Data.Binding(nameof(LkpSvc.Row.IsActive))
        });

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(_btnSave);
        bar.Children.Add(_btnDelete);
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_grid);
        Content = root;

        ApplyLocalizedText();
        Reload();
    }

    public static bool TryOpen(string? key, Window? owner)
    {
        if (!LkpSvc.TryGetKind(key, out var kind)) return false;
        foreach (Window w in System.Windows.Application.Current.Windows)
        {
            if (w is LkpWin open && open._kind == kind && open.IsVisible)
            {
                open.Activate();
                return true;
            }
        }
        new LkpWin(key!, kind) { Owner = owner }.Show();
        return true;
    }

    private void AddText(string key, string path, double width) =>
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = T(key),
            Width = width,
            Binding = new System.Windows.Data.Binding(path)
        });

    private void Reload()
    {
        try
        {
            _rows.Clear();
            foreach (var r in LkpSvc.Load(_kind)) _rows.Add(r);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save()
    {
        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            LkpSvc.Save(_kind, _rows.ToList(), CurrentUserId);
            MessageBox.Show(this, T("Lkp.Saved"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteSelected()
    {
        if (_grid.SelectedItem is LkpSvc.Row row)
            _rows.Remove(row);
    }

    protected override void ApplyLocalizedText()
    {
        Title = LocSvc?.GetString(_key, _key) ?? _key;
        _btnSave.Content = T("Common.Save");
        _btnDelete.Content = T("Lkp.Delete");
        if (_grid.Columns.Count == 4)
        {
            _grid.Columns[0].Header = T("Lkp.Code");
            _grid.Columns[1].Header = T("Lkp.NameAr");
            _grid.Columns[2].Header = T("Lkp.NameEn");
            _grid.Columns[3].Header = T("Lkp.Active");
        }
    }
}
