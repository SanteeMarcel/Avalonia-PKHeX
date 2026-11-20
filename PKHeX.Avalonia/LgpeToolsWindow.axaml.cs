using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PKHeX.Core;

namespace PKHeX.Avalonia;

/// <summary>
/// Let's Go, Pikachu! / Let's Go, Eevee! (<see cref="SAV7b"/>) save tools. Currently a single
/// Trainer tab mirroring the WinForms <c>SAV_Trainer7GG</c> trainer fields: OT / rival name,
/// gender, game, language, 6-digit TID / 4-digit SID, Game Sync ID, money, play time, adventure
/// start and last-saved dates. Boxes and party are handled by <see cref="SavePanel"/> itself.
/// <para>
/// Deliberately left out (vs WinForms): map coordinates (writing a bad position can soft-lock the
/// save), the GO Park import/export, and the trainer-title / fashion unlock buttons.
/// </para>
/// Same model as the other tool windows: edits target the live save in memory; the panel's 💾 Save
/// — or this window's "Save to Disk" — writes to disk.
/// </summary>
public partial class LgpeToolsWindow : Window
{
    private const decimal U16 = ushort.MaxValue;

    /// <summary>Gen 7+ trainer IDs are displayed as ID32 split in base 10: TID = low 6 digits, SID = the rest.</summary>
    private const decimal MaxTID7 = 999_999;
    private const decimal MaxSID7 = 4294; // uint.MaxValue / 1,000,000

    private static readonly IReadOnlyList<Opt> GenderOpts = [new Opt("Male", 0), new Opt("Female", 1)];

    /// <summary>Only the two LGPE versions — a save claiming any other game is rejected by IsVersionValid.</summary>
    private static readonly IReadOnlyList<Opt> GameOpts = GameInfo.Sources.VersionDataSource
        .Where(z => (GameVersion)z.Value is GameVersion.GP or GameVersion.GE)
        .Select(z => new Opt(z.Text, z.Value)).ToList();

    private static readonly IReadOnlyList<Opt> LanguageOpts =
        ToolsUi.ToOpts(GameInfo.LanguageDataSource(7, EntityContext.Gen7b));

    private readonly Func<Task<string?>>? _persist;
    private readonly List<Action> _applyHooks = [];
    private readonly TabControl _tabs;
    private readonly TextBlock _status;

    public LgpeToolsWindow() : this(new SAV7b(), null, null) { }

    /// <param name="sav">The live save; edits land in its buffers on Apply.</param>
    /// <param name="loadedPath">Unused for now (no importable blobs); kept for parity with the other tool windows.</param>
    /// <param name="persist">Writes the save to disk; null turns Apply into an in-memory apply.</param>
    public LgpeToolsWindow(SaveFile sav, string? loadedPath, Func<Task<string?>>? persist)
    {
        AvaloniaXamlLoader.Load(this);
        _persist = persist;

        _tabs = this.FindControl<TabControl>("RootTabs")!;
        _status = this.FindControl<TextBlock>("StatusText")!;
        var apply = this.FindControl<Button>("ApplyButton")!;
        var close = this.FindControl<Button>("CloseButton")!;
        apply.Content = persist is null ? "Apply & Close" : "Save to Disk";
        apply.Click += async (_, _) => await ApplyAndClose(persist is not null);
        close.Click += (_, _) => { Flush(); Close(); };

        ComboWheelGuard.Attach(this); // wheel over a closed combo must scroll the form, not edit it

        if (sav is SAV7b gg)
        {
            Title = $"Let's Go Tools — {gg.OT} ({gg.Version})";
            _tabs.Items.Add(new TabItem { Header = "Trainer", Content = new ScrollViewer { Content = BuildTrainerTab(gg) } });
        }

        if (_tabs.Items.Count != 0)
            _tabs.SelectedIndex = 0;
    }

    /// <summary>
    /// Commits every registered form into the save. Apply runs from an <c>async void</c> handler, so
    /// a throwing setter is caught and reported here rather than allowed to kill the process.
    /// </summary>
    private bool Flush()
    {
        string? failure = null;
        foreach (var h in _applyHooks)
        {
            try { h(); }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                failure ??= ex.Message;
            }
        }
        if (failure is null)
            return true;
        _status.Text = $"Some values were rejected: {failure}";
        return false;
    }

    private async Task ApplyAndClose(bool persist)
    {
        if (!Flush())
            return; // leave the window open so the user can see what to fix
        if (persist && _persist is not null)
        {
            var p = await _persist();
            _status.Text = p is null ? "Applied." : $"Saved → {p}";
        }
        Close();
    }

    // ---------------- Trainer ----------------

    private Control BuildTrainerTab(SAV7b sav)
    {
        var f = new FieldForm();

        f.Header("Trainer");
        // Only write names when they actually changed: SetString re-encodes and clears the trash
        // bytes, so an unconditional write would churn an otherwise untouched save (WinForms does the same).
        f.Text("OT Name", () => sav.OT, v => { if (v != sav.OT) sav.OT = v; }, sav.MaxStringLengthTrainer);
        f.Text("Rival Name", () => sav.Misc.RivalName,
            v => { if (v != sav.Misc.RivalName) sav.Misc.RivalName = v; }, sav.MaxStringLengthTrainer);
        f.Combo("Gender", GenderOpts, () => sav.Gender, v => sav.Gender = (byte)v);
        f.Combo("Game", GameOpts, () => (int)sav.Version, v => sav.Version = (GameVersion)v);
        // SAV7b.Language writes both MyStatus7b and ConfigSave7b (the game stores it twice).
        f.Combo("Language", LanguageOpts, () => sav.Language, v => sav.Language = v);

        // TID and SID share one ID32, so they're committed together: the TID row stashes its value
        // and the SID row (saved after it — FieldForm saves in row order) writes the pair.
        uint pendingTid = 0;
        f.Num("TID (6-digit)", () => sav.GetTrainerTID7(), v => pendingTid = (uint)v, MaxTID7);
        f.Num("SID (4-digit)", () => sav.GetTrainerSID7(), v => SetTrainerID(sav, (uint)v, pendingTid), MaxSID7);
        f.Text("Game Sync ID (16 hex)", () => sav.GameSyncID, v => SetGameSyncID(sav, v), MyStatus7b.GameSyncIDSize);

        f.Header("Progress");
        f.Num("Money", () => sav.Money, v => sav.Money = (uint)v, sav.MaxMoney);

        f.Header("Play Time");
        f.Num("Hours", () => sav.PlayedHours, v => sav.PlayedHours = (int)v, U16);
        f.Num("Minutes", () => sav.PlayedMinutes, v => sav.PlayedMinutes = (int)v, 59);
        f.Num("Seconds", () => sav.PlayedSeconds, v => sav.PlayedSeconds = (int)v, 59);

        // Both timestamps carry a time of day the date picker can't show; keep it on write-back.
        var begin = sav.PlayerGeoLocation.AdventureBegin;
        f.Date("Adventure Started",
            () => DateOnly.FromDateTime(begin.Timestamp),
            d =>
            {
                if (d is not { } date) return; // no "unset" state for this epoch value — Clear = leave as-is
                // Stored as unsigned seconds since 1970: an earlier date would wrap to the far future.
                if (date < new DateOnly(1970, 1, 1)) return;
                var next = date.ToDateTime(TimeOnly.FromDateTime(begin.Timestamp));
                if (next != begin.Timestamp) begin.Timestamp = next;
            });
        f.Date("Last Saved",
            () => sav.Played.LastSavedDate is { } dt ? DateOnly.FromDateTime(dt) : null,
            d =>
            {
                var old = sav.Played.LastSavedDate;
                if (d is not { } date)
                {
                    if (old is not null) sav.Played.LastSavedDate = null; // Clear zeroes the stamp
                    return;
                }
                var time = old is { } o ? TimeOnly.FromDateTime(o) : TimeOnly.MinValue;
                var next = date.ToDateTime(time);
                if (next != old) sav.Played.LastSavedDate = next;
            });
        ToolsUi.ReadOnlyRow(f, "Last Saved (raw)", () => sav.Played.LastSavedTime);

        f.Load();
        _applyHooks.Add(f.Save);
        return f;
    }

    /// <summary>
    /// TID7 + SID7 × 1,000,000 must fit in a uint; Core's <c>SetTrainerID7</c> silently wraps an
    /// overflowing pair (e.g. SID 4294 / TID 999999), so reject it here instead.
    /// </summary>
    private static void SetTrainerID(SAV7b sav, uint sid, uint tid)
    {
        if (!sav.IsValidTrainerID7(sid, tid))
            throw new ArgumentOutOfRangeException(nameof(sid), $"TID {tid:000000} / SID {sid:0000} exceeds the 32-bit trainer ID range.");
        if (sid == sav.GetTrainerSID7() && tid == sav.GetTrainerTID7())
            return;
        sav.SetTrainerID7(sid, tid);
    }

    /// <summary>Accepts exactly 16 hex digits; anything else leaves the stored ID untouched.</summary>
    private static void SetGameSyncID(SAV7b sav, string value)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length != MyStatus7b.GameSyncIDSize || !v.All(char.IsAsciiHexDigit))
            return;
        if (!string.Equals(v, sav.GameSyncID, StringComparison.OrdinalIgnoreCase))
            sav.GameSyncID = v;
    }
}
