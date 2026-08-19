using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
/// Gen 5 save tools. Hosts a tab per available feature:
///   - Join Avenue (BW2 only): full editor for settings, self, visitors, occupants, fans, assistants.
///   - Memory Link (all Gen5): import/export of the two raw 0x400-byte link blocks.
///   - Global Link (all Gen5): Dream World / Game Sync block — account flags, downloaded content,
///     20-slot item list, and the 5 Dream World furniture slots.
/// Edits are applied to the live <see cref="SaveFile"/> in memory; the owning panel's Save button
/// (or this window's "Save to Disk") writes to disk.
/// </summary>
public partial class Gen5ToolsWindow : Window
{
    private const decimal U32 = uint.MaxValue;
    private const decimal U16 = ushort.MaxValue;
    private const decimal U8 = byte.MaxValue;

    private enum Kind { Visitor, Fan, Assistant }

    private static readonly IReadOnlyList<Opt> ShopTypeOpts =
        Enum.GetValues<JoinAvenueShopType5>().Select(z => new Opt(z.ToString(), (int)z)).ToList();

    private static readonly IReadOnlyList<Opt> CeilingOpts =
        Enum.GetValues<JoinAvenueCeilingColor5>().Select(z => new Opt(z.ToString(), (int)z)).ToList();

    private static readonly IReadOnlyList<Opt> OriginOpts = [new Opt("NPC", 0), new Opt("Human Player", 1)];

    private static readonly IReadOnlyList<Opt> SpeciesOpts = BuildSpeciesOpts();

    /// <summary>Species list including index 0 as "(none)" — Entrée slots use 0 for an empty slot.</summary>
    private static readonly IReadOnlyList<Opt> EntreeSpeciesOpts = BuildEntreeSpeciesOpts();

    /// <summary>Move list including index 0 as "(none)".</summary>
    private static readonly IReadOnlyList<Opt> MoveOpts = BuildMoveOpts();

    private static readonly IReadOnlyList<Opt> AnimationOpts =
        Enum.GetValues<EntreeForestAnimation>().Select(z => new Opt(z.ToString(), (int)z)).ToList();

    /// <summary>Languages valid for Gen 5 (no Chinese+), as a label/value dropdown.</summary>
    private static readonly IReadOnlyList<Opt> LanguageOpts =
        GameInfo.LanguageDataSource(5, EntityContext.Gen5).Select(c => new Opt(c.Text, c.Value)).ToList();

    private readonly SaveFile _sav;
    private readonly string? _loadedPath;
    private readonly Func<Task<string?>>? _persist;
    private readonly List<Action> _applyHooks = [];

    /// <summary>Game versions compatible with the loaded save, as a label/value dropdown.</summary>
    private readonly IReadOnlyList<Opt> _versionOpts;

    private TabControl _tabs = null!;
    private TextBlock _status = null!;

    public Gen5ToolsWindow() : this(new SAV5B2W2(), null, null) { }

    public Gen5ToolsWindow(SaveFile sav, string? loadedPath, Func<Task<string?>>? persist)
    {
        AvaloniaXamlLoader.Load(this);
        _sav = sav;
        _loadedPath = loadedPath;
        _persist = persist;
        _versionOpts = new FilteredGameDataSource(sav, GameInfo.Sources).Games
            .Select(c => new Opt(c.Text, c.Value)).ToList();

        _tabs = this.FindControl<TabControl>("RootTabs")!;
        _status = this.FindControl<TextBlock>("StatusText")!;
        var apply = this.FindControl<Button>("ApplyButton")!;
        var close = this.FindControl<Button>("CloseButton")!;
        apply.Content = persist is null ? "Apply & Close" : "Save to Disk";
        apply.Click += async (_, _) => await ApplyAndClose(persist is not null);
        close.Click += (_, _) => { Flush(); Close(); };

        ComboWheelGuard.Attach(this); // wheel over a closed combo must scroll the form, not edit it

        if (sav is SAV5B2W2 b2)
            _tabs.Items.Add(new TabItem { Header = "Join Avenue", Content = BuildJoinAvenueTab(b2) });
        if (sav is SAV5 g5)
        {
            _tabs.Items.Add(new TabItem { Header = "Memory Link", Content = BuildMemoryLinkTab(g5) });
            _tabs.Items.Add(new TabItem { Header = "Global Link", Content = BuildGlobalLinkTab(g5) });
            _tabs.Items.Add(new TabItem { Header = "Entrée Forest", Content = BuildEntreeTab(g5) });
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

    // ---------------- Join Avenue ----------------

    private Control BuildJoinAvenueTab(SAV5B2W2 sav)
    {
        var av = sav.JoinAvenue;

        var top = new FieldForm();
        top.Check("Script Flag", () => av.ScriptFlag, v => av.ScriptFlag = v);
        top.Num("Visitor Count", () => av.CountVisitor, v => av.CountVisitor = (uint)v, U32);
        top.Num("Fan Count", () => av.CountFan, v => av.CountFan = (uint)v, U32);
        top.Load();
        _applyHooks.Add(top.Save);

        var sub = new TabControl();

        var setForm = new FieldForm();
        BuildSettings(setForm, av.Settings);
        setForm.Load();
        _applyHooks.Add(setForm.Save);
        sub.Items.Add(new TabItem { Header = "Settings", Content = new ScrollViewer { Content = setForm } });

        sub.Items.Add(new TabItem { Header = "Self", Content = BuildSingleEntity(av.Self, Kind.Visitor) });
        sub.Items.Add(new TabItem { Header = "Visitors", Content = BuildList(JoinAvenue5.VisitorCount, i => av.GetVisitor(i), Kind.Visitor) });
        sub.Items.Add(new TabItem { Header = "Occupants", Content = BuildList(JoinAvenue5.OccupantCount, i => av.GetOccupant(i), Kind.Visitor) });
        sub.Items.Add(new TabItem { Header = "Fans", Content = BuildList(JoinAvenue5.FanCount, i => av.GetFan(i), Kind.Fan) });
        sub.Items.Add(new TabItem { Header = "Assistants", Content = BuildList(JoinAvenue5.AssistantCount, i => av.GetAssistant(i), Kind.Assistant) });

        var dock = new DockPanel();
        var topWrap = new Border { Child = top, BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)) };
        DockPanel.SetDock(topWrap, Dock.Top);
        dock.Children.Add(topWrap);
        dock.Children.Add(sub);
        return dock;
    }

    private static void BuildSettings(FieldForm f, JoinAvenueSettings5 s)
    {
        f.Header("Avenue Settings");
        f.Text("Avenue Name", () => s.Name, v => s.Name = v, 20);
        f.Text("Player Title", () => s.PlayerTitle, v => s.PlayerTitle = v, 20);
        f.Num("Experience", () => s.Experience, v => s.Experience = (uint)v, U32);
        f.Num("Rank (max 9999)", () => s.Rank, v => s.Rank = (ushort)v, JoinAvenueSettings5.MaxAvenueRank);
        f.Combo("Ceiling Color", CeilingOpts, () => (int)s.CeilingColor, v => s.CeilingColor = (JoinAvenueCeilingColor5)v);
        f.Num("Flags", () => s.Flags, v => s.Flags = (uint)v, U32);
        f.Num("Visiting Player Count (max 32)", () => s.VisitingPlayerDatabaseCount, v => s.VisitingPlayerDatabaseCount = (ushort)v, JoinAvenueSettings5.CountVisitingPlayersRemembered);
        f.Num("Visiting Player Insert Index", () => s.VistiingPlayerDatabaseInsertIndex, v => s.VistiingPlayerDatabaseInsertIndex = (ushort)v, JoinAvenueSettings5.CountVisitingPlayersRemembered - 1);
        f.Num("Seed", () => s.Seed, v => s.Seed = (uint)v, U32);
        f.Num("Promotion Days Elapsed", () => s.PromotionDaysElapsed, v => s.PromotionDaysElapsed = (ushort)v, U16);
        f.Check("Promotion Active", () => s.IsPromotionActive, v => s.IsPromotionActive = v);

        var db = new TextBox { AcceptsReturn = true, Height = 200, Width = 220, FontFamily = new FontFamily("monospace") };
        f.CustomControl("Visiting Players (tid,sid per line)", db,
            load: () =>
            {
                var sb = new StringBuilder();
                for (int i = 0; i < JoinAvenueSettings5.CountVisitingPlayersRemembered; i++)
                {
                    var val = s.GetVisitingPlayerTrainerID(i);
                    sb.Append((ushort)val).Append(',').Append((ushort)(val >> 16)).Append('\n');
                }
                db.Text = sb.ToString();
            },
            save: () =>
            {
                var lines = (db.Text ?? "").Replace("\r", "").Split('\n');
                for (int i = 0; i < JoinAvenueSettings5.CountVisitingPlayersRemembered; i++)
                {
                    ushort tid = 0, sid = 0;
                    if (i < lines.Length)
                    {
                        var parts = lines[i].Split(',', ';');
                        if (parts.Length > 0) ushort.TryParse(parts[0].Trim(), out tid);
                        if (parts.Length > 1) ushort.TryParse(parts[1].Trim(), out sid);
                    }
                    s.SetVisitingPlayerTrainerID(i, tid | ((uint)sid << 16));
                }
            });
    }

    private Control BuildSingleEntity(IJoinAvenueEntity5 e, Kind k)
    {
        var g = new FieldForm();
        BuildGeneral(g, e);
        g.Load();
        var sp = BuildSpecific(e, k);
        sp.Load();
        _applyHooks.Add(() => { g.Save(); sp.Save(); });
        return TwoColumns(new ScrollViewer { Content = g }, new ScrollViewer { Content = sp });
    }

    private Control BuildList(int count, Func<int, IJoinAvenueEntity5> getter, Kind k)
    {
        var list = new ListBox { Width = 80, ItemsSource = Enumerable.Range(1, count).Select(i => $"#{i}").ToList() };
        var genHost = new ScrollViewer();
        var spHost = new ScrollViewer();

        FieldForm? g = null, sp = null;
        int cur = -1;

        void SaveCur() { if (cur >= 0) { g?.Save(); sp?.Save(); } }
        void Show(int idx)
        {
            SaveCur();
            cur = idx;
            if (idx < 0) return;
            var e = getter(idx);
            g = new FieldForm();
            BuildGeneral(g, e);
            g.Load();
            genHost.Content = g;
            sp = BuildSpecific(e, k);
            sp.Load();
            spHost.Content = sp;
        }

        list.SelectionChanged += (_, _) => { if (list.SelectedIndex >= 0) Show(list.SelectedIndex); };
        _applyHooks.Add(SaveCur);
        if (count > 0) list.SelectedIndex = 0;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*") };
        Grid.SetColumn(list, 0);
        Grid.SetColumn(genHost, 1);
        Grid.SetColumn(spHost, 2);
        grid.Children.Add(list);
        grid.Children.Add(genHost);
        grid.Children.Add(spHost);
        return grid;
    }

    private static Control TwoColumns(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private FieldForm BuildSpecific(IJoinAvenueEntity5 e, Kind k)
    {
        var f = new FieldForm();
        switch (k)
        {
            case Kind.Visitor: BuildVisitorSpecific(f, (JoinAvenueVisitor5)e); break;
            case Kind.Fan: BuildFanSpecific(f, (JoinAvenueFan5)e); break;
            case Kind.Assistant: BuildAssistantSpecific(f, (JoinAvenueAssistant5)e); break;
        }
        return f;
    }

    private void BuildGeneral(FieldForm f, IJoinAvenueEntity5 e)
    {
        f.Header("General");
        f.Text("Name", () => e.Name, v => e.Name = v, 7);
        f.Num("Country", () => e.Country, v => e.Country = (byte)v, U8);
        f.Num("Subregion", () => e.Subregion, v => e.Subregion = (byte)v, U8);
        f.Text("Shout", () => e.Shout, v => e.Shout = v, 8);
        f.Combo("Version", _versionOpts, () => e.Version, v => e.Version = (byte)v);
        f.Combo("Language", LanguageOpts, () => e.Language, v => e.Language = (byte)v);
        f.Num("Unknown22", () => e.Unknown22, v => e.Unknown22 = (byte)v, 15);
        f.Num("Gender", () => e.Gender, v => e.Gender = (byte)v, 15);
        f.Num("Unused23", () => e.Unused23, v => e.Unused23 = (byte)v, U8);
        f.Num("TID16", () => e.TID16, v => e.TID16 = (ushort)v, U16);
        f.Num("Unknown26", () => e.Unknown26, v => e.Unknown26 = (byte)v, U8);
        f.Num("Unknown27", () => e.Unknown27, v => e.Unknown27 = (byte)v, U8);
        f.Num("Played Hours", () => e.PlayedHours, v => e.PlayedHours = (ushort)v, 1023);
        f.Num("Played Minutes", () => e.PlayedMinutes, v => e.PlayedMinutes = (byte)v, 63);
        f.Num("Sprite", () => e.Sprite, v => e.Sprite = (ushort)v, U16);
        f.Text("Greeting", () => e.Greeting, v => e.Greeting = v, 8);
        f.Text("Farewell", () => e.Farewell, v => e.Farewell = v, 8);
        f.Num("Met Year", () => e.MetYear, v => e.MetYear = (byte)v, U8);
        f.Num("Met Month", () => e.MetMonth, v => e.MetMonth = (byte)v, U8);
        f.Num("Met Day", () => e.MetDay, v => e.MetDay = (byte)v, U8);
        f.Num("Seed", () => e.Seed, v => e.Seed = (uint)v, U32);
    }

    private static void BuildVisitorSpecific(FieldForm f, JoinAvenueVisitor5 e)
    {
        f.Header("Shop / Visitor");
        f.Check("IsFlag2C", () => e.IsFlag2C, v => e.IsFlag2C = v);
        f.Num("Join Avenue Level", () => e.JoinAvenueLevel, v => e.JoinAvenueLevel = (byte)v, 127);
        f.Num("Unused2D", () => e.Unused2D, v => e.Unused2D = (byte)v, U8);
        f.Combo("Desired Shop Type", ShopTypeOpts, () => e.DesiredShopType, v => e.DesiredShopType = (ushort)v);
        f.Custom("Shop Counts (8, max 15)", () => ShopCountsStr(e), s => SetShopCounts(e, s));
        f.Num("Dex Seen", () => e.DexSeen, v => e.DexSeen = (ushort)v, 1023);
        f.Combo("Favorite Species", SpeciesOpts, () => e.FavoriteSpecies, v => e.FavoriteSpecies = (ushort)v);
        f.Num("Medal Rank", () => e.MedalRank, v => e.MedalRank = (byte)v, U8);
        f.Num("Medal Hint", () => e.MedalHint, v => e.MedalHint = (byte)v, U8);
        f.Num("Medal Count", () => e.MedalCount, v => e.MedalCount = (byte)v, U8);
        f.Date("Date1", () => e.Date1.Date, d => e.Date1 = MakeDate(d));
        f.Date("Date Adventure Start", () => e.DateAdventureStart.Date, d => e.DateAdventureStart = MakeDate(d));
        f.Date("Date Hall of Fame", () => e.DateHallOfFame.Date, d => e.DateHallOfFame = MakeDate(d));
        f.Custom("Records (8 uint)", () => RecordsStr(e), s => SetRecords(e, s), 340);
        f.Custom("Trivia (16 byte)", () => TriviaStr(e), s => SetTrivia(e, s), 340);
        f.Custom("Activities (4 byte)", () => ActivitiesStr(e), s => SetActivities(e, s));
        f.Custom("Activity Dates (4 raw)", () => ActivityDatesStr(e), s => SetActivityDates(e, s));
        f.Combo("Origin", OriginOpts, () => e.Origin, v => e.Origin = (ushort)v);
        f.Num("Met Hour", () => e.MetHour, v => e.MetHour = (byte)v, U8);
        f.Num("Met Minute", () => e.MetMinute, v => e.MetMinute = (byte)v, U8);
        f.Num("UnknownA8", () => e.UnknownA8, v => e.UnknownA8 = (byte)v, U8);
        f.Check("Shop Change Allowed", () => e.IsShopChangeAllowed, v => e.IsShopChangeAllowed = v);
        f.Check("FlagA9_1", () => e.IsFlagA9_1, v => e.IsFlagA9_1 = v);
        f.Check("FlagA9_2", () => e.IsFlagA9_2, v => e.IsFlagA9_2 = v);
        f.Check("Interacted Today", () => e.IsInteractedToday, v => e.IsInteractedToday = v);
        f.Check("FlagAA", () => e.IsFlagAA, v => e.IsFlagAA = v);
        f.Num("Join Avenue Rank", () => e.JoinAvenueRank, v => e.JoinAvenueRank = (byte)v, U8);
        f.Num("UnknownAC", () => e.UnknownAC, v => e.UnknownAC = (byte)v, U8);
        f.Num("Shop Level (max 10)", () => e.ShopLevel, v => e.ShopLevel = (byte)v, JoinAvenueVisitor5.ShopMaxLevel);
        f.Num("Shop Experience", () => e.ShopExperience, v => e.ShopExperience = (ushort)v, U16);
        f.Num("IsInventory", () => e.IsInventory, v => e.IsInventory = (uint)v, U32);
        f.Combo("Shop Type", ShopTypeOpts, () => (int)e.ShopType, v => e.ShopType = (JoinAvenueShopType5)v);
        f.Num("Shop Work", () => e.ShopWork, v => e.ShopWork = (ushort)v, U16);
        f.Num("UnusedB8", () => e.UnusedB8, v => e.UnusedB8 = (uint)v, U32);
        f.Num("UnknownBits0_8", () => e.UnknownBits0_8, v => e.UnknownBits0_8 = (ushort)v, 511);
        f.Check("UnknownBit9", () => e.IsUnknownBits9, v => e.IsUnknownBits9 = v);
        f.Num("UnknownBits10", () => e.UnknownBits10, v => e.UnknownBits10 = (byte)v, 7);
        f.Num("UnknownBits13_20", () => e.UnknownBits13_20, v => e.UnknownBits13_20 = (byte)v, U8);
        f.Num("UnknownBits21_27", () => e.UnknownBits21_27, v => e.UnknownBits21_27 = (byte)v, 127);
        f.Num("UnknownBits28_31", () => e.UnknownBits28_31, v => e.UnknownBits28_31 = (byte)v, 15);
    }

    private static void BuildFanSpecific(FieldForm f, JoinAvenueFan5 e)
    {
        f.Header("Fan");
        f.Num("Unknown4C", () => e.Unknown4C, v => e.Unknown4C = (byte)v, U8);
        f.Num("Unknown4D", () => e.Unknown4D, v => e.Unknown4D = (byte)v, U8);
        f.Num("Unknown4F", () => e.Unknown4F, v => e.Unknown4F = (byte)v, U8);
        f.Check("Interacted Today", () => e.IsInteractedToday, v => e.IsInteractedToday = v);
        f.Combo("Species", SpeciesOpts, () => e.Species, v => e.Species = (ushort)v);
        f.Num("Unknown52", () => e.Unknown52, v => e.Unknown52 = (ushort)v, U16);
        f.Num("Unknown54", () => e.Unknown54, v => e.Unknown54 = (byte)v, U8);
        f.Num("Bubble Target", () => e.BubbleTarget, v => e.BubbleTarget = (byte)v, U8);
        f.Num("Unknown56", () => e.Unknown56, v => e.Unknown56 = (byte)v, U8);
        f.Num("Unknown5A", () => e.Unknown5A, v => e.Unknown5A = (ushort)v, U16);
    }

    private static void BuildAssistantSpecific(FieldForm f, JoinAvenueAssistant5 e)
    {
        f.Header("Assistant");
        f.Num("Position0", () => e.Position0, v => e.Position0 = (byte)v, U8);
        f.Num("Position1", () => e.Position1, v => e.Position1 = (byte)v, U8);
        f.Num("Position2", () => e.Position2, v => e.Position2 = (byte)v, U8);
        f.Num("Position Unused", () => e.PositionUnused, v => e.PositionUnused = (byte)v, U8);
        f.Check("Interacted Today", () => e.IsInteractedToday, v => e.IsInteractedToday = v);
    }

    // ---- comma-separated list helpers (mirror the WinForms editor's textbox approach) ----

    private static string ShopCountsStr(JoinAvenueVisitor5 e) => string.Join(", ", new[]
    {
        e.ShopCountRaffle, e.ShopCountSalon, e.ShopCountMarket, e.ShopCountFlorist,
        e.ShopCountDojo, e.ShopCountNurse, e.ShopCountAntique, e.ShopCountCafe,
    });

    private static void SetShopCounts(JoinAvenueVisitor5 e, string s)
    {
        var c = ParseBytes(s, 8, 0x0F);
        e.ShopCountRaffle = c[0]; e.ShopCountSalon = c[1]; e.ShopCountMarket = c[2]; e.ShopCountFlorist = c[3];
        e.ShopCountDojo = c[4]; e.ShopCountNurse = c[5]; e.ShopCountAntique = c[6]; e.ShopCountCafe = c[7];
    }

    private static string RecordsStr(JoinAvenueVisitor5 e) =>
        string.Join(", ", Enumerable.Range(0, (int)JoinAvenueRecordIndex5.COUNT_MAX).Select(i => e.GetRecord((JoinAvenueRecordIndex5)i)));

    private static void SetRecords(JoinAvenueVisitor5 e, string s)
    {
        var r = ParseUInts(s, (int)JoinAvenueRecordIndex5.COUNT_MAX);
        for (int i = 0; i < r.Length; i++) e.SetRecord((JoinAvenueRecordIndex5)i, r[i]);
    }

    private static string TriviaStr(JoinAvenueVisitor5 e) =>
        string.Join(", ", Enumerable.Range(0, JoinAvenueVisitor5.TriviaCount).Select(e.GetTrivia));

    private static void SetTrivia(JoinAvenueVisitor5 e, string s)
    {
        var t = ParseBytes(s, JoinAvenueVisitor5.TriviaCount, byte.MaxValue);
        for (int i = 0; i < t.Length; i++) e.SetTrivia(i, t[i]);
    }

    private static string ActivitiesStr(JoinAvenueVisitor5 e) =>
        string.Join(", ", Enumerable.Range(0, JoinAvenueVisitor5.ActivityCount).Select(e.GetActivity));

    private static void SetActivities(JoinAvenueVisitor5 e, string s)
    {
        var a = ParseBytes(s, JoinAvenueVisitor5.ActivityCount, byte.MaxValue);
        for (int i = 0; i < a.Length; i++) e.SetActivity(i, a[i]);
    }

    private static string ActivityDatesStr(JoinAvenueVisitor5 e) =>
        string.Join(", ", Enumerable.Range(0, JoinAvenueVisitor5.ActivityCount).Select(i => e.GetActivityDate(i).RawValue));

    private static void SetActivityDates(JoinAvenueVisitor5 e, string s)
    {
        var d = ParseUShorts(s, JoinAvenueVisitor5.ActivityCount);
        for (int i = 0; i < d.Length; i++) e.SetActivityDate(i, new JoinAvenueDate5(d[i]));
    }

    private static string[] Split(string? s) => (s ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);

    private static uint[] ParseUInts(string? s, int n)
    {
        var r = new uint[n];
        var p = Split(s);
        for (int i = 0; i < n && i < p.Length; i++) uint.TryParse(p[i], out r[i]);
        return r;
    }

    private static ushort[] ParseUShorts(string? s, int n)
    {
        var r = new ushort[n];
        var p = Split(s);
        for (int i = 0; i < n && i < p.Length; i++) ushort.TryParse(p[i], out r[i]);
        return r;
    }

    private static byte[] ParseBytes(string? s, int n, byte max)
    {
        var r = new byte[n];
        var p = Split(s);
        for (int i = 0; i < n && i < p.Length; i++)
        {
            if (uint.TryParse(p[i], out var v))
                r[i] = (byte)Math.Min(v, max);
        }
        return r;
    }

    private static IReadOnlyList<Opt> BuildSpeciesOpts()
    {
        var names = GameInfo.GetStrings("en").specieslist;
        var list = new List<Opt>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            var n = names[i];
            if (!string.IsNullOrEmpty(n))
                list.Add(new Opt($"{i:000} {n}", i));
        }
        return list;
    }

    private static IReadOnlyList<Opt> BuildEntreeSpeciesOpts()
    {
        var names = GameInfo.GetStrings("en").specieslist;
        var list = new List<Opt> { new("000 (none)", 0) };
        for (int i = 1; i < names.Length; i++)
        {
            var n = names[i];
            if (!string.IsNullOrEmpty(n))
                list.Add(new Opt($"{i:000} {n}", i));
        }
        return list;
    }

    private static IReadOnlyList<Opt> BuildMoveOpts()
    {
        var names = GameInfo.GetStrings("en").movelist;
        var list = new List<Opt> { new("000 (none)", 0) };
        for (int i = 1; i < names.Length; i++)
        {
            var n = names[i];
            if (!string.IsNullOrEmpty(n))
                list.Add(new Opt($"{i:000} {n}", i));
        }
        return list;
    }

    private static JoinAvenueDate5 MakeDate(DateOnly? d)
    {
        var jd = new JoinAvenueDate5();
        jd.Date = d; // null clears RawValue to 0
        return jd;
    }

    // ---------------- Global Link (Dream World / Game Sync) ----------------

    private Control BuildGlobalLinkTab(SAV5 sav)
    {
        var gl = sav.GlobalLink;
        var f = new FieldForm();

        f.Header("Account / Sync");
        f.Num("Upload Count (-1 = never)", () => gl.UploadCount, v => gl.UploadCount = (int)v, int.MaxValue, -1);
        f.Num("Upload Status", () => gl.UploadStatus, v => gl.UploadStatus = (byte)v, U8);
        f.Check("Slot Present", () => gl.IsSlotPresent, v => gl.IsSlotPresent = v);
        f.Check("Registered (B/W Dream World)", () => gl.IsRegistered, v => gl.IsRegistered = v);
        f.Check("Account Full Access", () => gl.IsAccountFullAccess, v => gl.IsAccountFullAccess = v);

        f.Header("Downloaded Content");
        f.Num("Musical", () => gl.Musical, v => gl.Musical = (byte)v, U8);
        f.Num("C-Gear Skin", () => gl.CGearSkin, v => gl.CGearSkin = (byte)v, U8);
        f.Num("Dex Skin", () => gl.DexSkin, v => gl.DexSkin = (byte)v, U8);

        f.Header($"Items ({GlobalLink5.CountItems})");
        f.Custom("Item IDs", () => GlItemsStr(gl), s => SetGlItems(gl, s), 340);
        f.Custom("Item Quantities", () => GlItemQtyStr(gl), s => SetGlItemQty(gl, s), 340);

        f.Header("Furniture");
        f.Num("Selected Index (0x7F = none)", () => gl.SelectedFurnitureIndex, v => gl.SelectedFurnitureIndex = (byte)v, 127);
        f.Check("Furniture Synchronized", () => gl.IsFurnitureSynchronized, v => gl.IsFurnitureSynchronized = v);
        for (int i = 0; i < GlobalLink5.CountFurniture; i++)
        {
            int idx = i; // capture per-row index
            f.Num($"Furniture #{idx + 1} Value", () => gl.GetFurniture(idx).Value, v => { var fr = gl.GetFurniture(idx); fr.Value = (ushort)v; }, U16);
            f.Text($"Furniture #{idx + 1} Name", () => gl.GetFurniture(idx).Name, v => { var fr = gl.GetFurniture(idx); fr.Name = v; }, 12);
        }

        f.Load();
        _applyHooks.Add(f.Save);
        return new ScrollViewer { Content = f };
    }

    private static string GlItemsStr(GlobalLink5 gl) =>
        string.Join(", ", Enumerable.Range(0, GlobalLink5.CountItems).Select(gl.GetItem));

    private static void SetGlItems(GlobalLink5 gl, string s)
    {
        var v = ParseUShorts(s, GlobalLink5.CountItems);
        for (int i = 0; i < v.Length; i++) gl.SetItem(i, v[i]);
    }

    private static string GlItemQtyStr(GlobalLink5 gl) =>
        string.Join(", ", Enumerable.Range(0, GlobalLink5.CountItems).Select(gl.GetItemQuantity));

    private static void SetGlItemQty(GlobalLink5 gl, string s)
    {
        var v = ParseBytes(s, GlobalLink5.CountItems, byte.MaxValue);
        for (int i = 0; i < v.Length; i++) gl.SetItemQuantity(i, v[i]);
    }

    // ---------------- Entralink / Entrée Forest ----------------

    private Control BuildEntreeTab(SAV5 sav)
    {
        var entree = sav.EntreeForest;
        var slots = entree.Slots; // decrypts the block in place; re-encrypted on save via EndAccess()

        var settings = new FieldForm();
        settings.Header("Entralink");
        settings.Num("White Forest Level", () => sav.Entralink.WhiteForestLevel, v => sav.Entralink.WhiteForestLevel = (ushort)v, U16);
        settings.Num("Black City Level", () => sav.Entralink.BlackCityLevel, v => sav.Entralink.BlackCityLevel = (ushort)v, U16);
        if (sav.Entralink is Entralink5BW bw)
            settings.Num("Missions Complete", () => bw.MissionsComplete, v => bw.MissionsComplete = (byte)v, U8);
        if (sav.Entralink is Entralink5B2W2 b2)
        {
            settings.Num("Pass Power 1", () => b2.PassPower1, v => b2.PassPower1 = (byte)v, U8);
            settings.Num("Pass Power 2", () => b2.PassPower2, v => b2.PassPower2 = (byte)v, U8);
            settings.Num("Pass Power 3", () => b2.PassPower3, v => b2.PassPower3 = (byte)v, U8);
        }

        settings.Header("Entrée Forest");
        settings.Check("9th Area Unlocked", () => entree.Unlock9thArea, v => entree.Unlock9thArea = v);
        settings.Num("Extra Areas Unlocked (0–6)", () => entree.Unlock38Areas, v => entree.Unlock38Areas = (int)v, 6);
        settings.Load();
        _applyHooks.Add(settings.Save);

        var unlockAll = new Button { Content = "Unlock All Areas" };
        unlockAll.Click += (_, _) => { entree.UnlockAllAreas(); settings.Load(); };
        var deleteAll = new Button { Content = "Delete All Slots" };
        deleteAll.Click += (_, _) => { entree.DeleteAll(); _entreeReload?.Invoke(); };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(6, 4, 6, 4) };
        btnRow.Children.Add(unlockAll);
        btnRow.Children.Add(deleteAll);

        var settingsPane = new StackPanel();
        settingsPane.Children.Add(new ScrollViewer { Content = settings });
        settingsPane.Children.Add(btnRow);

        var slotEditor = BuildEntreeSlots(slots);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(settingsPane, 0);
        Grid.SetColumn(slotEditor, 1);
        grid.Children.Add(settingsPane);
        grid.Children.Add(slotEditor);
        return grid;
    }

    private Action? _entreeReload;

    private static string EntreeSlotLabel(int index, EntreeSlot s)
    {
        var name = GameInfo.GetStrings("en").specieslist;
        var sp = s.Species != 0 && s.Species < name.Length ? name[s.Species] : "—";
        return $"#{index + 1,3}  {sp}";
    }

    private Control BuildEntreeSlots(EntreeSlot[] slots)
    {
        var list = new ListBox
        {
            Width = 240,
            ItemsSource = slots.Select((s, i) => EntreeSlotLabel(i, s)).ToList(),
        };
        var host = new ScrollViewer();

        FieldForm? cur = null;
        int curIdx = -1;

        void SaveCur() { if (curIdx >= 0) cur?.Save(); }
        void Show(int idx)
        {
            SaveCur();
            curIdx = idx;
            if (idx < 0) return;
            var s = slots[idx];
            var f = new FieldForm();
            f.Header($"Slot #{idx + 1} — {s.Area}");
            f.Combo("Species", EntreeSpeciesOpts, () => s.Species, v => s.Species = (ushort)v);
            f.Combo("Special Move", MoveOpts, () => s.Move, v => s.Move = (ushort)v);
            f.Num("Gender (0=M,1=F,2=Genderless)", () => s.Gender, v => s.Gender = (byte)v, 3);
            f.Num("Form", () => s.Form, v => s.Form = (byte)v, 63);
            f.Combo("Animation", AnimationOpts, () => (int)s.Animation, v => s.Animation = (EntreeForestAnimation)v);
            f.Load();
            host.Content = f;
            cur = f;
        }

        list.SelectionChanged += (_, _) => { if (list.SelectedIndex >= 0) Show(list.SelectedIndex); };
        _applyHooks.Add(SaveCur);

        // Called after a bulk edit (Delete All) zeroes the backing data: drop the stale editor state
        // (so SaveCur won't write old values back) then reload labels + the current slot from data.
        _entreeReload = () =>
        {
            curIdx = -1;
            cur = null;
            list.ItemsSource = slots.Select((s, i) => EntreeSlotLabel(i, s)).ToList();
            list.SelectedIndex = slots.Length > 0 ? 0 : -1; // fires SelectionChanged → Show(0) with fresh data
        };

        if (slots.Length > 0) list.SelectedIndex = 0;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(list, 0);
        Grid.SetColumn(host, 1);
        grid.Children.Add(list);
        grid.Children.Add(host);
        return grid;
    }

    // ---------------- Memory Link ----------------

    private static readonly FilePickerFileType Ml5FileType = new("Memory Link block (.ml5)")
    {
        Patterns = ["*.ml5"],
    };

    private Control BuildMemoryLinkTab(SAV5 sav)
    {
        var panel = new StackPanel { Margin = new Thickness(10), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "Memory Link stores two raw 0x400-byte (.ml5) blocks. Use Import/Export to read or " +
                   "write a block via the file picker. Changes apply to the in-memory save.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.85,
        });
        panel.Children.Add(BuildMemoryLinkSlot("Slot 1", () => sav.Link1Data, d => sav.SetLink1Data(d)));
        panel.Children.Add(BuildMemoryLinkSlot("Slot 2", () => sav.Link2Data, d => sav.SetLink2Data(d)));
        return panel;
    }

    private Control BuildMemoryLinkSlot(string title, Func<Memory<byte>> get, Action<byte[]> set)
    {
        var status = new TextBlock { FontSize = 11, Opacity = 0.85, Margin = new Thickness(0, 4, 0, 0) };
        var import = new Button { Content = "Import…" };
        var export = new Button { Content = "Export…" };

        import.Click += async (_, _) =>
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Import {title} (.ml5)",
                    AllowMultiple = false,
                    FileTypeFilter = [Ml5FileType],
                    SuggestedStartLocation = await GetStartFolderAsync(),
                });
                if (files.Count == 0) return;
                var file = files[0];

                byte[] data;
                await using (var stream = await file.OpenReadAsync())
                using (var ms = new MemoryStream())
                {
                    await stream.CopyToAsync(ms);
                    data = ms.ToArray();
                }

                var len = get().Length;
                if (data.Length != len) { status.Text = $"Expected {len} bytes, got {data.Length}."; return; }
                set(data);
                status.Text = $"Imported {data.Length} bytes from {file.Name} (in memory).";
            }
            catch (Exception ex) { status.Text = "Import failed: " + ex.Message; }
        };

        export.Click += async (_, _) =>
        {
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = $"Export {title} (.ml5)",
                    SuggestedFileName = DefaultMlName(title),
                    DefaultExtension = "ml5",
                    FileTypeChoices = [Ml5FileType],
                    SuggestedStartLocation = await GetStartFolderAsync(),
                });
                if (file is null) return;

                var bytes = get().Span.ToArray();
                await using (var stream = await file.OpenWriteAsync())
                    await stream.WriteAsync(bytes);
                status.Text = $"Exported {bytes.Length} bytes → {file.Name}";
            }
            catch (Exception ex) { status.Text = "Export failed: " + ex.Message; }
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(import);
        row.Children.Add(export);

        var inner = new StackPanel { Spacing = 4 };
        inner.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold });
        inner.Children.Add(row);
        inner.Children.Add(status);

        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x80, 0x80, 0x80)),
            Child = inner,
        };
    }

    private async Task<IStorageFolder?> GetStartFolderAsync()
    {
        var dir = _loadedPath is not null ? Path.GetDirectoryName(_loadedPath) : null;
        if (string.IsNullOrEmpty(dir))
            return null;
        return await StorageProvider.TryGetFolderFromPathAsync(dir);
    }

    private static string DefaultMlName(string title) => title.Replace(" ", "").ToLowerInvariant() + ".ml5";
}
