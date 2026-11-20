using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PKHeX.Core;
using PKHeX.Drawing.PokeSprite; // pk.Sprite(), SpriteUtil

namespace PKHeX.Avalonia;

/// <summary>
/// Rich single-Pokémon editor opened by double-clicking a box/party slot. Edits a CLONE of the
/// selected <see cref="PKM"/> so Cancel discards cleanly; on Apply it refreshes the checksum and
/// hands the edited clone back to the caller via <paramref name="onApply"/>, which writes it to the
/// originating slot. Covers nickname, level, nature, ability, held item, four moves, IVs, EVs, and
/// a make-shiny shortcut.
/// </summary>
public partial class EditPokemonWindow : Window
{
    private const int LanguageEnglish = 2;

    private readonly PKM _pk;            // working clone
    private readonly Action<PKM>? _onApply;
    private readonly GameStrings _strings;
    private readonly string[] _itemList;
    private readonly string[] _moveList;
    private readonly string[] _abilityList;
    private readonly string[] _ballList;

    // Name -> id maps for the AutoCompleteBox fields (first occurrence wins).
    private readonly Dictionary<string, int> _itemMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _moveMap = new(StringComparer.OrdinalIgnoreCase);

    private Image _sprite = null!;
    private TextBlock _speciesText = null!, _legalityText = null!, _status = null!;
    private TextBox _nick = null!;
    private CheckBox _nicknamed = null!;
    private NumericUpDown _level = null!;
    private ComboBox _nature = null!, _ability = null!;
    private AutoCompleteBox _heldItem = null!, _move1 = null!, _move2 = null!, _move3 = null!, _move4 = null!;
    private NumericUpDown _move1Pp = null!, _move2Pp = null!, _move3Pp = null!, _move4Pp = null!;
    private NumericUpDown _ivHp = null!, _ivAtk = null!, _ivDef = null!, _ivSpe = null!, _ivSpa = null!, _ivSpd = null!;
    private NumericUpDown _evHp = null!, _evAtk = null!, _evDef = null!, _evSpe = null!, _evSpa = null!, _evSpd = null!;
    private ComboBox _gender = null!, _ball = null!, _otGender = null!, _form = null!, _metLocation = null!;
    private NumericUpDown _friendship = null!, _tid = null!, _sid = null!, _metLevel = null!;
    private TextBox _otName = null!, _legalityReport = null!, _pid = null!;
    private DatePicker _metDate = null!;
    private Button _shiny = null!, _apply = null!, _cancel = null!, _recheck = null!, _clearMetDate = null!, _rerollPid = null!;
    private FieldForm? _markForm, _ribbonForm;

    /// <summary>Design-time / XAML preview constructor.</summary>
    public EditPokemonWindow() : this(new PK9 { Species = 1 }, null) { }

    public EditPokemonWindow(PKM source, Action<PKM>? onApply)
    {
        AvaloniaXamlLoader.Load(this);
        _pk = source.Clone();
        _onApply = onApply;

        _strings = GameInfo.GetStrings("en");
        _itemList = _strings.itemlist;
        _moveList = _strings.movelist;
        _abilityList = _strings.abilitylist;
        _ballList = _strings.balllist;

        ResolveControls();
        BuildLookups();
        PopulateChoices();
        BuildMarkingsAndRibbons();
        LoadFromPk();
        ComboWheelGuard.Attach(this); // wheel over a closed combo must scroll the form, not edit it

        _shiny.Click += (_, _) => MakeShiny();
        _rerollPid.Click += (_, _) => RerollPid();
        _recheck.Click += (_, _) => Recheck();
        _apply.Click += (_, _) => ApplyAndClose();
        _cancel.Click += (_, _) => Close();
        _clearMetDate.Click += (_, _) => _metDate.SelectedDate = null;
    }

    private void ResolveControls()
    {
        _sprite = this.FindControl<Image>("EditSprite")!;
        _speciesText = this.FindControl<TextBlock>("SpeciesText")!;
        _legalityText = this.FindControl<TextBlock>("LegalityText")!;
        _status = this.FindControl<TextBlock>("StatusText")!;
        _nick = this.FindControl<TextBox>("NickBox")!;
        _nicknamed = this.FindControl<CheckBox>("NicknamedCheck")!;
        _level = this.FindControl<NumericUpDown>("LevelBox")!;
        _nature = this.FindControl<ComboBox>("NatureBox")!;
        _ability = this.FindControl<ComboBox>("AbilityBox")!;
        _heldItem = this.FindControl<AutoCompleteBox>("HeldItemBox")!;
        _move1 = this.FindControl<AutoCompleteBox>("Move1Box")!;
        _move2 = this.FindControl<AutoCompleteBox>("Move2Box")!;
        _move3 = this.FindControl<AutoCompleteBox>("Move3Box")!;
        _move4 = this.FindControl<AutoCompleteBox>("Move4Box")!;
        _move1Pp = this.FindControl<NumericUpDown>("Move1Pp")!;
        _move2Pp = this.FindControl<NumericUpDown>("Move2Pp")!;
        _move3Pp = this.FindControl<NumericUpDown>("Move3Pp")!;
        _move4Pp = this.FindControl<NumericUpDown>("Move4Pp")!;
        _ivHp = this.FindControl<NumericUpDown>("IvHp")!;
        _ivAtk = this.FindControl<NumericUpDown>("IvAtk")!;
        _ivDef = this.FindControl<NumericUpDown>("IvDef")!;
        _ivSpe = this.FindControl<NumericUpDown>("IvSpe")!;
        _ivSpa = this.FindControl<NumericUpDown>("IvSpa")!;
        _ivSpd = this.FindControl<NumericUpDown>("IvSpd")!;
        _evHp = this.FindControl<NumericUpDown>("EvHp")!;
        _evAtk = this.FindControl<NumericUpDown>("EvAtk")!;
        _evDef = this.FindControl<NumericUpDown>("EvDef")!;
        _evSpe = this.FindControl<NumericUpDown>("EvSpe")!;
        _evSpa = this.FindControl<NumericUpDown>("EvSpa")!;
        _evSpd = this.FindControl<NumericUpDown>("EvSpd")!;
        _gender = this.FindControl<ComboBox>("GenderBox")!;
        _ball = this.FindControl<ComboBox>("BallBox")!;
        _otGender = this.FindControl<ComboBox>("OtGenderBox")!;
        _form = this.FindControl<ComboBox>("FormBox")!;
        _friendship = this.FindControl<NumericUpDown>("FriendshipBox")!;
        _tid = this.FindControl<NumericUpDown>("TidBox")!;
        _sid = this.FindControl<NumericUpDown>("SidBox")!;
        _metLevel = this.FindControl<NumericUpDown>("MetLevelBox")!;
        _metLocation = this.FindControl<ComboBox>("MetLocationBox")!;
        _otName = this.FindControl<TextBox>("OtNameBox")!;
        _legalityReport = this.FindControl<TextBox>("LegalityReport")!;
        _metDate = this.FindControl<DatePicker>("MetDateBox")!;
        _pid = this.FindControl<TextBox>("PidBox")!;
        _rerollPid = this.FindControl<Button>("RerollPidButton")!;
        _shiny = this.FindControl<Button>("ShinyButton")!;
        _apply = this.FindControl<Button>("ApplyButton")!;
        _cancel = this.FindControl<Button>("CancelButton")!;
        _recheck = this.FindControl<Button>("RecheckButton")!;
        _clearMetDate = this.FindControl<Button>("ClearMetDateButton")!;

        // Let's Go (PB7) has no EVs — the games use Awakening Values (0-200 each) instead, and any
        // non-zero EV is flagged illegal. Repurpose the EV column for AVs rather than adding a 7th.
        if (_pk is IAwakened)
        {
            this.FindControl<TextBlock>("EvHeader")!.Text = $"AV (0-{AwakeningUtil.AwakeningMax})";
            foreach (var n in new[] { _evHp, _evAtk, _evDef, _evSpe, _evSpa, _evSpd })
                n.Maximum = AwakeningUtil.AwakeningMax;
        }
    }

    private void BuildLookups()
    {
        for (int id = 0; id < _itemList.Length; id++)
        {
            var name = _itemList[id];
            if (!string.IsNullOrEmpty(name))
                _itemMap.TryAdd(name, id);
        }
        for (int id = 0; id < _moveList.Length; id++)
        {
            var name = _moveList[id];
            if (!string.IsNullOrEmpty(name))
                _moveMap.TryAdd(name, id);
        }
    }

    private void PopulateChoices()
    {
        // Natures, labelled with the stat each one raises/lowers, e.g. "Adamant (+Atk, -SpA)".
        var natureOpts = new List<Opt>();
        foreach (var nat in Enum.GetValues<Nature>())
        {
            if (!NatureDisplay.IsRealNature(nat)) // skip Random / out-of-range
                continue;
            natureOpts.Add(new Opt(NatureDisplay.GetLabel(nat), (int)nat));
        }
        _nature.ItemsSource = natureOpts;

        // Ability slots available to this species (1: / 2: / Hidden).
        var opts = new List<Opt>();
        var pi = _pk.PersonalInfo;
        for (int n = 0; n < pi.AbilityCount; n++)
        {
            int abilityId = pi.GetAbilityAtIndex(n);
            string abilityName = (uint)abilityId < (uint)_abilityList.Length ? _abilityList[abilityId] : $"#{abilityId}";
            string slotLabel = n == 2 ? "Hidden" : $"Slot {n + 1}";
            opts.Add(new Opt($"{slotLabel}: {abilityName}", n));
        }
        _ability.ItemsSource = opts;

        // Build searchable name lists (skip blanks).
        var itemNames = new List<string>(_itemMap.Keys);
        var moveNames = new List<string>(_moveMap.Keys);
        itemNames.Sort(StringComparer.OrdinalIgnoreCase);
        moveNames.Sort(StringComparer.OrdinalIgnoreCase);
        _heldItem.ItemsSource = itemNames;
        _move1.ItemsSource = moveNames;
        _move2.ItemsSource = moveNames;
        _move3.ItemsSource = moveNames;
        _move4.ItemsSource = moveNames;

        // Gender (♂ / ♀ / genderless) and OT gender (♂ / ♀).
        var genders = GameInfo.GenderSymbolUnicode;
        _gender.ItemsSource = new List<Opt> { new(genders[0], 0), new(genders[1], 1), new(genders[2], 2) };
        _otGender.ItemsSource = new List<Opt> { new(genders[0], 0), new(genders[1], 1) };

        // Ball list (id-indexed names; skip blanks).
        var ballOpts = new List<Opt>();
        for (int id = 0; id < _ballList.Length; id++)
        {
            if (!string.IsNullOrEmpty(_ballList[id]))
                ballOpts.Add(new Opt(_ballList[id], id));
        }
        _ball.ItemsSource = ballOpts;

        // Form names for this species (id-indexed; fall back to "Form N" for blanks).
        var formNames = FormConverter.GetFormList(_pk.Species, _strings.types, _strings.forms,
            GameInfo.GenderSymbolUnicode, _pk.Context);
        // A blank entry means "no distinct form name". For form 0 that's the base form, so show
        // the species name (e.g. "Pikachu") instead of a bare "Form 0"; higher blank indices keep
        // the numbered fallback.
        var speciesName = (uint)_pk.Species < (uint)_strings.specieslist.Length
            ? _strings.specieslist[_pk.Species] : null;
        var formOpts = new List<Opt>(formNames.Length);
        for (int i = 0; i < formNames.Length; i++)
        {
            var name = formNames[i];
            if (string.IsNullOrWhiteSpace(name))
                name = i == 0 && !string.IsNullOrEmpty(speciesName) ? speciesName : $"Form {i}";
            formOpts.Add(new Opt(name, i));
        }
        _form.ItemsSource = formOpts;

        // Met-location names for this entity's game/context.
        var locOpts = new List<Opt>();
        foreach (var c in GameInfo.GetLocationList(_pk.Version, _pk.Context, egg: false))
            locOpts.Add(new Opt(c.Text, c.Value));
        _metLocation.ItemsSource = locOpts;
    }

    private static readonly IReadOnlyList<Opt> MarkingColorOpts =
        [new Opt("None", 0), new Opt("Blue", 1), new Opt("Pink", 2)];

    /// <summary>Builds the markings (typed per generation) and ribbons (reflected) sections into a
    /// <see cref="FieldForm"/> each, or hides the section when the entity has none. The forms are
    /// loaded/saved alongside the rest of the controls via <see cref="LoadFromPk"/> /
    /// <see cref="WriteControlsToPk"/>.</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "The Avalonia app is not trimmed; reflection over ribbon properties is safe.")]
    private void BuildMarkingsAndRibbons()
    {
        var markHost = this.FindControl<StackPanel>("MarkingsHost")!;
        var markHeader = this.FindControl<TextBlock>("MarkingsHeader")!;
        var ribbonHost = this.FindControl<StackPanel>("RibbonsHost")!;
        var ribbonExpander = this.FindControl<Expander>("RibbonsExpander")!;

        // ---- Markings (named shapes via the typed interface for the entity's generation) ----
        var mf = new FieldForm();
        if (_pk is IAppliedMarkings7 m7)
        {
            mf.Combo("Circle", MarkingColorOpts, () => (int)m7.MarkingCircle, v => m7.MarkingCircle = (MarkingColor)v);
            mf.Combo("Triangle", MarkingColorOpts, () => (int)m7.MarkingTriangle, v => m7.MarkingTriangle = (MarkingColor)v);
            mf.Combo("Square", MarkingColorOpts, () => (int)m7.MarkingSquare, v => m7.MarkingSquare = (MarkingColor)v);
            mf.Combo("Heart", MarkingColorOpts, () => (int)m7.MarkingHeart, v => m7.MarkingHeart = (MarkingColor)v);
            mf.Combo("Star", MarkingColorOpts, () => (int)m7.MarkingStar, v => m7.MarkingStar = (MarkingColor)v);
            mf.Combo("Diamond", MarkingColorOpts, () => (int)m7.MarkingDiamond, v => m7.MarkingDiamond = (MarkingColor)v);
            _markForm = mf;
        }
        else if (_pk is IAppliedMarkings4 m4)
        {
            mf.Check("Circle", () => m4.MarkingCircle, v => m4.MarkingCircle = v);
            mf.Check("Triangle", () => m4.MarkingTriangle, v => m4.MarkingTriangle = v);
            mf.Check("Square", () => m4.MarkingSquare, v => m4.MarkingSquare = v);
            mf.Check("Heart", () => m4.MarkingHeart, v => m4.MarkingHeart = v);
            mf.Check("Star", () => m4.MarkingStar, v => m4.MarkingStar = v);
            mf.Check("Diamond", () => m4.MarkingDiamond, v => m4.MarkingDiamond = v);
            _markForm = mf;
        }
        else if (_pk is IAppliedMarkings3 m3)
        {
            mf.Check("Circle", () => m3.MarkingCircle, v => m3.MarkingCircle = v);
            mf.Check("Triangle", () => m3.MarkingTriangle, v => m3.MarkingTriangle = v);
            mf.Check("Square", () => m3.MarkingSquare, v => m3.MarkingSquare = v);
            mf.Check("Heart", () => m3.MarkingHeart, v => m3.MarkingHeart = v);
            _markForm = mf;
        }

        if (_markForm is not null)
            markHost.Children.Add(_markForm);
        else
            markHeader.IsVisible = markHost.IsVisible = false;

        // ---- Ribbons (enumerated by reflection; bool = had/not, byte = count up to MaxCount) ----
        var rf = new FieldForm();
        int ribbonCount = 0;
        // Show ribbons the Pokémon already has first (stable: original order kept within each group).
        var ribbons = RibbonInfo.GetRibbonInfo(_pk)
            .OrderByDescending(ri => ri.HasRibbon || ri.RibbonCount > 0);
        foreach (var ri in ribbons)
        {
            var name = ri.Name;
            var label = PrettifyRibbon(name);
            if (ri.Type == RibbonValueType.Boolean)
            {
                rf.Check(label,
                    () => ReflectUtil.GetValue(_pk, name) is true,
                    v => ReflectUtil.SetValue(_pk, name, v));
            }
            else
            {
                int max = ri.MaxCount;
                rf.Num(label,
                    () => Convert.ToDecimal((byte)(ReflectUtil.GetValue(_pk, name) ?? (byte)0)),
                    v => ReflectUtil.SetValue(_pk, name, (byte)v),
                    max);
            }
            ribbonCount++;
        }

        if (ribbonCount > 0)
        {
            _ribbonForm = rf;
            ribbonHost.Children.Add(_ribbonForm);
            ribbonExpander.Header = $"Ribbons ({ribbonCount})";
        }
        else
        {
            ribbonExpander.IsVisible = false;
        }
    }

    private static string PrettifyRibbon(string name)
    {
        var s = name.StartsWith(RibbonInfo.PropertyPrefix, StringComparison.Ordinal)
            ? name[RibbonInfo.PropertyPrefix.Length..]
            : name;
        var sb = new System.Text.StringBuilder(s.Length + 4);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
                sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private void LoadFromPk()
    {
        RefreshSprite();
        _speciesText.Text = SpeciesName.GetSpeciesName(_pk.Species, LanguageEnglish);
        RefreshLegality();

        _nick.Text = _pk.Nickname;
        _nicknamed.IsChecked = _pk.IsNicknamed;
        _level.Value = _pk.CurrentLevel;
        SelectOpt(_nature, (int)_pk.Nature);

        // Current ability slot from AbilityNumber bitflag (1/2/4 -> 0/1/2); fall back to id match.
        int slot = _pk.AbilityNumber is > 0 ? BitOperations.TrailingZeroCount((uint)_pk.AbilityNumber) : -1;
        if (slot < 0 || slot >= _pk.PersonalInfo.AbilityCount)
            slot = Math.Max(0, _pk.PersonalInfo.GetIndexOfAbility(_pk.Ability));
        if (_ability.ItemsSource is List<Opt> abilityOpts && slot < abilityOpts.Count)
            _ability.SelectedItem = abilityOpts[slot];

        _heldItem.Text = ItemName(_pk.HeldItem);
        _move1.Text = MoveName(_pk.Move1);
        _move2.Text = MoveName(_pk.Move2);
        _move3.Text = MoveName(_pk.Move3);
        _move4.Text = MoveName(_pk.Move4);
        _move1Pp.Value = _pk.Move1_PPUps;
        _move2Pp.Value = _pk.Move2_PPUps;
        _move3Pp.Value = _pk.Move3_PPUps;
        _move4Pp.Value = _pk.Move4_PPUps;

        _ivHp.Value = _pk.IV_HP; _ivAtk.Value = _pk.IV_ATK; _ivDef.Value = _pk.IV_DEF;
        _ivSpe.Value = _pk.IV_SPE; _ivSpa.Value = _pk.IV_SPA; _ivSpd.Value = _pk.IV_SPD;
        if (_pk is IAwakened av)
        {
            _evHp.Value = av.AV_HP; _evAtk.Value = av.AV_ATK; _evDef.Value = av.AV_DEF;
            _evSpe.Value = av.AV_SPE; _evSpa.Value = av.AV_SPA; _evSpd.Value = av.AV_SPD;
        }
        else
        {
            _evHp.Value = _pk.EV_HP; _evAtk.Value = _pk.EV_ATK; _evDef.Value = _pk.EV_DEF;
            _evSpe.Value = _pk.EV_SPE; _evSpa.Value = _pk.EV_SPA; _evSpd.Value = _pk.EV_SPD;
        }

        SelectOpt(_gender, _pk.Gender);
        SelectOpt(_ball, _pk.Ball);
        SelectOpt(_otGender, _pk.OriginalTrainerGender);
        SelectOrAdd(_form, _pk.Form, $"Form {_pk.Form}");
        SelectOrAdd(_metLocation, _pk.MetLocation, $"Location {_pk.MetLocation}");
        _friendship.Value = _pk.CurrentFriendship;
        _otName.Text = _pk.OriginalTrainerName;
        _tid.Value = _pk.DisplayTID;
        _sid.Value = _pk.DisplaySID;
        _metLevel.Value = _pk.MetLevel;
        _metDate.SelectedDate = _pk.MetDate is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue)) : null;
        _pid.Text = _pk.PID.ToString("X8");

        _markForm?.Load();
        _ribbonForm?.Load();
    }

    private static void SelectOpt(ComboBox box, int value)
    {
        if (box.ItemsSource is IEnumerable<Opt> opts)
            box.SelectedItem = opts.FirstOrDefault(o => o.Value == value) ?? opts.FirstOrDefault();
    }

    /// <summary>Selects the option matching <paramref name="value"/>; if none exists (e.g. a met
    /// location not in the game's list), appends one labelled <paramref name="fallbackLabel"/> so the
    /// current value is shown and round-trips.</summary>
    private static void SelectOrAdd(ComboBox box, int value, string fallbackLabel)
    {
        if (box.ItemsSource is not List<Opt> opts)
            return;
        var match = opts.FirstOrDefault(o => o.Value == value);
        if (match is null)
        {
            match = new Opt(fallbackLabel, value);
            opts.Add(match);
            box.ItemsSource = null;   // force the ItemsControl to pick up the mutated list
            box.ItemsSource = opts;
        }
        box.SelectedItem = match;
    }

    private string ItemName(int id) => (uint)id < (uint)_itemList.Length ? _itemList[id] : string.Empty;
    private string MoveName(int id) => (uint)id < (uint)_moveList.Length ? _moveList[id] : string.Empty;

    private int ParseItem(string? text)
        => !string.IsNullOrWhiteSpace(text) && _itemMap.TryGetValue(text.Trim(), out var id) ? id : -1;

    private int ParseMove(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0; // blank clears the move slot
        return _moveMap.TryGetValue(text.Trim(), out var id) ? id : -1;
    }

    private void MakeShiny()
    {
        _pk.SetShiny();
        _pid.Text = _pk.PID.ToString("X8"); // SetShiny may change the PID; keep the box in sync so Apply doesn't revert it
        RefreshSprite();
        _status.Text = "Made shiny — click Apply to write it back.";
    }

    /// <summary>Rerolls the PID via <see cref="PKM.SetPIDGender"/>, keeping the species/gender/
    /// nature/form currently in the controls consistent (Core also syncs the EncryptionConstant for
    /// Gen3–5 entities in modern formats). Note Core rerolls until the result is NOT shiny — use
    /// Make Shiny for the shiny path.</summary>
    private void RerollPid()
    {
        try
        {
            WriteControlsToPk(); // reroll against the gender/nature the user has picked, not stale values
            _pk.SetPIDGender(_pk.Gender);
            LoadFromPk();
            _status.Text = "PID rerolled (non-shiny).";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _status.Text = $"Reroll failed: {ex.Message}";
        }
    }

    /// <summary>Writes every control value into the working clone (no checksum/close).
    /// Shared by Re-check (preview) and Apply.</summary>
    private void WriteControlsToPk()
    {
        _pk.Nickname = _nick.Text ?? string.Empty;
        _pk.IsNicknamed = _nicknamed.IsChecked == true;

        if (_level.Value is { } lv)
            _pk.CurrentLevel = (byte)Math.Clamp((int)lv, 1, 100);

        // PID first: the Gen3-5 nature/gender/ability writes below may re-embed their value into
        // the PID (rerolling it), and those rerolls must win over the stale textbox value.
        // 8-digit hex; an unparseable value leaves the PID unchanged (same policy as unknown move
        // names). A manual edit on a Gen3-5 entity stored in a modern format must keep
        // EncryptionConstant == PID (the SetPID* helpers do this themselves).
        var pidText = _pid.Text?.Trim();
        if (pidText?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true)
            pidText = pidText[2..];
        if (uint.TryParse(pidText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var pid) && pid != _pk.PID)
        {
            _pk.PID = pid;
            if (_pk.Format >= 6 && (_pk.Gen3 || _pk.Gen4 || _pk.Gen5))
                _pk.EncryptionConstant = pid;
        }

        if (_nature.SelectedItem is Opt n)
        {
            var nat = (Nature)n.Value;
            if (_pk.Format is 3 or 4 && _pk.Nature != nat)
                _pk.SetPIDNature(nat); // Gen3/4: nature is PID-derived (the plain setter is a no-op)
            else
                _pk.Nature = nat;
        }
        if (_gender.SelectedItem is Opt g)
        {
            var gv = (byte)g.Value;
            if (_pk.Format <= 5 && _pk.Gender != gv)
                _pk.SetPIDGender(gv); // Gen3-5: gender is PID-derived; embed it via reroll
            _pk.Gender = gv;
        }
        if (_ability.SelectedItem is Opt a)
        {
            // Gen3-5 formats derive the non-hidden ability slot from the PID (pk.PIDAbility), so a
            // slot change must be embedded into the PID via SetAbilityIndex (rerolls it). Plain
            // RefreshAbility only sets the stored ability byte: the combo would revert on the next
            // load, and Reroll (which preserves the old PID ability bit) appears to change the
            // ability. Hidden abilities (slot 2) and modern formats keep the RefreshAbility path.
            if (_pk.Format <= 5 && a.Value < 2 && _pk.PIDAbility > -1 && a.Value != _pk.PIDAbility)
                _pk.SetAbilityIndex(a.Value);
            else
                _pk.RefreshAbility(a.Value);
        }
        if (_ball.SelectedItem is Opt b)
            _pk.Ball = (byte)b.Value;
        if (_otGender.SelectedItem is Opt og)
            _pk.OriginalTrainerGender = (byte)og.Value;
        if (_form.SelectedItem is Opt fo)
            _pk.Form = (byte)fo.Value;
        _pk.CurrentFriendship = (byte)(_friendship.Value ?? 0);

        int item = ParseItem(_heldItem.Text);
        if (item >= 0)
            _pk.HeldItem = item;

        ApplyMove(_move1.Text, v => _pk.Move1 = v);
        ApplyMove(_move2.Text, v => _pk.Move2 = v);
        ApplyMove(_move3.Text, v => _pk.Move3 = v);
        ApplyMove(_move4.Text, v => _pk.Move4 = v);
        _pk.Move1_PPUps = (int)(_move1Pp.Value ?? 0); // set PP-Ups before FixMoves so reordering carries them
        _pk.Move2_PPUps = (int)(_move2Pp.Value ?? 0);
        _pk.Move3_PPUps = (int)(_move3Pp.Value ?? 0);
        _pk.Move4_PPUps = (int)(_move4Pp.Value ?? 0);
        _pk.FixMoves(); // reorder empties to the end + zero their PP/PP-Ups
        _pk.HealPP();   // set current PP to the max for each move's PP-Ups (avoids PP legality flags)

        _pk.IV_HP = (int)(_ivHp.Value ?? 0); _pk.IV_ATK = (int)(_ivAtk.Value ?? 0); _pk.IV_DEF = (int)(_ivDef.Value ?? 0);
        _pk.IV_SPE = (int)(_ivSpe.Value ?? 0); _pk.IV_SPA = (int)(_ivSpa.Value ?? 0); _pk.IV_SPD = (int)(_ivSpd.Value ?? 0);
        if (_pk is IAwakened av)
        {
            // EVs are left untouched (always 0 on a legal PB7).
            static byte Av(NumericUpDown n) => (byte)Math.Clamp((int)(n.Value ?? 0), 0, AwakeningUtil.AwakeningMax);
            av.AV_HP = Av(_evHp); av.AV_ATK = Av(_evAtk); av.AV_DEF = Av(_evDef);
            av.AV_SPE = Av(_evSpe); av.AV_SPA = Av(_evSpa); av.AV_SPD = Av(_evSpd);
        }
        else
        {
            _pk.EV_HP = (int)(_evHp.Value ?? 0); _pk.EV_ATK = (int)(_evAtk.Value ?? 0); _pk.EV_DEF = (int)(_evDef.Value ?? 0);
            _pk.EV_SPE = (int)(_evSpe.Value ?? 0); _pk.EV_SPA = (int)(_evSpa.Value ?? 0); _pk.EV_SPD = (int)(_evSpd.Value ?? 0);
        }

        _pk.OriginalTrainerName = _otName.Text ?? string.Empty;
        _pk.DisplayTID = (uint)Math.Max(0, _tid.Value ?? 0);
        _pk.DisplaySID = (uint)Math.Max(0, _sid.Value ?? 0);
        _pk.MetLevel = (byte)Math.Clamp((int)(_metLevel.Value ?? 0), 0, 100);
        if (_metLocation.SelectedItem is Opt ml)
            _pk.MetLocation = (ushort)ml.Value;
        // Met date: null clears it (setter is a no-op on formats that don't store a met date).
        _pk.MetDate = _metDate.SelectedDate is { } dto ? DateOnly.FromDateTime(dto.DateTime) : null;

        _markForm?.Save();
        _ribbonForm?.Save();
    }

    /// <summary>Writes the controls back into the clone, then re-renders sprite + legality (stays open).</summary>
    private void Recheck()
    {
        try
        {
            WriteControlsToPk();
            LoadFromPk();   // reflect any normalisation (e.g. reordered moves) back into the controls
            _status.Text = "Re-checked.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _status.Text = $"Re-check failed: {ex.Message}";
        }
    }

    private void ApplyAndClose()
    {
        try
        {
            WriteControlsToPk();
            _pk.RefreshChecksum();
            _onApply?.Invoke(_pk);
            Close();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _status.Text = $"Apply failed: {ex.Message}";
        }
    }

    private void ApplyMove(string? text, Action<ushort> set)
    {
        int id = ParseMove(text);
        if (id >= 0)
            set((ushort)id);
        // id < 0 means an unrecognised name was typed — leave that move slot unchanged.
    }

    private void RefreshSprite()
    {
        try { _sprite.Source = SpriteConvert.FromPkm(_pk); }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
    }

    private void RefreshLegality()
    {
        try
        {
            var la = new LegalityAnalysis(_pk);
            _legalityText.Text = la.Valid ? "Legal ✓" : "ILLEGAL ⚠";
            _legalityReport.Text = la.Report();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _legalityText.Text = string.Empty;
            _legalityReport.Text = $"Legality check error: {ex.Message}";
        }
    }
}
