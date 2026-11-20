using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PKHeX.Core;

namespace PKHeX.Avalonia;

/// <summary>
/// Small layout / option-list helpers shared by the per-generation tool windows. These started life
/// as private statics inside <see cref="Gen6ToolsWindow"/>; anything new (e.g.
/// <see cref="Gen4ToolsWindow"/>) should use this copy rather than adding another.
/// </summary>
public static class ToolsUi
{
    /// <summary>Side-by-side equal columns.</summary>
    public static Control TwoColumns(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Docks a button row (and optional hint text) beneath a form body.</summary>
    public static Control WithToolbar(Control body, string? hint, params Button[] buttons)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(6, 4, 6, 4),
        };
        foreach (var b in buttons)
            row.Children.Add(b);
        if (hint is not null)
        {
            row.Children.Add(new TextBlock
            {
                Text = hint, FontSize = 11, Opacity = 0.85,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var wrap = new Border
        {
            Child = row,
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)),
        };
        DockPanel.SetDock(wrap, Dock.Bottom);

        var dock = new DockPanel();
        dock.Children.Add(wrap);
        dock.Children.Add(body);
        return dock;
    }

    /// <summary>
    /// A button that runs a bulk block mutation then re-reads the form from the new data. Without the
    /// re-read, the form's cached pre-op values would be written straight back over the bulk edit on
    /// the next Apply.
    /// </summary>
    public static Button Bulk(string label, Action op, FieldForm form)
    {
        var b = new Button { Content = label };
        b.Click += (_, _) => { op(); form.Load(); };
        return b;
    }

    /// <summary>Read-only display row (no write-back on Save).</summary>
    public static void ReadOnlyRow(FieldForm f, string label, Func<string> get)
    {
        var tb = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        f.CustomControl(label, tb, load: () => tb.Text = get(), save: () => { });
    }

    /// <summary>Hex row for a 32-bit value (PID / encryption constant), shown as 8 upper-case digits.</summary>
    public static void HexRow(FieldForm f, string label, Func<uint> get, Action<uint> set)
        => f.Custom(label,
            () => get().ToString("X8", CultureInfo.InvariantCulture),
            s =>
            {
                if (uint.TryParse(s.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                    set(v);
            }, 120);

    public static IReadOnlyList<Opt> ToOpts(IReadOnlyList<ComboItem> items)
        => items.Select(c => new Opt(c.Text, c.Value)).ToList();

    /// <summary>
    /// Save-filtered dropdown, guaranteeing a value-0 "(none)" entry — an id of 0 is the legitimate
    /// "empty" state for a record holder / participant, but the filtered sources don't always include it.
    /// </summary>
    public static IReadOnlyList<Opt> ToOptsWithNone(IReadOnlyList<ComboItem> items)
    {
        var list = new List<Opt>(items.Count + 1);
        if (!items.Any(c => c.Value == 0))
            list.Add(new Opt("(none)", 0));
        list.AddRange(items.Select(c => new Opt(c.Text, c.Value)));
        return list;
    }

    /// <summary>"HurdleDash" → "Hurdle Dash", for enum-derived row labels.</summary>
    public static string SplitPascal(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (i != 0 && (char.IsUpper(c) || char.IsDigit(c)) && !char.IsWhiteSpace(value[i - 1])
                && !(char.IsDigit(c) && char.IsDigit(value[i - 1])))
            {
                sb.Append(' ');
            }
            sb.Append(c == '_' ? ' ' : c);
        }
        return sb.ToString().Replace("  ", " ").Trim();
    }
}
