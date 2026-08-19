using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
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
    private readonly ObservableCollection<SlotVm> _slots = [];
    private readonly ObservableCollection<SlotVm> _party = [];

    private readonly TextBlock _titleText, _trainerText, _boxLabel, _selText, _partyLabel;
    private readonly Button _loadButton, _prevButton, _nextButton, _saveButton, _gen5Button, _gen6Button;
    private readonly Image _wallpaper;
    private readonly ItemsControl _boxGrid, _partyGrid;

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
        _loadButton = this.FindControl<Button>("LoadButton")!;
        _prevButton = this.FindControl<Button>("PrevButton")!;
        _nextButton = this.FindControl<Button>("NextButton")!;
        _saveButton = this.FindControl<Button>("SaveButton")!;
        _gen5Button = this.FindControl<Button>("Gen5Button")!;
        _gen6Button = this.FindControl<Button>("Gen6Button")!;
        _wallpaper = this.FindControl<Image>("Wallpaper")!;
        _boxGrid = this.FindControl<ItemsControl>("BoxGrid")!;
        _partyGrid = this.FindControl<ItemsControl>("PartyGrid")!;

        _boxGrid.ItemsSource = _slots;
        _partyGrid.ItemsSource = _party;
        _loadButton.Click += async (_, _) => await OpenFromPickerAsync();
        _prevButton.Click += (_, _) => ChangeBox(-1);
        _nextButton.Click += (_, _) => ChangeBox(+1);
        _saveButton.Click += async (_, _) => await SaveCurrentToDiskAsync();
        _gen5Button.Click += (_, _) => OpenGen5Tools();
        _gen6Button.Click += (_, _) => OpenGen6Tools();
        _boxGrid.AddHandler(Button.ClickEvent, OnSlotClick);
        _partyGrid.AddHandler(Button.ClickEvent, OnSlotClick);
        _boxGrid.DoubleTapped += OnSlotDoubleTap;
        _partyGrid.DoubleTapped += OnSlotDoubleTap;
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
            if (isParty)
            {
                edited.ResetPartyStats(); // recompute stored battle stats after level/IV/EV edits
                Sav.SetPartySlotAtIndex(edited, index);
            }
            else
            {
                Sav.SetBoxSlotAtIndex(edited, _box, index);
            }

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
            RenderBox();
            _gen5Button.IsEnabled = sav is SAV5;
            _gen6Button.IsEnabled = sav is SAV6;
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

    private void OpenGen5Tools()
    {
        if (Sav is not SAV5)
        {
            _trainerText.Text = "Gen 5 tools require a BW/BW2 save.";
            return;
        }
        var owner = TopLevel.GetTopLevel(this) as Window;
        var win = new Gen5ToolsWindow(Sav, LoadedPath, SaveCurrentToDiskAsync);
        if (owner is not null)
            win.Show(owner);
        else
            win.Show();
    }

    private void OpenGen6Tools()
    {
        if (Sav is not SAV6)
        {
            _trainerText.Text = "Gen 6 tools require an XY/ORAS save.";
            return;
        }
        var owner = TopLevel.GetTopLevel(this) as Window;
        var win = new Gen6ToolsWindow(Sav, LoadedPath, SaveCurrentToDiskAsync);
        if (owner is not null)
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
            if (data[i].Species == 0 && !Sav.IsBoxSlotOverwriteProtected(_box, i))
            {
                slot = i;
                return true;
            }
        }
        return false;
    }

    /// <summary>Clears the currently selected slot (used for a true move-out).</summary>
    public void ClearSelectedSlot()
    {
        if (Sav is null || SelectedSlot < 0) return;
        if (SelectedFromParty)
        {
            Sav.DeletePartySlot(SelectedSlot);
            ClearSelection();
            RenderBox();
            return;
        }
        if (Sav.IsBoxSlotOverwriteProtected(_box, SelectedSlot)) return;
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
            var speciesName = SpeciesName.GetSpeciesName(pk.Species, LanguageEnglish);
            var badge = SpriteConvert.ToAvalonia(SpriteUtil.GetLegalIndicator(IsLegal(pk)));
            _slots.Add(new SlotVm(SpriteConvert.FromPkm(pk), speciesName, i, pk, badge));
        }

        var name = (Sav as IBoxDetailNameRead)?.GetBoxName(_box);
        var label = string.IsNullOrWhiteSpace(name) ? $"Box {_box + 1}" : name;
        _boxLabel.Text = $"{label} ({_box + 1}/{Sav.BoxCount})";

        RenderParty();
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
