using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
// Must be imported, not qualified: inside `namespace PKHeX.Avalonia`, a leading `Avalonia.` binds to
// PKHeX.Avalonia (enclosing namespaces beat using-directives), so `Avalonia.Media.X` won't resolve.
using Avalonia.Media;
using PKHeX.Core;

namespace PKHeX.Avalonia;

/// <summary>
/// Gen 4 (HG/SS) save tools. Currently a single feature: the Pokéathlon block
/// (<see cref="Pokeathlon4"/>, at 0xD9D4 in <see cref="SAV4HGSS"/>), split across tabs:
///   - General: accumulated points, the 27 Data Card flags, the 12 once-daily Athlete Shop flags,
///     and a read-only global-score / friendship-trophy / fame-level summary.
///   - Medals: the per-species 5-bit medal bitfield for all 493 Gen 4 species, with give-all / clear-all.
///   - Courses: the 5 course records (three run scores + best) and their three participants.
///   - Best Scores: the player's best Athlete-Point score for each of the 10 events.
///   - Counters: the global counters block, including the per-event 1st-place tallies and fame.
///   - Events: the player's own per-event data — attempts plus the 5 stored records and their teams.
///   - Connections: the wireless per-event data — the same 5 records plus the 5 opposing trainers.
/// Every tab edits the live <see cref="SaveFile"/> in memory (same model as
/// <see cref="Gen5ToolsWindow"/> / <see cref="Gen6ToolsWindow"/>); the owning panel's 💾 Save — or
/// this window's "Save to Disk" — writes to disk.
/// </summary>
public partial class Gen4ToolsWindow : Window
{
    private const decimal U16 = ushort.MaxValue;

    /// <summary>1-indexed species range covered by the medal bitfield [Bulbasaur..Arceus].</summary>
    private const int MedalSpeciesCount = PokeathlonMedalManager4.SIZE;

    private const int CourseCount = (int)PokeathlonStat4.Count;
    private const int EventCount = (int)PokeathlonEvent4.Count;
    private const int DataCardCount = (int)DataCard4.Count;
    private const int DailyShopSlots = 12;

    /// <summary>Item id of "Data Card 01"; the 27 cards are consecutive from here.</summary>
    private const int DataCard01ItemID = 505;

    private static readonly IReadOnlyList<Opt> ParticipantGenderOpts =
        [new Opt("♂ Male", 0), new Opt("♀ Female", 1), new Opt("— Genderless", 2)];

    /// <summary>Course names, indexed by <see cref="PokeathlonStat4"/>.</summary>
    private static readonly string[] CourseNames =
        [.. Enum.GetNames<PokeathlonStat4>().Take(CourseCount).Select(ToolsUi.SplitPascal)];

    /// <summary>Event names, indexed by <see cref="PokeathlonEvent4"/>.</summary>
    private static readonly string[] EventNames =
        [.. Enum.GetNames<PokeathlonEvent4>().Take(EventCount).Select(ToolsUi.SplitPascal)];

    private readonly Func<Task<string?>>? _persist;
    private readonly List<Action> _applyHooks = [];

    private readonly IReadOnlyList<Opt> _speciesOpts;
    private readonly IReadOnlyList<Opt> _languageOpts;

    private readonly TabControl _tabs;
    private readonly TextBlock _status;

    public Gen4ToolsWindow() : this(new SAV4HGSS(), null, null) { }

    /// <param name="sav">The live save; edits land in its buffers immediately.</param>
    /// <param name="loadedPath">
    /// Where the save came from. Unused so far — no Pokéathlon field is an importable blob — but kept
    /// to match the other tool windows, which use it to seed the file picker's start folder.
    /// </param>
    /// <param name="persist">Writes the save to disk; null turns Apply into an in-memory apply.</param>
    public Gen4ToolsWindow(SaveFile sav, string? loadedPath, Func<Task<string?>>? persist)
    {
        AvaloniaXamlLoader.Load(this);
        _persist = persist;

        // Filtered to what a Gen 4 save can legally hold — the global string tables run to Gen 9 and
        // would happily write an out-of-range species id into the block.
        var filtered = new FilteredGameDataSource(sav, GameInfo.Sources);
        _speciesOpts = ToolsUi.ToOptsWithNone(filtered.Species);

        var langs = new List<Opt> { new("(none)", 0) };
        langs.AddRange(GameInfo.LanguageDataSource(4, EntityContext.Gen4).Select(c => new Opt(c.Text, c.Value)));
        _languageOpts = langs;

        _tabs = this.FindControl<TabControl>("RootTabs")!;
        _status = this.FindControl<TextBlock>("StatusText")!;
        var apply = this.FindControl<Button>("ApplyButton")!;
        var close = this.FindControl<Button>("CloseButton")!;
        apply.Content = persist is null ? "Apply & Close" : "Save to Disk";
        apply.Click += async (_, _) => await ApplyAndClose(persist is not null);
        close.Click += (_, _) => { Flush(); Close(); };

        ComboWheelGuard.Attach(this); // wheel over a closed combo must scroll the form, not edit it

        // Pokéathlon is HG/SS only: DP/Pt (SAV4DP / SAV4Pt) have no such block.
        if (sav is SAV4HGSS hgss)
        {
            var p = hgss.Pokeathlon;
            _tabs.Items.Add(new TabItem { Header = "Pokéathlon", Content = BuildGeneralTab(p) });
            _tabs.Items.Add(new TabItem { Header = "Medals", Content = BuildMedalTab(p) });
            _tabs.Items.Add(new TabItem { Header = "Courses", Content = BuildCourseTab(p) });
            _tabs.Items.Add(new TabItem { Header = "Best Scores", Content = BuildBestScoreTab(p) });
            _tabs.Items.Add(new TabItem { Header = "Counters", Content = BuildCounterTab(p) });
            _tabs.Items.Add(new TabItem { Header = "Events", Content = BuildSelfEventTab(p) });
            _tabs.Items.Add(new TabItem { Header = "Connections", Content = BuildConnectionTab(p) });
        }

        if (_tabs.Items.Count != 0)
            _tabs.SelectedIndex = 0;
    }

    /// <summary>
    /// Commits every registered form back into the save. Apply runs from an <c>async void</c> click
    /// handler, where an escaping exception would take the process down, so a throwing setter is
    /// caught and reported here rather than left to propagate.
    /// </summary>
    /// <returns>false if any hook threw (the rest still run).</returns>
    private bool Flush()
    {
        string? failure = null;
        foreach (var h in _applyHooks)
        {
            try
            {
                h();
            }
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
        // A rejected value leaves the window open so the user can see which field to fix.
        if (!Flush())
            return;
        if (persist && _persist is not null)
        {
            var p = await _persist();
            _status.Text = p is null ? "Applied." : $"Saved → {p}";
        }
        Close();
    }

    // ---------------- General (points / Data Cards / daily shop) ----------------

    private Control BuildGeneralTab(Pokeathlon4 p)
    {
        var f = new FieldForm();
        f.Header("Pokéathlon");
        f.Num("Points", () => p.Points, v => p.Points = (uint)v, Pokeathlon4.MaxPoints);

        // Score summary is derived from the course records, medals and best scores, so it only
        // reflects the values already committed to the block — hence the Recalculate button below.
        f.Header("Summary (read-only)");
        ToolsUi.ReadOnlyRow(f, "Global Score", () => p.CalculateGlobalScore().ToString());
        ToolsUi.ReadOnlyRow(f, "Friendship Trophies",
            () => $"{Pokeathlon4.CalculateFriendshipTrophyCount(p.CalculateGlobalScore())} / {Pokeathlon4.FriendshipTrophyThresholds.Length}");
        ToolsUi.ReadOnlyRow(f, "Fame Level",
            () => $"{PokeathlonGlobalCounters4.GetFameLevel(p.GlobalCounters.Fame)} / {PokeathlonGlobalCounters4.FameInclusiveThreshold.Length}");

        var cards = BuildFlagPanel(DataCardCount, DataCardLabel);
        var daily = BuildFlagPanel(DailyShopSlots, i => $"Slot {i + 1}");

        void LoadFlags()
        {
            var cardFlags = p.FlagsDataCard;
            for (int i = 0; i < cards.Length; i++)
                cards[i].IsChecked = ((cardFlags >> i) & 1) != 0;
            var shopFlags = p.FlagsDailyShop;
            for (int i = 0; i < daily.Length; i++)
                daily[i].IsChecked = ((shopFlags >> i) & 1) != 0;
        }

        void SaveFlags()
        {
            uint cardFlags = 0;
            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i].IsChecked == true)
                    cardFlags |= 1u << i;
            }
            p.FlagsDataCard = cardFlags;

            ushort shopFlags = 0;
            for (int i = 0; i < daily.Length; i++)
            {
                if (daily[i].IsChecked == true)
                    shopFlags |= (ushort)(1 << i);
            }
            p.FlagsDailyShop = shopFlags;
        }

        f.Load();
        LoadFlags();
        _applyHooks.Add(f.Save);
        _applyHooks.Add(SaveFlags);

        // Save before Load: the same form holds the editable Points row, so a bare Load() would both
        // discard a pending edit and recompute the summary from data that still excludes it.
        var recalc = new Button { Content = "Recalculate Summary" };
        recalc.Click += (_, _) => { f.Save(); f.Load(); };

        var allCards = new Button { Content = "Obtain All Data Cards" };
        allCards.Click += (_, _) => SetAll(cards, true);
        var noCards = new Button { Content = "Clear Data Cards" };
        noCards.Click += (_, _) => SetAll(cards, false);
        var allShop = new Button { Content = "Mark Shop Bought" };
        allShop.Click += (_, _) => SetAll(daily, true);
        var noShop = new Button { Content = "Reset Shop" };
        noShop.Click += (_, _) => SetAll(daily, false);

        var flagsColumn = new StackPanel { Spacing = 4, Margin = new Thickness(6) };
        flagsColumn.Children.Add(new TextBlock { Text = "Data Cards", FontWeight = FontWeight.Bold });
        flagsColumn.Children.Add(new TextBlock
        {
            Text = "Cards are also held as Key Items; clearing a flag here does not remove the item.",
            FontSize = 11, Opacity = 0.85, TextWrapping = TextWrapping.Wrap,
        });
        foreach (var c in cards)
            flagsColumn.Children.Add(c);
        flagsColumn.Children.Add(new TextBlock
        {
            Text = "Daily Athlete Shop (purchased today)",
            FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8, 0, 0),
        });
        foreach (var c in daily)
            flagsColumn.Children.Add(c);

        var body = ToolsUi.TwoColumns(new ScrollViewer { Content = f }, new ScrollViewer { Content = flagsColumn });
        return ToolsUi.WithToolbar(body, null, recalc, allCards, noCards, allShop, noShop);
    }

    private static void SetAll(CheckBox[] boxes, bool value)
    {
        foreach (var b in boxes)
            b.IsChecked = value;
    }

    private static CheckBox[] BuildFlagPanel(int count, Func<int, string> label)
    {
        var result = new CheckBox[count];
        for (int i = 0; i < count; i++)
            result[i] = new CheckBox { Content = label(i), FontSize = 11 };
        return result;
    }

    private static string DataCardLabel(int index)
    {
        var items = GameInfo.GetStrings("en").GetItemStrings(EntityContext.Gen4);
        var id = DataCard01ItemID + index;
        var name = (uint)id < (uint)items.Length ? items[id] : $"Data Card {index + 1:00}";
        var stat = ToolsUi.SplitPascal(((DataCard4)index).ToString());
        return $"[{index:00}] {name} — {stat}";
    }

    // ---------------- Medals ----------------

    private Control BuildMedalTab(Pokeathlon4 p)
    {
        const string hint = "S=Speed P=Power K=Skill T=Stamina J=Jump. Medals sum into the global score.";
        return BuildListPane(
            () => MedalLabels(p),
            i =>
            {
                var species = (ushort)(i + 1); // the bitfield is 1-indexed
                var f = new FieldForm();
                f.Header($"#{species:000} {SpeciesLabel(species)}");
                for (int c = 0; c < CourseCount; c++)
                {
                    var bit = (byte)(1 << c);
                    f.Check(CourseNames[c],
                        () => (p.Medals.GetMedal(species) & bit) != 0,
                        v =>
                        {
                            var medals = p.Medals;
                            var bits = medals.GetMedal(species);
                            medals.SetMedal(species, (byte)(v ? bits | bit : bits & ~bit));
                        });
                }
                return f;
            },
            hint,
            reload =>
            {
                // Bulk writes bypass the open per-species form; reload drops its cached (pre-op)
                // values so the next Apply can't write them back over the bulk edit.
                var all = new Button { Content = "Give All Medals" };
                all.Click += (_, _) => { var m = p.Medals; m.SetAllMedals(); reload(); };
                var none = new Button { Content = "Clear All Medals" };
                none.Click += (_, _) => { var m = p.Medals; m.Clear(); reload(); };
                return [all, none];
            },
            listWidth: 240);
    }

    private static List<string> MedalLabels(Pokeathlon4 p)
    {
        var medals = p.Medals;
        var list = new List<string>(MedalSpeciesCount);
        for (int i = 0; i < MedalSpeciesCount; i++)
        {
            var species = (ushort)(i + 1);
            var bits = medals.GetMedal(species);
            list.Add($"{species:000} {SpeciesLabel(species)}  {MedalBitString(bits)}");
        }
        return list;
    }

    /// <summary>
    /// Distinct one-letter tags for the five courses, in <see cref="PokeathlonStat4"/> order. Not just
    /// the first letter of each name — Speed / Skill / Stamina would all collide on 'S'.
    /// </summary>
    private const string CourseInitials = "SPKTJ";

    /// <summary>Compact per-course medal display, e.g. "SP·T·" — one tag per obtained course.</summary>
    private static string MedalBitString(byte bits)
    {
        var chars = new char[CourseCount];
        for (int i = 0; i < CourseCount; i++)
            chars[i] = ((bits >> i) & 1) != 0 ? CourseInitials[i] : '·';
        return new string(chars);
    }

    private static string SpeciesLabel(ushort species)
    {
        var names = GameInfo.GetStrings("en").specieslist;
        return species < names.Length ? names[species] : "—";
    }

    // ---------------- Course records ----------------

    private Control BuildCourseTab(Pokeathlon4 p)
    {
        return BuildListPane(
            () => [.. Enumerable.Range(0, CourseCount).Select(i => $"{i + 1} — {CourseNames[i]}")],
            i =>
            {
                var course = p.GetCourseRecord((PokeathlonStat4)i);
                var f = new FieldForm();
                f.Header($"{CourseNames[i]} Course — Scores");
                f.Num("Run 1", () => course.Score0, v => course.Score0 = (ushort)v, U16);
                f.Num("Run 2", () => course.Score1, v => course.Score1 = (ushort)v, U16);
                f.Num("Run 3", () => course.Score2, v => course.Score2 = (ushort)v, U16);
                f.Num("Best (counts toward global score)", () => course.ScoreMax, v => course.ScoreMax = (ushort)v, U16);

                for (int n = 0; n < PokeathlonCourseRecord4.CountParticipant; n++)
                {
                    var participant = course.GetParticipant(n);
                    f.Header($"Participant #{n + 1}");
                    AddParticipantRows(f, participant);
                }
                return f;
            },
            "Best is the value summed into the global score; the three runs are the stored history.",
            listWidth: 180);
    }

    private void AddParticipantRows(FieldForm f, PokeathlonParticipant4 entity)
    {
        f.Combo("Species", _speciesOpts, () => entity.Species, v => entity.Species = (ushort)v);
        f.Num("Form", () => entity.Form, v => entity.Form = (byte)v, 31);
        f.Combo("Gender", ParticipantGenderOpts, () => entity.Gender, v => entity.Gender = (byte)v);
        f.Check("Shiny", () => entity.IsShiny, v => entity.IsShiny = v);
        ToolsUi.HexRow(f, "Encryption Constant", () => entity.EncryptionConstant, v => entity.EncryptionConstant = v);
        f.Num("TID16", () => entity.TID16, v => entity.TID16 = (ushort)v, U16);
        f.Num("SID16", () => entity.SID16, v => entity.SID16 = (ushort)v, U16);
    }

    // ---------------- Best scores ----------------

    private Control BuildBestScoreTab(Pokeathlon4 p)
    {
        var f = new FieldForm();
        f.Header("Best score per event (Athlete Points)");
        for (int i = 0; i < EventCount; i++)
        {
            var index = (PokeathlonEvent4)i;
            f.Num($"{i + 1} — {EventNames[i]}", () => p.GetBestScore(index), v => p.SetBestScore(index, (ushort)v), U16);
        }
        f.Load();
        _applyHooks.Add(f.Save);
        return ToolsUi.WithToolbar(new ScrollViewer { Content = f },
            "These ten values are summed into the global Pokéathlon score.");
    }

    // ---------------- Global counters ----------------

    private Control BuildCounterTab(Pokeathlon4 p)
    {
        const decimal maxStat = PokeathlonGlobalCounters4.MaxStat;
        var c = p.GlobalCounters;

        var left = new FieldForm();
        left.Header("Play");
        left.Num("Time Spent (minutes)", () => c.TimeSpent, v => c.TimeSpent = (uint)v, PokeathlonGlobalCounters4.MaxPlay);
        left.Num("Sessions Joined", () => c.SessionsJoined, v => c.SessionsJoined = (uint)v, maxStat);
        left.Num("Placed First", () => c.PlacedFirst, v => c.PlacedFirst = (uint)v, maxStat);
        left.Num("Placed Last", () => c.PlacedLast, v => c.PlacedLast = (uint)v, maxStat);
        left.Num("Fame", () => c.Fame, v => c.Fame = (uint)v, PokeathlonGlobalCounters4.MaxFame);

        left.Header("Performance");
        left.Num("Bonuses Earned", () => c.BonusesEarned, v => c.BonusesEarned = (uint)v, maxStat);
        left.Num("Instructions", () => c.Instructions, v => c.Instructions = (uint)v, maxStat);
        left.Num("Failed", () => c.Failed, v => c.Failed = (uint)v, maxStat);
        left.Num("Jumped", () => c.Jumped, v => c.Jumped = (uint)v, maxStat);
        left.Num("Points Acquired", () => c.Acquired, v => c.Acquired = (uint)v, maxStat);
        left.Num("Tackled", () => c.Tackled, v => c.Tackled = (uint)v, maxStat);
        left.Num("Fell Down", () => c.FellDown, v => c.FellDown = (uint)v, maxStat);
        left.Num("Dashed", () => c.Dashed, v => c.Dashed = (uint)v, maxStat);
        left.Num("Switched", () => c.Switched, v => c.Switched = (uint)v, maxStat);
        left.Num("Self-Impeded", () => c.SelfImpeded, v => c.SelfImpeded = (uint)v, maxStat);

        var right = new FieldForm();
        right.Header("Wireless (Connection)");
        right.Num("Joined", () => c.ConnectionJoined, v => c.ConnectionJoined = (uint)v, maxStat);
        right.Num("First Places", () => c.ConnectionFirst, v => c.ConnectionFirst = (uint)v, maxStat);
        right.Num("Last Places", () => c.ConnectionLast, v => c.ConnectionLast = (uint)v, maxStat);

        right.Header("1st places per event");
        for (int i = 0; i < EventCount; i++)
        {
            var index = (PokeathlonEvent4)i;
            right.Num(EventNames[i], () => c[index], v => c[index] = (uint)v, maxStat);
        }

        right.Header("Totals");
        // The aggregate event 1st-place total isn't stored; Core sums the per-event counters.
        ToolsUi.ReadOnlyRow(right, "Event First (sum)", () => c.TotalEventFirst.ToString());
        right.Num("Event Last", () => c.TotalEventLast, v => c.TotalEventLast = (uint)v, maxStat);

        left.Load();
        right.Load();
        _applyHooks.Add(left.Save);
        _applyHooks.Add(right.Save);

        var refresh = new Button { Content = "Refresh Totals" };
        refresh.Click += (_, _) => { right.Save(); right.Load(); };

        var body = ToolsUi.TwoColumns(new ScrollViewer { Content = left }, new ScrollViewer { Content = right });
        return ToolsUi.WithToolbar(body, "Data Card readouts are driven by these counters.", refresh);
    }

    // ---------------- Per-event records (self) ----------------

    private Control BuildSelfEventTab(Pokeathlon4 p)
    {
        return BuildListPane(
            () => [.. Enumerable.Range(0, EventCount).Select(i => $"{i + 1} — {EventNames[i]}")],
            i =>
            {
                var data = p.GetEventSelf((PokeathlonEvent4)i);
                var f = new FieldForm();
                f.Header(EventNames[i]);
                f.Num("Attempts", () => data.Attempts, v => data.Attempts = (uint)v, PokeathlonEventData4.MaxAttempts);
                AddRecordRows(f, data);
                return f;
            },
            "Record units are event-specific (Hurdle Dash stores a frame count — lower is better).",
            listWidth: 190);
    }

    // ---------------- Per-event records (wireless connection) ----------------

    private Control BuildConnectionTab(Pokeathlon4 p)
    {
        return BuildListPane(
            () => [.. Enumerable.Range(0, EventCount).Select(i => $"{i + 1} — {EventNames[i]}")],
            i =>
            {
                var conn = p.GetEventConnection((PokeathlonEvent4)i);
                var inner = conn.Inner;
                var f = new FieldForm();
                f.Header($"{EventNames[i]} — Connection");
                f.Num("Attempts", () => inner.Attempts, v => inner.Attempts = (uint)v, PokeathlonEventData4.MaxAttempts);
                AddRecordRows(f, inner);

                for (int t = 0; t < PokeathlonConnection4.MaxTrainer; t++)
                {
                    var trainer = conn.GetTrainer(t);
                    f.Header($"Record #{t + 1} — Opposing Trainer");
                    // Language drives the string encoding, so set it before the name on a fresh entry.
                    f.Combo("Language", _languageOpts, () => trainer.Language, v => trainer.Language = (byte)v);
                    f.Text("OT Name", () => trainer.OriginalTrainerName, v => trainer.OriginalTrainerName = v, 7);
                    f.Num("TID16", () => trainer.TID16, v => trainer.TID16 = (ushort)v, U16);
                    f.Num("SID16", () => trainer.SID16, v => trainer.SID16 = (ushort)v, U16);
                }
                return f;
            },
            "Trainers are positionally paired with the five records above them.",
            listWidth: 190);
    }

    /// <summary>The five stored records of a <see cref="PokeathlonEventData4"/>, each with its team of three.</summary>
    private void AddRecordRows(FieldForm f, PokeathlonEventData4 data)
    {
        for (int r = 0; r < PokeathlonEventData4.MaxRecord; r++)
        {
            var rec = data.GetRecord(r);
            f.Header($"Record #{r + 1}");
            f.Num("Record", () => rec.Record, v => rec.Record = (ushort)v, U16);
            AddEntryRows(f, "Entry 1", () => rec.Entry0, v => rec.Entry0 = v);
            AddEntryRows(f, "Entry 2", () => rec.Entry1, v => rec.Entry1 = v);
            AddEntryRows(f, "Entry 3", () => rec.Entry2, v => rec.Entry2 = v);
        }
    }

    /// <summary>
    /// A packed <see cref="SpeciesForm10"/> slot. Unlike the block wrappers this is a plain value
    /// (not a view over save memory), so each edit reads, modifies and writes the whole field back.
    /// </summary>
    private void AddEntryRows(FieldForm f, string label, Func<SpeciesForm10> get, Action<SpeciesForm10> set)
    {
        f.Combo($"{label} Species", _speciesOpts, () => get().Species, v =>
        {
            var e = get();
            e.Species = (ushort)v;
            set(e);
        });
        f.Num($"{label} Form", () => get().Form, v =>
        {
            var e = get();
            e.Form = (byte)v;
            set(e);
        }, 63);
    }

    // ---------------- shared list-editor pane ----------------

    /// <summary>
    /// A left-hand index list driving a right-hand <see cref="FieldForm"/> built on demand. The open
    /// form is committed when the selection changes and on Apply; <paramref name="extra"/> receives a
    /// reload callback that discards the open form's cached values (needed after any bulk mutation —
    /// otherwise the stale form would be written straight back over it).
    /// </summary>
    private Control BuildListPane(
        Func<List<string>> labels,
        Func<int, FieldForm> build,
        string? hint = null,
        Func<Action, Button[]>? extra = null,
        double listWidth = 210)
    {
        var list = new ListBox { Width = listWidth, ItemsSource = labels() };
        var host = new ScrollViewer();

        FieldForm? cur = null;
        int curIndex = -1;

        void SaveCur()
        {
            if (curIndex >= 0)
                cur?.Save();
        }

        void Show(int index)
        {
            SaveCur();
            curIndex = index;
            if (index < 0)
            {
                cur = null;
                host.Content = null;
                return;
            }
            var f = build(index);
            f.Load();
            host.Content = f;
            cur = f;
        }

        list.SelectionChanged += (_, _) => Show(list.SelectedIndex);
        _applyHooks.Add(SaveCur);

        void Reload()
        {
            curIndex = -1; // drop the stale form before re-reading the labels
            cur = null;
            var keep = list.SelectedIndex;
            list.ItemsSource = labels();
            list.SelectedIndex = -1;
            list.SelectedIndex = keep >= 0 ? keep : 0;
        }

        var buttons = new List<Button>();
        if (extra is not null)
            buttons.AddRange(extra(Reload));

        // Labels are snapshots: editing a field doesn't relabel its row. Refreshing from inside
        // SaveCur would re-enter the SelectionChanged handler that calls it, so this is explicit.
        var relabel = new Button { Content = "Refresh Labels" };
        relabel.Click += (_, _) => { SaveCur(); Reload(); };
        buttons.Add(relabel);

        list.SelectedIndex = 0;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(list, 0);
        var right = ToolsUi.WithToolbar(host, hint, [.. buttons]);
        Grid.SetColumn(right, 1);
        grid.Children.Add(list);
        grid.Children.Add(right);
        return grid;
    }
}
