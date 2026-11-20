using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives; // UniformGrid
using Avalonia.Controls.Templates; // FuncTemplate
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using PKHeX.Core;
using PKHeX.Drawing.Misc;       // sav.WallpaperImage()
using PKHeX.Drawing.PokeSprite; // SpriteUtil

namespace PKHeX.Avalonia;

public partial class SavePanel : UserControl
{
    private const int LanguageEnglish = 2;

    private int _box;
    /// <summary>"Nursery" is the Gen 7+ name for what earlier games called the Day Care.</summary>
    private string _nurseryTitle = "Nursery";
    private readonly ObservableCollection<SlotVm> _slots = [];
    private readonly ObservableCollection<SlotVm> _party = [];
    private readonly ObservableCollection<SlotVm> _nursery = [];

    private readonly TextBlock _titleText, _trainerText, _boxLabel, _selText, _partyLabel, _nurseryLabel;
    private readonly Button _loadButton, _prevButton, _nextButton, _saveButton;
    private readonly StackPanel _toolsPanel;
    private readonly Image _wallpaper;
    private readonly ItemsControl _boxGrid, _partyGrid, _nurseryGrid;
    private readonly StackPanel _nurseryPanel;

    public SaveFile? Sav { get; private set; }
    public string? LoadedPath { get; private set; }
    public int CurrentBox => _box;
    public int SelectedSlot { get; private set; } = -1;
    public PKM? SelectedPk { get; private set; }
    /// <summary>True when the current selection is a party member (vs a box slot).</summary>
    public bool SelectedFromParty { get; private set; }

    /// <summary>Raised when the user selects (or clears) a slot in this panel.</summary>
    public event Action? SelectionChanged;

    public string Title { set => _titleText.Text = value; }

    public SavePanel()
    {
        AvaloniaXamlLoader.Load(this);
        _titleText = this.FindControl<TextBlock>("TitleText")!;
        _trainerText = this.FindControl<TextBlock>("TrainerText")!;
        _boxLabel = this.FindControl<TextBlock>("BoxLabel")!;
        _selText = this.FindControl<TextBlock>("SelText")!;
        _partyLabel = this.FindControl<TextBlock>("PartyLabel")!;
        _nurseryLabel = this.FindControl<TextBlock>("NurseryLabel")!;
        _nurseryPanel = this.FindControl<StackPanel>("NurseryPanel")!;
        _nurseryGrid = this.FindControl<ItemsControl>("NurseryGrid")!;
        _loadButton = this.FindControl<Button>("LoadButton")!;
        _prevButton = this.FindControl<Button>("PrevButton")!;
        _nextButton = this.FindControl<Button>("NextButton")!;
        _saveButton = this.FindControl<Button>("SaveButton")!;
        _toolsPanel = this.FindControl<StackPanel>("ToolsPanel")!;
        _wallpaper = this.FindControl<Image>("Wallpaper")!;
        _boxGrid = this.FindControl<ItemsControl>("BoxGrid")!;
        _partyGrid = this.FindControl<ItemsControl>("PartyGrid")!;

        _boxGrid.ItemsSource = _slots;
        _partyGrid.ItemsSource = _party;
        _nurseryGrid.ItemsSource = _nursery;
        _loadButton.Click += async (_, _) => await OpenFromPickerAsync();
        _prevButton.Click += (_, _) => ChangeBox(-1);
        _nextButton.Click += (_, _) => ChangeBox(+1);
        _saveButton.Click += async (_, _) => await SaveCurrentToDiskAsync();
        _boxGrid.AddHandler(Button.ClickEvent, OnSlotClick);
        _partyGrid.AddHandler(Button.ClickEvent, OnSlotClick);
        _boxGrid.DoubleTapped += OnSlotDoubleTap;
        _partyGrid.DoubleTapped += OnSlotDoubleTap;
        // Nursery is read-only: it gets its own click handler so a daycare slot can never
        // become the transfer/delete selection (which is indexed against box/party storage).
        _nurseryGrid.AddHandler(Button.ClickEvent, OnNurseryClick);
    }

    private void OnNurseryClick(object? sender, RoutedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not SlotVm { Pk: { Species: > 0 } pk } vm)
            return;
        var name = SpeciesName.GetSpeciesName(pk.Species, LanguageEnglish);
        var gender = pk.Gender switch { 0 => " ♂", 1 => " ♀", _ => "" };
        var egg = pk.IsEgg ? " (egg)" : $" — Lv. {pk.CurrentLevel}";
        var nature = NatureDisplay.GetLabel(pk.Nature);
        _selText.Text = $"{_nurseryTitle} {vm.Index + 1}: {name}{gender}{egg} — {nature} (read-only)";
    }

    private void OnSlotDoubleTap(object? sender, TappedEventArgs e)
    {
        if (Sav is null) return;
        if ((e.Source as Control)?.DataContext is SlotVm vm && vm.Pk is { Species: > 0 } pk)
            OpenEditor(vm.Index, vm.IsParty, pk);
    }

    /// <summary>Opens the rich editor on a CLONE of the slot's Pokémon; applies edits back on save.</summary>
    private void OpenEditor(int index, bool isParty, PKM pk)
    {
        if (Sav is null) return;
        SpriteUtil.Initialize(Sav); // editor sprites must use this save's builder (multi-save safe)
        var owner = TopLevel.GetTopLevel(this) as Window;
        var editor = new EditPokemonWindow(pk, edited => ApplyEditedPk(edited, index, isParty));
        if (owner is not null)
            editor.Show(owner);
        else
            editor.Show();
    }

    private void ApplyEditedPk(PKM edited, int index, bool isParty)
    {
        if (Sav is null) return;
        try
        {
            // Let's Go stores party-format data (with battle stats) in its boxes too, and Core's
            // SetPartyValues skips the recompute when stats are already present — so a level/IV/AV
            // edit would keep stale stats unless we reset them here.
            if (isParty || Sav is SAV7b)
                edited.ResetPartyStats(); // recompute stored battle stats after level/IV/EV edits

            if (isParty)
                Sav.SetPartySlotAtIndex(edited, index); // LGPE: writes through the party pointer into box storage
            else
                Sav.SetBoxSlotAtIndex(edited, _box, index);

            RenderBox(); // re-renders box + party + legality badges
            var name = SpeciesName.GetSpeciesName(edited.Species, LanguageEnglish);
            var where = isParty ? "party" : "slot";
            _selText.Text = $"Edited: {name} ({where} {index + 1}) — {(IsLegal(edited) ? "Legal ✓" : "ILLEGAL ⚠")}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _trainerText.Text = $"Edit failed: {ex.Message}";
        }
    }

    private void OnSlotClick(object? sender, RoutedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is SlotVm vm && vm.Pk is { Species: > 0 } pk)
        {
            SelectedSlot = vm.Index;
            SelectedPk = pk;
            SelectedFromParty = vm.IsParty;
            var name = SpeciesName.GetSpeciesName(pk.Species, LanguageEnglish);
            var where = vm.IsParty ? "party" : "slot";
            _selText.Text = $"Selected: {name} ({where} {vm.Index + 1}) — {(IsLegal(pk) ? "Legal ✓" : "ILLEGAL ⚠")}";
            SelectionChanged?.Invoke();
        }
    }

    private static readonly FilePickerFileType SaveFileType = new("Pokémon save files")
    {
        Patterns = ["*.sav", "*.dsv", "*.dat", "*.bin", "*.srm", "*.gci", "*.raw"],
    };

    private IStorageProvider? Storage => TopLevel.GetTopLevel(this)?.StorageProvider;

    private async Task OpenFromPickerAsync()
    {
        var sp = Storage;
        if (sp is null) { _trainerText.Text = "No storage provider available."; return; }
        try
        {
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open save file",
                AllowMultiple = false,
                FileTypeFilter = [SaveFileType, FilePickerFileTypes.All],
                SuggestedStartLocation = await GetStartFolderAsync(sp),
            });
            if (files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (path is null) { _trainerText.Text = "Could not resolve a local file path."; return; }
            LoadFromPath(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _trainerText.Text = $"Open failed: {ex.Message}";
        }
    }

    private async Task<IStorageFolder?> GetStartFolderAsync(IStorageProvider sp)
    {
        var dir = LoadedPath is not null ? Path.GetDirectoryName(LoadedPath) : null;
        if (string.IsNullOrEmpty(dir))
            return null;
        return await sp.TryGetFolderFromPathAsync(dir);
    }

    private void LoadFromPath(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) { _trainerText.Text = "Enter a save path."; return; }
            path = path.Trim().Trim('"');
            if (!File.Exists(path)) { _trainerText.Text = "File not found."; return; }
            var sav = SaveUtil.GetSaveFile(path);
            if (sav is null) { _trainerText.Text = "Unrecognized save file."; return; }

            Sav = sav;
            LoadedPath = path;
            _box = 0;
            ClearSelection();
            UpdateBoxColumns();
            RenderBox();
            BuildToolButtons();
            _trainerText.Text = $"{sav.GetType().Name} — OT {sav.OT} ({sav.Version})";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _trainerText.Text = $"Load failed: {ex.Message}";
        }
    }

    /// <summary>Writes the in-memory save to a user-chosen file via the native picker.
    /// Returns the output path (null on cancel/failure).</summary>
    public async Task<string?> SaveCurrentToDiskAsync()
    {
        if (Sav is null) { _trainerText.Text = "No save loaded."; return null; }
        var sp = Storage;
        if (sp is null) { _trainerText.Text = "No storage provider available."; return null; }
        try
        {
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save file as",
                SuggestedFileName = LoadedPath is not null ? Path.GetFileName(LoadedPath) : "save.sav",
                FileTypeChoices = [SaveFileType, FilePickerFileTypes.All],
                SuggestedStartLocation = await GetStartFolderAsync(sp),
            });
            if (file is null) return null;

            // Let's Go keeps its 1000 storage slots as a packed list (no gaps allowed); a transfer
            // into a later box or a move-out leaves holes. Core compacts the list and re-points the
            // party/starter indexes — same pre-write step WinForms' ExportSaveFile runs. Slots may
            // shift, so drop the selection and re-render.
            if (Sav is IStorageCleanup cleanup && cleanup.FixStoragePreWrite())
            {
                ClearSelection();
                RenderBox();
            }

            var bytes = Sav.Write().ToArray();
            await using (var stream = await file.OpenWriteAsync())
                await stream.WriteAsync(bytes);

            _trainerText.Text = $"Saved → {file.Name}";
            return file.TryGetLocalPath() ?? file.Name;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _trainerText.Text = $"Save failed: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// One per-generation tool window. <c>Match</c> decides whether the loaded save has the feature
    /// blocks the window edits; <c>Label</c> names the games rather than the generation number (a
    /// "Gen4 Tools" button on a Diamond save would be a lie — Pokéathlon is HG/SS only); <c>Create</c>
    /// builds the window with the live save, the loaded path and the persist callback.
    /// </summary>
    private sealed record ToolWindow(
        string Label,
        Func<SaveFile, bool> Match,
        Func<SaveFile, string?, Func<Task<string?>>?, Window> Create);

    /// <summary>
    /// The tool windows this panel can open, in button order. Adding a generation is one entry here
    /// — no XAML, no field, no click handler. <see cref="BuildToolButtons"/> only materialises the
    /// entries that match the save that's actually loaded.
    /// </summary>
    private static readonly ToolWindow[] ToolWindows =
    [
        new("HG/SS Tools", s => s is SAV4HGSS, (s, p, persist) => new Gen4ToolsWindow(s, p, persist)),
        new("BW/BW2 Tools", s => s is SAV5, (s, p, persist) => new Gen5ToolsWindow(s, p, persist)),
        new("XY/ORAS Tools", s => s is SAV6, (s, p, persist) => new Gen6ToolsWindow(s, p, persist)),
        new("Let's Go Tools", s => s is SAV7b, (s, p, persist) => new LgpeToolsWindow(s, p, persist)),
    ];

    /// <summary>
    /// Rebuilds the tool button row for the loaded save. Buttons are created on load rather than
    /// declared in XAML and greyed out: a disabled button for every generation we support would keep
    /// growing the row until the applicable one scrolled out of view.
    /// </summary>
    private void BuildToolButtons()
    {
        _toolsPanel.Children.Clear();
        if (Sav is not { } sav)
            return;
        foreach (var tool in ToolWindows)
        {
            if (!tool.Match(sav))
                continue;
            var button = new Button { Content = $"🛠 {tool.Label}" };
            button.Click += (_, _) => OpenToolWindow(tool);
            _toolsPanel.Children.Add(button);
        }
    }

    private void OpenToolWindow(ToolWindow tool)
    {
        // Re-checked at click time: the panel can load a different save after the button was built.
        if (Sav is not { } sav || !tool.Match(sav))
        {
            _trainerText.Text = $"{tool.Label} doesn't apply to the loaded save.";
            return;
        }
        var win = tool.Create(sav, LoadedPath, SaveCurrentToDiskAsync);
        if (TopLevel.GetTopLevel(this) is Window owner)
            win.Show(owner);
        else
            win.Show();
    }

    private void ChangeBox(int delta)
    {
        if (Sav is null) return;
        var count = Sav.BoxCount;
        if (count <= 0) return;
        _box = (((_box + delta) % count) + count) % count;
        ClearSelection();
        RenderBox();
    }

    /// <summary>Re-render the current box (call after an external change writes into this save).</summary>
    public void Refresh() => RenderBox();

    /// <summary>Finds the first writable empty slot in the current box.</summary>
    public bool TryGetEmptySlot(out int slot)
    {
        slot = -1;
        if (Sav is null) return false;
        var data = Sav.GetBoxData(_box);
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i].Species == 0 && !IsSlotProtected(_box, i))
            {
                slot = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether <see cref="ClearSelectedSlot"/> may remove the current selection. Checked by the
    /// transfer view <em>before</em> it copies, so a refused move-out never leaves a duplicate.
    /// </summary>
    public bool CanClearSelectedSlot(out string reason)
    {
        reason = string.Empty;
        if (Sav is null || SelectedSlot < 0) { reason = "nothing selected"; return false; }
        if (Sav is SAV7b && SelectedFromParty)
        {
            // LGPE party slots are pointers into box storage; Core's DeletePartySlot shuffles the
            // pointed-at box data instead of the pointers, which would scramble the boxes.
            reason = "Let's Go party members can't be removed here (deposit them in-game first)";
            return false;
        }
        if (!SelectedFromParty && IsSlotProtected(_box, SelectedSlot))
        {
            reason = "that slot is locked, the starter, or in the party";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Overwrite protection plus party backing. Core's <c>IsOverwriteProtected</c> covers locked /
    /// starter / battle-team slots but not party membership — and on Let's Go a party member *is* a
    /// box slot, so blanking it would leave the party pointing at an empty slot.
    /// </summary>
    private bool IsSlotProtected(int box, int slot)
    {
        if (Sav is null) return true;
        var flags = Sav.GetBoxSlotFlags(box, slot);
        return flags.IsOverwriteProtected() || flags.IsParty() >= 0;
    }

    /// <summary>Clears the currently selected slot (used for a true move-out).</summary>
    public void ClearSelectedSlot()
    {
        if (Sav is null || !CanClearSelectedSlot(out _)) return;
        if (SelectedFromParty)
        {
            Sav.DeletePartySlot(SelectedSlot);
            ClearSelection();
            RenderBox();
            return;
        }
        Sav.SetBoxSlotAtIndex(Sav.BlankPKM, _box, SelectedSlot);
        ClearSelection();
        RenderBox();
    }

    private static bool IsLegal(PKM pk)
    {
        try { return new LegalityAnalysis(pk).Valid; }
        catch (Exception ex) { Console.Error.WriteLine($"legality {pk.Species}: {ex.Message}"); return false; }
    }

    private void ClearSelection()
    {
        SelectedSlot = -1;
        SelectedPk = null;
        SelectedFromParty = false;
        _selText.Text = "No selection";
    }

    private void RenderBox()
    {
        _slots.Clear();
        if (Sav is null) { _boxLabel.Text = "—"; return; }

        SpriteUtil.Initialize(Sav); // global sprite builder follows this save (multi-save safe per render)
        _wallpaper.Source = SpriteConvert.ToAvalonia(Sav.WallpaperImage(_box));

        var data = Sav.GetBoxData(_box);
        for (int i = 0; i < data.Length; i++)
        {
            var pk = data[i];
            if (pk.Species == 0) { _slots.Add(new SlotVm(null, string.Empty, i, pk)); continue; }
            var speciesName = SpeciesName.GetSpeciesName(pk.Species, LanguageEnglish) + GetSlotTag(_box, i);
            var badge = SpriteConvert.ToAvalonia(SpriteUtil.GetLegalIndicator(IsLegal(pk)));
            _slots.Add(new SlotVm(SpriteConvert.FromPkm(pk), speciesName, i, pk, badge));
        }

        var name = (Sav as IBoxDetailNameRead)?.GetBoxName(_box);
        var label = string.IsNullOrWhiteSpace(name) ? $"Box {_box + 1}" : name;
        _boxLabel.Text = $"{label} ({_box + 1}/{Sav.BoxCount})";

        RenderParty();
        RenderNursery();
    }

    /// <summary>
    /// Suffix marking box slots the save treats specially. Only Let's Go (and Stadium) report party
    /// membership on a box slot — their party *is* a set of pointers into box storage — so this is
    /// what tells the user that a box Pokémon is also in the party row above.
    /// </summary>
    private string GetSlotTag(int box, int slot)
    {
        if (Sav is null) return string.Empty;
        var flags = Sav.GetBoxSlotFlags(box, slot);
        var tag = string.Empty;
        if (flags.HasFlag(StorageSlotSource.Starter)) tag += " ★";
        if (flags.IsParty() is >= 0 and var p) tag += $"\n(party {p + 1})";
        if (flags.HasFlag(StorageSlotSource.Locked)) tag += " 🔒";
        return tag;
    }

    /// <summary>
    /// Box grid column count: 6 for the usual 30-slot box, 5 for Let's Go's 25-slot "boxes" (its
    /// 1000-slot list paged by Core) and Gen 1/2's 20-slot boxes, so rows come out full.
    /// </summary>
    private void UpdateBoxColumns()
    {
        var count = Sav?.BoxSlotCount ?? 30;
        var columns = count % 6 == 0 ? 6 : count % 5 == 0 ? 5 : 6;
        _boxGrid.ItemsPanel = new FuncTemplate<Panel?>(() => new UniformGrid { Columns = columns });
    }

    /// <summary>
    /// Renders the Day Care / Nursery occupants. Generation-agnostic: any save implementing
    /// <see cref="IDaycareStorage"/> (Gen 1–8) is covered; saves without one hide the row.
    /// </summary>
    private void RenderNursery()
    {
        _nursery.Clear();
        var sav = Sav;
        if (sav is not IDaycareStorage dc)
        {
            _nurseryPanel.IsVisible = false;
            return;
        }

        _nurseryPanel.IsVisible = true;
        SpriteUtil.Initialize(sav); // ensure sprites use this save's builder

        var count = dc.DaycareSlotCount;
        var occupied = 0;
        for (int i = 0; i < count; i++)
        {
            var pk = GetDaycarePk(sav, dc, i);
            if (pk is null || pk.Species == 0)
            {
                _nursery.Add(new SlotVm(null, "(empty)", i));
                continue;
            }
            occupied++;
            var speciesName = SpeciesName.GetSpeciesName(pk.Species, LanguageEnglish);
            // Nature matters for breeding (Everstone), so show it on eggs too even though the game hides it.
            var line1 = pk.IsEgg ? $"{speciesName} (egg)" : $"{speciesName}\nLv. {pk.CurrentLevel}";
            var label = $"{line1}\n{NatureDisplay.GetName(pk.Nature)}";
            var badge = SpriteConvert.ToAvalonia(SpriteUtil.GetLegalIndicator(IsLegal(pk)));
            _nursery.Add(new SlotVm(SpriteConvert.FromPkm(pk), label, i, pk, badge));
        }

        _nurseryTitle = sav.Generation >= 7 ? "Nursery" : "Day Care";
        var eggReady = sav is IDaycareEggState { IsEggAvailable: true } ? " — egg available! 🥚" : "";
        _nurseryLabel.Text = occupied == 0
            ? $"{_nurseryTitle} (empty){eggReady}"
            : $"{_nurseryTitle} ({occupied}/{count}){eggReady}";
    }

    /// <summary>Decodes the stored entity in a daycare slot, or null when the slot is vacant/unreadable.</summary>
    private static PKM? GetDaycarePk(SaveFile sav, IDaycareStorage dc, int index)
    {
        try
        {
            if (!dc.IsDaycareOccupied(index))
                return null;
            var span = dc.GetDaycareSlot(index).Span;
            return span.Length < sav.SIZE_STORED ? null : sav.GetStoredSlot(span);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"daycare slot {index}: {ex.Message}");
            return null;
        }
    }

    private void RenderParty()
    {
        _party.Clear();
        if (Sav is null) { _partyLabel.Text = "Party"; return; }

        SpriteUtil.Initialize(Sav); // ensure sprites use this save's builder
        var party = Sav.PartyData;
        for (int i = 0; i < party.Count; i++)
        {
            var pk = party[i];
            if (pk.Species == 0) continue;
            var speciesName = SpeciesName.GetSpeciesName(pk.Species, LanguageEnglish);
            var badge = SpriteConvert.ToAvalonia(SpriteUtil.GetLegalIndicator(IsLegal(pk)));
            _party.Add(new SlotVm(SpriteConvert.FromPkm(pk), speciesName, i, pk, badge, isParty: true));
        }

        _partyLabel.Text = _party.Count == 0 ? "Party (empty)" : $"Party ({_party.Count}/6)";
    }
}
