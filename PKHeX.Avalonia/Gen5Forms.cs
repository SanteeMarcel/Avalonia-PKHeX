using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PKHeX.Avalonia;

/// <summary>A label/value option for a <see cref="ComboBox"/>. <see cref="ToString"/> is the display text.</summary>
public sealed record Opt(string Text, int Value)
{
    public override string ToString() => Text;
}

/// <summary>
/// Lightweight, code-built form helper. Each <c>Add*</c> call appends a labelled row and registers
/// matching load/save actions so the whole form can be populated from, and written back to, a model
/// with one <see cref="Load"/> / <see cref="Save"/> call. Avoids hand-writing dozens of named XAML
/// controls + FindControl lookups for the (very large) Join Avenue field surface.
/// </summary>
public sealed class FieldForm : StackPanel
{
    private readonly List<Action> _load = [];
    private readonly List<Action> _save = [];

    public FieldForm()
    {
        Spacing = 2;
        Margin = new Thickness(6);
    }

    public void Load() { foreach (var a in _load) a(); }
    public void Save() { foreach (var a in _save) a(); }

    public void Header(string text) => Children.Add(new TextBlock
    {
        Text = text,
        FontWeight = FontWeight.Bold,
        Margin = new Thickness(0, 8, 0, 2),
    });

    private void AddRow(string label, Control editor)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*"), Margin = new Thickness(0, 1, 0, 1) };
        var t = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(t, 0);
        Grid.SetColumn(editor, 1);
        g.Children.Add(t);
        g.Children.Add(editor);
        Children.Add(g);
    }

    public void Num(string label, Func<decimal> get, Action<decimal> set, decimal max, decimal min = 0)
    {
        var n = new NumericUpDown
        {
            Minimum = min, Maximum = max, Increment = 1, FormatString = "0",
            Width = 160, HorizontalAlignment = HorizontalAlignment.Left,
        };
        AddRow(label, n);
        _load.Add(() => n.Value = Math.Clamp(get(), min, max));
        _save.Add(() => set(n.Value ?? min));
    }

    public void Text(string label, Func<string> get, Action<string> set, int maxLength)
    {
        var tb = new TextBox { Width = 200, MaxLength = maxLength, HorizontalAlignment = HorizontalAlignment.Left };
        AddRow(label, tb);
        _load.Add(() => tb.Text = get());
        _save.Add(() => set(tb.Text ?? string.Empty));
    }

    public void Check(string label, Func<bool> get, Action<bool> set)
    {
        var c = new CheckBox();
        AddRow(label, c);
        _load.Add(() => c.IsChecked = get());
        _save.Add(() => set(c.IsChecked == true));
    }

    public void Combo(string label, IReadOnlyList<Opt> items, Func<int> get, Action<int> set)
    {
        var cb = new ComboBox { ItemsSource = items, Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        AddRow(label, cb);
        // No FirstOrDefault fallback: if the stored value isn't in the list (e.g. an out-of-range
        // game/language byte) leave the box blank so Save() preserves the original value untouched.
        _load.Add(() => cb.SelectedItem = items.FirstOrDefault(o => o.Value == get()));
        _save.Add(() => { if (cb.SelectedItem is Opt o) set(o.Value); });
    }

    /// <summary>
    /// Date row backed by a <see cref="DatePicker"/> + a Clear button. Works in <see cref="DateOnly"/>?
    /// terms so callers wire it to any date model (e.g. <c>JoinAvenueDate5.Date</c>); null = unset.
    /// </summary>
    public void Date(string label, Func<DateOnly?> get, Action<DateOnly?> set)
    {
        var dp = new DatePicker { HorizontalAlignment = HorizontalAlignment.Left };
        var clear = new Button { Content = "Clear" };
        clear.Click += (_, _) => dp.SelectedDate = null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(dp);
        row.Children.Add(clear);
        AddRow(label, row);
        _load.Add(() => dp.SelectedDate = get() is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null);
        _save.Add(() => set(dp.SelectedDate is { } off ? DateOnly.FromDateTime(off.DateTime) : null));
    }

    /// <summary>Free-form text row (e.g. comma-separated lists) wired to arbitrary load/save.</summary>
    public void Custom(string label, Func<string> get, Action<string> set, int width = 280)
    {
        var tb = new TextBox { Width = width, HorizontalAlignment = HorizontalAlignment.Left };
        AddRow(label, tb);
        _load.Add(() => tb.Text = get());
        _save.Add(() => set(tb.Text ?? string.Empty));
    }

    /// <summary>Add an arbitrary editor control with custom load/save hooks (e.g. multi-line grids).</summary>
    public void CustomControl(string label, Control editor, Action load, Action save)
    {
        AddRow(label, editor);
        _load.Add(load);
        _save.Add(save);
    }
}
