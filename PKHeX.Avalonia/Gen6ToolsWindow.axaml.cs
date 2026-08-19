using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PKHeX.Core;

namespace PKHeX.Avalonia;

/// <summary>
/// Gen 6 (XY / ORAS) save tools. Hosts a tab per available feature:
///   - Trainer: identity, region/language, money/badges/BP/Poké Miles, play time, sayings,
///     Mega unlocks, Battle Maison streaks (+ XY-only trainer nickname / eye colour).
///   - Poké Puffs: the 100 puff slots + count, with give-all / reset / sort helpers.
///   - O-Powers: per-power unlock flags, field/battle power levels, points, unlock-all / clear-all.
///   - Super Training: regimen + distribution unlock flags, per-stage record times and record
///     holders, the 12 training-bag slots, unlock-all / clear-block.
///   - Pokémon Link: the <see cref="PL6"/> block — enabled flag, origin, BP, Poké Miles, the six
///     item/quantity pairs, the six gift entities, and raw .pl6 import/export.
///   - Hall of Fame: the 16 entries × 6 members, with per-entry clear index/date and delete.
/// Every tab edits the live <see cref="SaveFile"/> in memory (same model as
/// <see cref="Gen5ToolsWindow"/>); the owning panel's 💾 Save — or this window's "Save to Disk" —
/// writes to disk.
/// </summary>
public partial class Gen6ToolsWindow : Window
{
    private const decimal U32 = uint.MaxValue;
    private const decimal U16 = ushort.MaxValue;
    private const decimal U8 = byte.MaxValue;

    /// <summary>Stage record slots in <see cref="SuperTrainBlock"/> (48; the first 32 are released).</summary>
    private const int SuperTrainStages = 48;
    private const int SuperTrainReleased = 32;
    private const int SuperTrainDistribution = 6;
    private const int SuperTrainBags = 12;

    private const int PuffSlots = 100;

    private static readonly IReadOnlyList<Opt> GenderOpts = [new Opt("Male", 0), new Opt("Female", 1)];

    private static readonly IReadOnlyList<Opt> PuffOpts = BuildIndexedOpts(GameInfo.GetStrings("en").puffs, "(none)");
    private static readonly IReadOnlyList<Opt> BagOpts = BuildIndexedOpts(GameInfo.GetStrings("en").trainingbags, "---");

    /// <summary>Languages valid for Gen 6, as a label/value dropdown.</summary>
    private static readonly IReadOnlyList<Opt> LanguageOpts =
        GameInfo.LanguageDataSource(6, EntityContext.Gen6).Select(c => new Opt(c.Text, c.Value)).ToList();

    private readonly string? _loadedPath;
    private readonly Func<Task<string?>>? _persist;
    private readonly List<Action> _applyHooks = [];

    /// <summary>Game versions compatible with the loaded save, as a label/value dropdown.</summary>
    private readonly IReadOnlyList<Opt> _versionOpts;

    // Species/move/item dropdowns are filtered to what the loaded save can legally hold (matching the
    // WinForms editors, which all bind FilteredSources) — the global string tables run to Gen 9 and
    // would happily write an out-of-range id into a Gen 6 block.
    private readonly IReadOnlyList<Opt> _speciesOpts;
    private readonly IReadOnlyList<Opt> _moveOpts;
    private readonly IReadOnlyList<Opt> _itemOpts;

    private TabControl _tabs = null!;
    private TextBlock _status = null!;

    public Gen6ToolsWindow() : this(new SAV6AO(), null, null) { }

    public Gen6ToolsWindow(SaveFile sav, string? loadedPath, Func<Task<string?>>? persist)
    {
        AvaloniaXamlLoader.Load(this);
        _loadedPath = loadedPath;
        _persist = persist;

        var filtered = new FilteredGameDataSource(sav, GameInfo.Sources);
        _versionOpts = ToOpts(filtered.Games);
        _speciesOpts = ToOptsWithNone(filtered.Species);
        _moveOpts = ToOptsWithNone(filtered.Moves);
        _itemOpts = ToOptsWithNone(filtered.Items);

        _tabs = this.FindControl<TabControl>("RootTabs")!;
        _status = this.FindControl<TextBlock>("StatusText")!;
        var apply = this.FindControl<Button>("ApplyButton")!;
        var close = this.FindControl<Button>("CloseButton")!;
        apply.Content = persist is null ? "Apply & Close" : "Save to Disk";
        apply.Click += async (_, _) => await ApplyAndClose(persist is not null);
        close.Click += (_, _) => { Flush(); Close(); };

        ComboWheelGuard.Attach(this); // wheel over a closed combo must scroll the form, not edit it

        // The ORAS demo save (SAV6AODemo) implements only ISaveBlock6Core: it has no Puff / OPower /
        // SuperTrain / Link / HallOfFame / Maison blocks, so only the Trainer tab applies there.
        if (sav is SAV6 s6)
        {
            _tabs.Items.Add(new TabItem { Header = "Trainer", Content = BuildTrainerTab(s6) });
            if (s6 is ISaveBlock6Main m6)
            {
                _tabs.Items.Add(new TabItem { Header = "Poké Puffs", Content = BuildPuffTab(m6) });
                _tabs.Items.Add(new TabItem { Header = "O-Powers", Content = BuildOPowerTab(m6) });
                _tabs.Items.Add(new TabItem { Header = "Super Training", Content = BuildSuperTrainTab(m6) });
                _tabs.Items.Add(new TabItem { Header = "Pokémon Link", Content = BuildLinkTab(m6) });
                _tabs.Items.Add(new TabItem { Header = "Hall of Fame", Content = BuildHallOfFameTab(s6, m6) });
            }
        }

        if (_tabs.Items.Count != 0)
            _tabs.SelectedIndex = 0;
    }

    private void Flush() { foreach (var h in _applyHooks) h(); }

    private async Task ApplyAndClose(bool persist)
    {
        Flush();
        if (persist && _persist is not null)
        {
            var p = await _persist();
            _status.Text = p is null ? "Applied." : $"Saved → {p}";
        }
        Close();
    }

    // ---------------- Trainer ----------------

    private Control BuildTrainerTab(SAV6 sav)
    {
        var st = sav.Status;

        var left = new FieldForm();
        left.Header("Trainer");
        left.Text("OT Name", () => sav.OT, v => sav.OT = v, sav.MaxStringLengthTrainer);
        left.Combo("Gender", GenderOpts, () => sav.Gender, v => sav.Gender = (byte)v);
        left.Num("TID16", () => sav.TID16, v => sav.TID16 = (ushort)v, U16);
        left.Num("SID16", () => sav.SID16, v => sav.SID16 = (ushort)v, U16);
        left.Combo("Game", _versionOpts, () => (int)sav.Version, v => sav.Version = (GameVersion)v);
        left.Combo("Language", LanguageOpts, () => sav.Language, v => sav.Language = v);
        left.Num("Country", () => sav.Country, v => sav.Country = (byte)v, U8);
        left.Num("Region (subregion)", () => sav.Region, v => sav.Region = (byte)v, U8);
        left.Num("3DS Console Region", () => sav.ConsoleRegion, v => sav.ConsoleRegion = (byte)v, U8);
        left.Text("Game Sync ID (16 hex)", () => sav.GameSyncID, v => SetGameSyncID(sav, v), MyStatus6.GameSyncIDSize);

        left.Header("Progress");
        left.Num("Money", () => sav.Money, v => sav.Money = (uint)v, sav.MaxMoney);
        left.Num("Badges", () => sav.Badges, v => sav.Badges = (int)v, 8);
        left.Num("Battle Points", () => sav.BP, v => sav.BP = (int)v, 9999);
        left.Num("Poké Miles (current)", () => sav.GetRecord(63), v => sav.SetRecord(63, (int)v), sav.GetRecordMax(63));
        left.Num("Poké Miles (total earned)", () => sav.GetRecord(64), v => sav.SetRecord(64, (int)v), sav.GetRecordMax(64));
        left.Num("Vivillon Form", () => sav.Vivillon, v => sav.Vivillon = (int)v, 19);
        left.Check("Mega Evolution Unlocked", () => st.IsMegaEvolutionUnlocked, v => st.IsMegaEvolutionUnlocked = v);
        if (sav is SAV6AO)
            left.Check("Mega Rayquaza Unlocked", () => st.IsMegaRayquazaUnlocked, v => st.IsMegaRayquazaUnlocked = v);

        left.Header("Play Time");
        left.Num("Hours", () => sav.PlayedHours, v => sav.PlayedHours = (int)v, U16);
        left.Num("Minutes", () => sav.PlayedMinutes, v => sav.PlayedMinutes = (int)v, 59);
        left.Num("Seconds", () => sav.PlayedSeconds, v => sav.PlayedSeconds = (int)v, 59);
        left.Num("Seconds to Adventure Start", () => sav.SecondsToStart, v => sav.SecondsToStart = (uint)v, U32);
        left.Num("Seconds to Hall of Fame", () => sav.SecondsToFame, v => sav.SecondsToFame = (uint)v, U32);
        ReadOnlyRow(left, "Last Saved", () => sav.Played.LastSavedTime);

        if (sav is SAV6XY sxy)
        {
            var xy = sxy.Status; // MyStatus6XY — the XY-only appearance/nickname extras
            left.Header("XY Only");
            left.Text("Trainer Nickname", () => xy.Nickname, v => xy.Nickname = v, 12);
            left.Num("Eye Colour", () => xy.EyeColor, v => xy.EyeColor = (short)v, short.MaxValue, short.MinValue);
        }

        left.Load();
        _applyHooks.Add(left.Save);

        var right = new FieldForm();
        right.Header("Sayings (PSS / trainer card)");
        right.Text("Saying 1", () => st.Saying1, v => st.Saying1 = v, SayingLength);
        right.Text("Saying 2", () => st.Saying2, v => st.Saying2 = v, SayingLength);
        right.Text("Saying 3", () => st.Saying3, v => st.Saying3 = v, SayingLength);
        right.Text("Saying 4", () => st.Saying4, v => st.Saying4 = v, SayingLength);
        right.Text("Saying 5", () => st.Saying5, v => st.Saying5 = v, SayingLength);

        if (sav is ISaveBlock6Main main)
        {
            var maison = main.Maison;
            right.Header("Battle Maison");
            foreach (var style in Enum.GetValues<BattleStyle6>())
            {
                var s = style; // capture per-row
                right.Num($"{s} — Normal (current)", () => maison.GetMaisonStat(s, false, false), v => maison.SetMaisonStat(s, false, false, (ushort)v), U16);
                right.Num($"{s} — Normal (record)", () => maison.GetMaisonStat(s, true, false), v => maison.SetMaisonStat(s, true, false, (ushort)v), U16);
                right.Num($"{s} — Super (current)", () => maison.GetMaisonStat(s, false, true), v => maison.SetMaisonStat(s, false, true, (ushort)v), U16);
                right.Num($"{s} — Super (record)", () => maison.GetMaisonStat(s, true, true), v => maison.SetMaisonStat(s, true, true, (ushort)v), U16);
            }
        }

        right.Load();
        _applyHooks.Add(right.Save);

        return TwoColumns(new ScrollViewer { Content = left }, new ScrollViewer { Content = right });
    }

    /// <summary>Characters (not bytes) available in a <see cref="MyStatus6"/> saying slot.</summary>
    private const int SayingLength = 0x22 / 2;

    /// <summary>
    /// <see cref="MyStatus6.GameSyncID"/> throws on a wrong length (ArgumentOutOfRange) and again on a
    /// non-hex character (Convert.FromHexString → FormatException). Apply runs from an async void
    /// click handler, so an escaping exception kills the process — silently keep the stored ID instead
    /// of writing anything that isn't exactly 16 hex digits.
    /// </summary>
    private static void SetGameSyncID(SAV6 sav, string value)
    {
        var v = (value ?? string.Empty).Trim();
        if (v.Length != MyStatus6.GameSyncIDSize)
            return;
        foreach (var c in v)
        {
            if (!char.IsAsciiHexDigit(c))
                return;
        }
        sav.GameSyncID = v;
    }

    // ---------------- Poké Puffs ----------------

    private Control BuildPuffTab(ISaveBlock6Main sav)
    {
        var puff = sav.Puff;

        var f = new FieldForm();
        f.Header("Poké Puffs");
        f.Num($"Puff Count (max {PuffSlots})", () => puff.PuffCount, v => puff.PuffCount = (int)v, PuffSlots);
        for (int i = 0; i < PuffSlots; i++)
        {
            int idx = i; // capture per-row index
            f.Combo($"Slot {idx + 1}", PuffOpts,
                () => puff.GetPuffs()[idx],
                v => puff.GetPuffs()[idx] = (byte)v);
        }
        f.Load();
        _applyHooks.Add(f.Save);

        // Bulk ops write straight to the block, so reload the rows afterwards — otherwise the
        // registered Save() would put the pre-op values back on Apply.
        var giveVaried = Bulk("Give All (varied)", () => puff.MaxCheat(false), f);
        var giveBest = Bulk("Give All (best)", () => puff.MaxCheat(true), f);
        var reset = Bulk("Reset to Default", puff.Reset, f);
        var sort = Bulk("Sort", () => puff.Sort(), f);
        var sortRev = Bulk("Sort (reverse)", () => puff.Sort(true), f);

        return WithToolbar(new ScrollViewer { Content = f },
            "Give All (best) fills every slot with Supreme Wish/Honor puffs.",
            giveVaried, giveBest, reset, sort, sortRev);
    }

    // ---------------- O-Powers ----------------

    private Control BuildOPowerTab(ISaveBlock6Main sav)
    {
        var op = sav.OPower;

        var left = new FieldForm();
        left.Header("O-Power Unlocks");
        left.Num("Points", () => op.Points, v => op.Points = (byte)v, U8);
        foreach (var index in Enum.GetValues<OPower6Index>())
        {
            if (index == OPower6Index.Count)
                continue;
            var ix = index; // capture per-row
            left.Check(ix == OPower6Index.Enable ? "O-Powers Enabled" : SplitPascal(ix.ToString()),
                () => op.GetState(ix) == OPowerFlagState.Unlocked,
                v => op.SetState(ix, v ? OPowerFlagState.Unlocked : OPowerFlagState.Locked));
        }
        left.Load();
        _applyHooks.Add(left.Save);

        var right = new FieldForm();
        right.Header("Field Power Levels (0–3)");
        foreach (var type in Enum.GetValues<OPower6FieldType>())
        {
            if (type == OPower6FieldType.Count)
                continue;
            var t = type; // capture per-row
            right.Num($"{SplitPascal(t.ToString())} — Lv1", () => op.GetLevel1(t), v => op.SetLevel1(t, (byte)v), 3);
            right.Num($"{SplitPascal(t.ToString())} — Lv2", () => op.GetLevel2(t), v => op.SetLevel2(t, (byte)v), 3);
        }

        right.Header("Battle Power Levels (0–3)");
        foreach (var type in Enum.GetValues<OPower6BattleType>())
        {
            if (type == OPower6BattleType.Count)
                continue;
            var t = type; // capture per-row
            right.Num($"{SplitPascal(t.ToString())} — Lv1", () => op.GetLevel1(t), v => op.SetLevel1(t, (byte)v), 3);
            right.Num($"{SplitPascal(t.ToString())} — Lv2", () => op.GetLevel2(t), v => op.SetLevel2(t, (byte)v), 3);
        }
        right.Load();
        _applyHooks.Add(right.Save);

        var unlockAll = new Button { Content = "Unlock All" };
        unlockAll.Click += (_, _) => { op.UnlockAll(); left.Load(); right.Load(); };
        var clearAll = new Button { Content = "Clear All" };
        clearAll.Click += (_, _) => { op.ClearAll(); left.Load(); right.Load(); };

        var body = TwoColumns(new ScrollViewer { Content = left }, new ScrollViewer { Content = right });
        return WithToolbar(body, "Clear All keeps the master \"O-Powers Enabled\" flag.", unlockAll, clearAll);
    }

    // ---------------- Super Training ----------------

    private Action? _superTrainReload;

    private Control BuildSuperTrainTab(ISaveBlock6Main sav)
    {
        var st = sav.SuperTrain;
        var stageNames = GameInfo.GetStrings("en").trainingstage;

        var settings = new FieldForm();
        settings.Header("Super Training");
        settings.Num("Counter (max 10)", () => st.Counter, v => st.Counter = (byte)v, 10);
        settings.Num("Tutorial Index (max 6)", () => st.TutorialIndex, v => st.TutorialIndex = (uint)v, 6);

        settings.Header("Regimens Unlocked");
        for (int i = 0; i < SuperTrainReleased; i++)
        {
            int idx = i; // capture per-row
            settings.Check($"{idx + 1:00} — {StageName(stageNames, idx)}",
                () => st.GetIsRegimenUnlocked(idx), v => st.SetIsRegimenUnlocked(idx, v));
        }

        settings.Header("Distribution Regimens");
        for (int i = 0; i < SuperTrainDistribution; i++)
        {
            int idx = i; // capture per-row
            settings.Check($"Distribution #{idx + 1}",
                () => st.GetIsDistributionUnlocked(idx), v => st.SetIsDistributionUnlocked(idx, v));
        }

        settings.Header($"Training Bags ({SuperTrainBags})");
        for (int i = 0; i < SuperTrainBags; i++)
        {
            int idx = i; // capture per-row
            settings.Combo($"Bag {idx + 1}", BagOpts, () => st.GetBag(idx), v => st.SetBag(idx, (byte)v));
        }

        settings.Load();
        _applyHooks.Add(settings.Save);

        var unlock = new Button { Content = "Unlock All Stages" };
        unlock.Click += (_, _) => { st.UnlockAllStages(false); settings.Load(); };
        var unlockDist = new Button { Content = "Unlock All + Distribution" };
        unlockDist.Click += (_, _) => { st.UnlockAllStages(true); settings.Load(); };
        var clear = new Button { Content = "Clear Block" };
        clear.Click += (_, _) => { st.ClearBlock(); settings.Load(); _superTrainReload?.Invoke(); };

        var recordEditor = BuildSuperTrainRecords(st, stageNames);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var settingsPane = WithToolbar(new ScrollViewer { Content = settings },
            "Clear Block wipes every regimen flag, record and bag.", unlock, unlockDist, clear);
        Grid.SetColumn(settingsPane, 0);
        Grid.SetColumn(recordEditor, 1);
        grid.Children.Add(settingsPane);
        grid.Children.Add(recordEditor);
        return grid;
    }

    // stageNames is string[] rather than ReadOnlySpan<string>: this method builds lambdas and local
    // functions, and a ref struct parameter in that scope is a needless ref-safety hazard.
    private Control BuildSuperTrainRecords(SuperTrainBlock st, string[] stageNames)
    {
        var labels = new List<string>(SuperTrainStages);
        for (int i = 0; i < SuperTrainStages; i++)
            labels.Add($"#{i + 1:00}  {StageName(stageNames, i)}");

        var list = new ListBox { Width = 230, ItemsSource = labels };
        var host = new ScrollViewer();

        FieldForm? cur = null;
        int curIdx = -1;

        void SaveCur() { if (curIdx >= 0) cur?.Save(); }
        void Show(int idx)
        {
            SaveCur();
            curIdx = idx;
            if (idx < 0)
                return;
            int i = idx;
            var f = new FieldForm();
            f.Header($"Stage #{i + 1} Records");
            FloatRow(f, "Record 1 Time (s)", () => st.GetTime1(i), v => st.SetTime1(i, v));
            f.Combo("Holder 1 Species", _speciesOpts, () => st.GetHolder1(i).Species, v => { var h = st.GetHolder1(i); h.Species = (ushort)v; });
            f.Num("Holder 1 Form", () => st.GetHolder1(i).Form, v => { var h = st.GetHolder1(i); h.Form = (byte)v; }, 63);
            f.Num("Holder 1 Gender", () => st.GetHolder1(i).Gender, v => { var h = st.GetHolder1(i); h.Gender = (byte)v; }, 2);
            FloatRow(f, "Record 2 Time (s)", () => st.GetTime2(i), v => st.SetTime2(i, v));
            f.Combo("Holder 2 Species", _speciesOpts, () => st.GetHolder2(i).Species, v => { var h = st.GetHolder2(i); h.Species = (ushort)v; });
            f.Num("Holder 2 Form", () => st.GetHolder2(i).Form, v => { var h = st.GetHolder2(i); h.Form = (byte)v; }, 63);
            f.Num("Holder 2 Gender", () => st.GetHolder2(i).Gender, v => { var h = st.GetHolder2(i); h.Gender = (byte)v; }, 2);
            f.Load();
            host.Content = f;
            cur = f;
        }

        list.SelectionChanged += (_, _) => { if (list.SelectedIndex >= 0) Show(list.SelectedIndex); };
        _applyHooks.Add(SaveCur);

        // Called after Clear Block zeroes the backing data: drop the stale editor state so SaveCur
        // can't write the old record back, then re-show the current stage from fresh data.
        _superTrainReload = () =>
        {
            curIdx = -1;
            cur = null;
            list.SelectedIndex = -1;
            list.SelectedIndex = 0;
        };

        var clearRecord = new Button { Content = "Clear This Stage" };
        clearRecord.Click += (_, _) =>
        {
            int i = list.SelectedIndex;
            if (i < 0)
                return;
            st.ClearRecord1(i);
            st.ClearRecord2(i);
            curIdx = -1; // discard the stale form before re-reading
            cur = null;
            Show(i);
        };

        list.SelectedIndex = 0;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(list, 0);
        var right = WithToolbar(host, null, clearRecord);
        Grid.SetColumn(right, 1);
        grid.Children.Add(list);
        grid.Children.Add(right);
        return grid;
    }

    private static string StageName(ReadOnlySpan<string> names, int index) =>
        (uint)index < (uint)names.Length && names[index].Length != 0 ? names[index] : $"Stage {index + 1}";

    // ---------------- Pokémon Link ----------------

    private static readonly FilePickerFileType Pl6FileType = new("Pokémon Link Data (.pl6)")
    {
        Patterns = ["*.pl6"],
    };

    private Control BuildLinkTab(ISaveBlock6Main sav)
    {
        var block = sav.Link;
        var gifts = block.Gifts;

        var f = new FieldForm();
        f.Header("Pokémon Link");
        f.Check("Enabled", () => gifts.Enabled, v => gifts.Enabled = v);
        f.Text("Origin", () => gifts.Origin, v => gifts.Origin = v, 54);
        f.Num("Battle Points", () => gifts.BattlePoints, v => gifts.BattlePoints = (ushort)v, U16);
        f.Num("Poké Miles", () => gifts.Pokemiles, v => gifts.Pokemiles = (ushort)v, U16);

        f.Header("Items");
        f.Combo("Item 1", _itemOpts, () => gifts.Item1, v => gifts.Item1 = (ushort)v);
        f.Num("Quantity 1", () => gifts.Quantity1, v => gifts.Quantity1 = (ushort)v, U16);
        f.Combo("Item 2", _itemOpts, () => gifts.Item2, v => gifts.Item2 = (ushort)v);
        f.Num("Quantity 2", () => gifts.Quantity2, v => gifts.Quantity2 = (ushort)v, U16);
        f.Combo("Item 3", _itemOpts, () => gifts.Item3, v => gifts.Item3 = (ushort)v);
        f.Num("Quantity 3", () => gifts.Quantity3, v => gifts.Quantity3 = (ushort)v, U16);
        f.Combo("Item 4", _itemOpts, () => gifts.Item4, v => gifts.Item4 = (ushort)v);
        f.Num("Quantity 4", () => gifts.Quantity4, v => gifts.Quantity4 = (ushort)v, U16);
        f.Combo("Item 5", _itemOpts, () => gifts.Item5, v => gifts.Item5 = (ushort)v);
        f.Num("Quantity 5", () => gifts.Quantity5, v => gifts.Quantity5 = (ushort)v, U16);
        f.Combo("Item 6", _itemOpts, () => gifts.Item6, v => gifts.Item6 = (ushort)v);
        f.Num("Quantity 6", () => gifts.Quantity6, v => gifts.Quantity6 = (ushort)v, U16);

        f.Header("Gift Pokémon");
        var entities = new[] { gifts.Entity1, gifts.Entity2, gifts.Entity3, gifts.Entity4, gifts.Entity5, gifts.Entity6 };
        for (int i = 0; i < entities.Length; i++)
        {
            var e = entities[i]; // wraps the shared block memory; edits land in the save
            f.Combo($"Gift {i + 1} Species", _speciesOpts, () => e.Species, v => e.Species = (ushort)v);
            f.Num($"Gift {i + 1} Level", () => e.Level, v => e.Level = (byte)v, 100);
            f.Num($"Gift {i + 1} Form", () => e.Form, v => e.Form = (byte)v, 63);
            f.Check($"Gift {i + 1} Is Egg", () => e.IsEgg, v => e.IsEgg = v);
        }

        f.Load();
        // Field writes must land before the CRC is recomputed, so hook Save + checksum together.
        _applyHooks.Add(() => { f.Save(); block.RefreshChecksum(); });

        var status = new TextBlock { FontSize = 11, Opacity = 0.85 };

        var import = new Button { Content = "Import .pl6…" };
        import.Click += async (_, _) =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import Pokémon Link Data (.pl6)",
                    AllowMultiple = false,
                    FileTypeFilter = [Pl6FileType],
                    SuggestedStartLocation = await GetStartFolderAsync(),
                });
                if (files.Count == 0)
                    return;
                var file = files[0];

                byte[] data;
                await using (var stream = await file.OpenReadAsync())
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    data = ms.ToArray();
                }

                if (data.Length != PL6.Size)
                {
                    status.Text = $"Expected {PL6.Size} bytes, got {data.Length}.";
                    return;
                }

                data.AsSpan().CopyTo(block.PL6.Span);
                block.RefreshChecksum();
                f.Load();
                status.Text = $"Imported {data.Length} bytes from {file.Name} (in memory).";
            }
            catch (Exception ex) { status.Text = "Import failed: " + ex.Message; }
        };

        var export = new Button { Content = "Export .pl6…" };
        export.Click += async (_, _) =>
        {
            try
            {
                f.Save(); // export what's on screen, not the pre-edit block
                block.RefreshChecksum();
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export Pokémon Link Data (.pl6)",
                    SuggestedFileName = "link.pl6",
                    DefaultExtension = "pl6",
                    FileTypeChoices = [Pl6FileType],
                    SuggestedStartLocation = await GetStartFolderAsync(),
                });
                if (file is null)
                    return;

                var bytes = block.PL6.Span.ToArray();
                await using (var stream = await file.OpenWriteAsync())
                    await stream.WriteAsync(bytes);
                status.Text = $"Exported {bytes.Length} bytes → {file.Name}";
            }
            catch (Exception ex) { status.Text = "Export failed: " + ex.Message; }
        };

        var refresh = new Button { Content = "Refresh Checksum" };
        refresh.Click += (_, _) =>
        {
            f.Save();
            block.RefreshChecksum();
            status.Text = $"Checksum → 0x{block.Checksum:X4}";
        };

        var pane = WithToolbar(new ScrollViewer { Content = f },
            "The block checksum is refreshed automatically on Apply.", import, export, refresh);
        var outer = new DockPanel();
        DockPanel.SetDock(status, Dock.Bottom);
        status.Margin = new Thickness(10, 4, 10, 0);
        outer.Children.Add(status);
        outer.Children.Add(pane);
        return outer;
    }

    // ---------------- Hall of Fame ----------------

    private Action? _fameReload;

    private Control BuildHallOfFameTab(SAV6 sav, ISaveBlock6Main main)
    {
        var fame = main.HallOfFame;

        var entryList = new ListBox { Width = 190, ItemsSource = FameEntryLabels(fame) };
        var memberList = new ListBox { Width = 150 };
        var host = new ScrollViewer();

        FieldForm? cur = null;
        int curEntry = -1, curMember = -1;

        void SaveCur() { if (curEntry >= 0 && curMember >= 0) cur?.Save(); }

        void ShowMember(int entry, int member)
        {
            SaveCur();
            curEntry = entry;
            curMember = member;
            if (entry < 0 || member < 0)
                return;

            int t = entry, m = member;
            var f = new FieldForm();

            f.Header($"Entry #{t + 1} — Details");
            f.Num("Clear Index", () => new HallFame6Index(fame.GetEntry(t)[^4..]).ClearIndex,
                v => { var ix = new HallFame6Index(fame.GetEntry(t)[^4..]); ix.ClearIndex = (uint)v; }, 0x3FFF);
            f.Num("Year (2000 + n)", () => new HallFame6Index(fame.GetEntry(t)[^4..]).Year,
                v => { var ix = new HallFame6Index(fame.GetEntry(t)[^4..]); ix.Year = (uint)v; }, U8);
            f.Num("Month", () => new HallFame6Index(fame.GetEntry(t)[^4..]).Month,
                v => { var ix = new HallFame6Index(fame.GetEntry(t)[^4..]); ix.Month = (uint)v; }, 12);
            f.Num("Day", () => new HallFame6Index(fame.GetEntry(t)[^4..]).Day,
                v => { var ix = new HallFame6Index(fame.GetEntry(t)[^4..]); ix.Day = (uint)v; }, 31);
            f.Check("Has Data", () => new HallFame6Index(fame.GetEntry(t)[^4..]).HasData,
                v => { var ix = new HallFame6Index(fame.GetEntry(t)[^4..]); ix.HasData = v; });

            f.Header($"Member #{m + 1}");
            f.Combo("Species", _speciesOpts, () => Fame(fame, sav, t, m).Species,
                v => { var e = Fame(fame, sav, t, m); e.Species = (ushort)v; });
            f.Num("Form", () => Fame(fame, sav, t, m).Form,
                v => { var e = Fame(fame, sav, t, m); e.Form = (byte)v; }, 31);
            f.Num("Gender (0=M,1=F,2=—)", () => Fame(fame, sav, t, m).Gender,
                v => { var e = Fame(fame, sav, t, m); e.Gender = (uint)v; }, 3);
            f.Num("Level", () => Fame(fame, sav, t, m).Level,
                v => { var e = Fame(fame, sav, t, m); e.Level = (uint)v; }, 127);
            f.Combo("Held Item", _itemOpts, () => Fame(fame, sav, t, m).HeldItem,
                v => { var e = Fame(fame, sav, t, m); e.HeldItem = (ushort)v; });
            f.Combo("Move 1", _moveOpts, () => Fame(fame, sav, t, m).Move1,
                v => { var e = Fame(fame, sav, t, m); e.Move1 = (ushort)v; });
            f.Combo("Move 2", _moveOpts, () => Fame(fame, sav, t, m).Move2,
                v => { var e = Fame(fame, sav, t, m); e.Move2 = (ushort)v; });
            f.Combo("Move 3", _moveOpts, () => Fame(fame, sav, t, m).Move3,
                v => { var e = Fame(fame, sav, t, m); e.Move3 = (ushort)v; });
            f.Combo("Move 4", _moveOpts, () => Fame(fame, sav, t, m).Move4,
                v => { var e = Fame(fame, sav, t, m); e.Move4 = (ushort)v; });
            f.Check("Shiny", () => Fame(fame, sav, t, m).IsShiny,
                v => { var e = Fame(fame, sav, t, m); e.IsShiny = v; });
            f.Check("Nicknamed", () => Fame(fame, sav, t, m).IsNicknamed,
                v => { var e = Fame(fame, sav, t, m); e.IsNicknamed = v; });
            f.Text("Nickname", () => Fame(fame, sav, t, m).Nickname,
                v => { var e = Fame(fame, sav, t, m); e.Nickname = v; }, 12);
            f.Text("OT Name", () => Fame(fame, sav, t, m).OriginalTrainerName,
                v => { var e = Fame(fame, sav, t, m); e.OriginalTrainerName = v; }, 12);
            f.Num("OT Gender (0=M,1=F)", () => Fame(fame, sav, t, m).OriginalTrainerGender,
                v => { var e = Fame(fame, sav, t, m); e.OriginalTrainerGender = (uint)v; }, 1);
            f.Num("TID16", () => Fame(fame, sav, t, m).TID16,
                v => { var e = Fame(fame, sav, t, m); e.TID16 = (ushort)v; }, U16);
            f.Num("SID16", () => Fame(fame, sav, t, m).SID16,
                v => { var e = Fame(fame, sav, t, m); e.SID16 = (ushort)v; }, U16);
            f.Num("Encryption Constant", () => Fame(fame, sav, t, m).EncryptionConstant,
                v => { var e = Fame(fame, sav, t, m); e.EncryptionConstant = (uint)v; }, U32);

            f.Load();
            host.Content = f;
            cur = f;
        }

        void ShowEntry(int entry)
        {
            // Replacing ItemsSource resets SelectedIndex to -1, so assigning 0 always raises
            // SelectionChanged — ShowMember runs from the handler, exactly once.
            memberList.ItemsSource = MemberLabels(fame, sav, entry);
            memberList.SelectedIndex = entry >= 0 ? 0 : -1;
        }

        entryList.SelectionChanged += (_, _) =>
        {
            if (entryList.SelectedIndex < 0)
                return;
            SaveCur();
            curEntry = -1;
            curMember = -1;
            cur = null;
            ShowEntry(entryList.SelectedIndex);
        };
        memberList.SelectionChanged += (_, _) =>
        {
            if (entryList.SelectedIndex >= 0 && memberList.SelectedIndex >= 0)
                ShowMember(entryList.SelectedIndex, memberList.SelectedIndex);
        };

        _applyHooks.Add(SaveCur);

        // ClearEntry shifts every later entry down, so all cached editor state is stale afterwards.
        _fameReload = () =>
        {
            curEntry = -1;
            curMember = -1;
            cur = null;
            int keep = entryList.SelectedIndex;
            entryList.ItemsSource = FameEntryLabels(fame);
            entryList.SelectedIndex = -1;
            entryList.SelectedIndex = keep >= 0 ? keep : 0;
        };

        var del = new Button { Content = "Delete Entry" };
        del.Click += (_, _) =>
        {
            int i = entryList.SelectedIndex;
            if (i < 0)
                return;
            curEntry = -1; // discard the on-screen form before the shift-down
            curMember = -1;
            cur = null;
            fame.ClearEntry(i);
            _fameReload?.Invoke();
        };

        // The two list panes are label snapshots: editing a species or an entry date doesn't update
        // them. Refreshing from inside SaveCur would re-enter the SelectionChanged handler that calls
        // it, so this is an explicit button — commit the open form first, then relabel.
        var relabel = new Button { Content = "Refresh Labels" };
        relabel.Click += (_, _) =>
        {
            SaveCur();
            _fameReload?.Invoke();
        };

        entryList.SelectedIndex = 0;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        Grid.SetColumn(entryList, 0);
        Grid.SetColumn(memberList, 1);
        var right = WithToolbar(host,
            "Deleting an entry shifts the later entries down, as the game does.", del, relabel);
        Grid.SetColumn(right, 2);
        grid.Children.Add(entryList);
        grid.Children.Add(memberList);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>
    /// Builds a fresh view over the entity's bytes. <see cref="HallFame6Entity"/> is a ref struct, so
    /// it can't be cached in a field or captured by a lambda — every access re-wraps the save memory.
    /// </summary>
    private static HallFame6Entity Fame(HallOfFame6 fame, SAV6 sav, int team, int member)
        => new(fame.GetEntity(team, member), sav.Language);

    private static List<string> FameEntryLabels(HallOfFame6 fame)
    {
        var list = new List<string>(HallOfFame6.Entries);
        for (int i = 0; i < HallOfFame6.Entries; i++)
        {
            var ix = new HallFame6Index(fame.GetEntry(i)[^4..]);
            list.Add(ix.HasData
                ? $"#{i + 1:00}  clear {ix.ClearIndex}  {2000 + ix.Year:0000}-{ix.Month:00}-{ix.Day:00}"
                : $"#{i + 1:00}  (empty)");
        }
        return list;
    }

    private static List<string> MemberLabels(HallOfFame6 fame, SAV6 sav, int entry)
    {
        var list = new List<string>(HallOfFame6.PokeCount);
        if (entry < 0)
            return list;
        var names = GameInfo.GetStrings("en").specieslist;
        for (int m = 0; m < HallOfFame6.PokeCount; m++)
        {
            var species = Fame(fame, sav, entry, m).Species;
            var name = species != 0 && species < names.Length ? names[species] : "—";
            list.Add($"{m + 1}. {name}");
        }
        return list;
    }

    // ---------------- shared helpers ----------------

    private static Control TwoColumns(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Docks a button row (and optional hint text) beneath a form body.</summary>
    private static Control WithToolbar(Control body, string? hint, params Button[] buttons)
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
            row.Children.Add(new TextBlock { Text = hint, FontSize = 11, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center });

        var dock = new DockPanel();
        var wrap = new Border
        {
            Child = row,
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)),
        };
        DockPanel.SetDock(wrap, Dock.Bottom);
        dock.Children.Add(wrap);
        dock.Children.Add(body);
        return dock;
    }

    /// <summary>A button that runs a bulk block mutation then re-reads the form from the new data.</summary>
    private static Button Bulk(string label, Action op, FieldForm form)
    {
        var b = new Button { Content = label };
        b.Click += (_, _) => { op(); form.Load(); };
        return b;
    }

    /// <summary>
    /// Float row. <see cref="FieldForm.Num"/> is integral (FormatString "0"), which would truncate
    /// Super Training record times, so these use a text field with invariant parsing.
    /// </summary>
    private static void FloatRow(FieldForm f, string label, Func<float> get, Action<float> set)
        => f.Custom(label,
            () => get().ToString("0.###", CultureInfo.InvariantCulture),
            s =>
            {
                if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    set(v);
            }, 160);

    /// <summary>Read-only display row (no write-back on Save).</summary>
    private static void ReadOnlyRow(FieldForm f, string label, Func<string> get)
    {
        var tb = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        f.CustomControl(label, tb, load: () => tb.Text = get(), save: () => { });
    }

    private static IReadOnlyList<Opt> ToOpts(IReadOnlyList<ComboItem> items)
        => items.Select(c => new Opt(c.Text, c.Value)).ToList();

    /// <summary>
    /// Save-filtered dropdown, guaranteeing a value-0 "(none)" entry — an id of 0 is the legitimate
    /// "empty" state for a Hall of Fame member, a Super Training record holder and a Link gift/item,
    /// but the filtered sources don't always include it.
    /// </summary>
    private static IReadOnlyList<Opt> ToOptsWithNone(IReadOnlyList<ComboItem> items)
    {
        var list = new List<Opt>(items.Count + 1);
        if (!items.Any(c => c.Value == 0))
            list.Add(new Opt("(none)", 0));
        list.AddRange(items.Select(c => new Opt(c.Text, c.Value)));
        return list;
    }

    /// <summary>Builds a "000 Name" option per array index, skipping blanks; index 0 gets <paramref name="zero"/>.</summary>
    private static IReadOnlyList<Opt> BuildIndexedOpts(ReadOnlySpan<string> names, string zero)
    {
        var list = new List<Opt>(names.Length) { new($"000 {zero}", 0) };
        for (int i = 1; i < names.Length; i++)
        {
            var n = names[i];
            if (!string.IsNullOrEmpty(n))
                list.Add(new Opt($"{i:000} {n}", i));
        }
        return list;
    }

    /// <summary>"SpecialAttack1" → "Special Attack 1", for enum-derived row labels.</summary>
    private static string SplitPascal(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 8);
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

    private async Task<IStorageFolder?> GetStartFolderAsync()
    {
        var dir = _loadedPath is not null ? Path.GetDirectoryName(_loadedPath) : null;
        if (string.IsNullOrEmpty(dir))
            return null;
        return await StorageProvider.TryGetFolderFromPathAsync(dir);
    }
}
