using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using PKHeX.Core;
using PKHeX.Drawing.Misc;       // sav.WallpaperImage(), sav.Sprite() (trainer)
using PKHeX.Drawing.PokeSprite; // pk.Sprite(), SpriteUtil
using SkiaSharp;

namespace PKHeX.Avalonia;

public partial class MainWindow : Window
{
    private const int LanguageEnglish = 2;

    private SaveFile? _sav;
    private string? _loadedPath;
    private int _box;
    private int _selectedSlot = -1;
    private PKM? _selectedPk;

    private readonly ObservableCollection<SlotVm> _slots = [];
    private readonly ObservableCollection<SlotVm> _party = [];

    private readonly ItemsControl _boxGrid;
    private readonly ItemsControl _partyGrid;
    private readonly TextBox _pathBox;
    private readonly Button _loadButton;
    private readonly Button _prevButton;
    private readonly Button _nextButton;
    private readonly TextBlock _boxLabel;
    private readonly Image _wallpaper;
    private readonly Image _trainerSprite;
    private readonly TextBlock _otText, _genderText, _idText, _gameText, _moneyText, _timeText, _statusText;

    // Edit panel
    private readonly Image _editSprite;
    private readonly TextBlock _editName, _editStatus;
    private readonly TextBox _nickBox, _outPathBox;
    private readonly NumericUpDown _levelBox;
    private readonly ComboBox _natureBox;
    private readonly Button _shinyButton, _applyButton, _saveButton;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _boxGrid = this.FindControl<ItemsControl>("BoxGrid")!;
        _partyGrid = this.FindControl<ItemsControl>("PartyGrid")!;
        _pathBox = this.FindControl<TextBox>("PathBox")!;
        _loadButton = this.FindControl<Button>("LoadButton")!;
        _prevButton = this.FindControl<Button>("PrevButton")!;
        _nextButton = this.FindControl<Button>("NextButton")!;
        _boxLabel = this.FindControl<TextBlock>("BoxLabel")!;
        _wallpaper = this.FindControl<Image>("Wallpaper")!;
        _trainerSprite = this.FindControl<Image>("TrainerSprite")!;
        _otText = this.FindControl<TextBlock>("OtText")!;
        _genderText = this.FindControl<TextBlock>("GenderText")!;
        _idText = this.FindControl<TextBlock>("IdText")!;
        _gameText = this.FindControl<TextBlock>("GameText")!;
        _moneyText = this.FindControl<TextBlock>("MoneyText")!;
        _timeText = this.FindControl<TextBlock>("TimeText")!;
        _statusText = this.FindControl<TextBlock>("StatusText")!;

        _editSprite = this.FindControl<Image>("EditSprite")!;
        _editName = this.FindControl<TextBlock>("EditName")!;
        _editStatus = this.FindControl<TextBlock>("EditStatus")!;
        _nickBox = this.FindControl<TextBox>("NickBox")!;
        _outPathBox = this.FindControl<TextBox>("OutPathBox")!;
        _levelBox = this.FindControl<NumericUpDown>("LevelBox")!;
        _natureBox = this.FindControl<ComboBox>("NatureBox")!;
        _shinyButton = this.FindControl<Button>("ShinyButton")!;
        _applyButton = this.FindControl<Button>("ApplyButton")!;
        _saveButton = this.FindControl<Button>("SaveButton")!;

        _boxGrid.ItemsSource = _slots;
        _partyGrid.ItemsSource = _party;
        _natureBox.ItemsSource = Enum.GetValues<Nature>();

        _loadButton.Click += (_, _) => LoadFromPath(_pathBox.Text);
        _prevButton.Click += (_, _) => ChangeBox(-1);
        _nextButton.Click += (_, _) => ChangeBox(+1);
        _shinyButton.Click += (_, _) => MakeShiny();
        _applyButton.Click += (_, _) => ApplyEdits();
        _saveButton.Click += (_, _) => SaveFile();
        _boxGrid.AddHandler(Button.ClickEvent, OnSlotClick);

        UpdateChrome();
    }

    private void OnSlotClick(object? sender, RoutedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is SlotVm { Pk: { Species: > 0 } pk } vm)
            SelectPokemon(vm.Index, pk);
    }

    private void SelectPokemon(int slot, PKM pk)
    {
        _selectedSlot = slot;
        _selectedPk = pk;
        _editSprite.Source = SafeSprite(pk);
        _editName.Text = SpeciesName.GetSpeciesName(pk.Species, LanguageEnglish);
        _nickBox.Text = pk.Nickname;
        _levelBox.Value = pk.CurrentLevel;
        _natureBox.SelectedItem = pk.Nature;
        _editStatus.Text = $"Editing box slot {slot + 1}.";
    }

    private void MakeShiny()
    {
        if (_selectedPk is null) { _editStatus.Text = "Select a Pokémon first."; return; }
        _selectedPk.SetShiny();
        _editSprite.Source = SafeSprite(_selectedPk);
        _editStatus.Text = "Made shiny — click Apply to write it to the slot.";
    }

    private void LoadFromPath(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) { _statusText.Text = "Enter or paste a save file path."; return; }
            path = path.Trim().Trim('"');
            if (!File.Exists(path)) { _statusText.Text = "File not found."; return; }

            var sav = SaveUtil.GetSaveFile(path);
            if (sav is null) { _statusText.Text = "Unrecognized save file."; return; }

            _sav = sav;
            _loadedPath = path;
            _outPathBox.Text = path + ".out.sav";
            SpriteUtil.Initialize(sav);
            _box = 0;
            _selectedSlot = -1;
            _selectedPk = null;
            RenderTrainer();
            RenderParty();
            RenderBox();
            _statusText.Text = $"Loaded {sav.GetType().Name}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _statusText.Text = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void ApplyEdits()
    {
        if (_sav is null || _selectedPk is null || _selectedSlot < 0)
        {
            _editStatus.Text = "Select a Pokémon first.";
            return;
        }
        try
        {
            var pk = _selectedPk;
            var nick = _nickBox.Text;
            if (!string.IsNullOrWhiteSpace(nick))
            {
                pk.Nickname = nick;
                pk.IsNicknamed = true;
            }
            if (_levelBox.Value is { } lv)
                pk.CurrentLevel = (byte)Math.Clamp((int)lv, 1, 100);
            if (_natureBox.SelectedItem is Nature n)
                pk.Nature = n;

            pk.RefreshChecksum();
            _sav.SetBoxSlotAtIndex(pk, _box, _selectedSlot);
            RenderBox();
            _editStatus.Text = "Applied to slot. Use Save File to write to disk.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _editStatus.Text = $"Apply failed: {ex.Message}";
        }
    }

    private void SaveFile()
    {
        if (_sav is null) { _editStatus.Text = "No save loaded."; return; }
        try
        {
            var outPath = _outPathBox.Text;
            if (string.IsNullOrWhiteSpace(outPath))
                outPath = (_loadedPath ?? "save") + ".out.sav";
            outPath = outPath.Trim().Trim('"');
            File.WriteAllBytes(outPath, _sav.Write().ToArray());
            _editStatus.Text = $"Saved → {outPath}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _editStatus.Text = $"Save failed: {ex.Message}";
        }
    }

    private void ChangeBox(int delta)
    {
        if (_sav is null) return;
        var count = _sav.BoxCount;
        if (count <= 0) return;
        _box = (((_box + delta) % count) + count) % count;
        RenderBox();
    }

    private void RenderTrainer()
    {
        if (_sav is null) return;
        _trainerSprite.Source = TryAvalonia(_sav.Sprite());
        _otText.Text = $"OT: {_sav.OT}";
        _genderText.Text = $"Gender: {(_sav.Gender == 1 ? "♀" : "♂")}";
        _idText.Text = $"ID: {_sav.DisplayTID:D6} / SID {_sav.DisplaySID:D4}";
        _gameText.Text = $"Game: {_sav.Version}";
        _moneyText.Text = $"Money: {_sav.Money:N0}";
        _timeText.Text = $"Play time: {_sav.PlayedHours}h {_sav.PlayedMinutes}m";
    }

    private void RenderParty()
    {
        _party.Clear();
        if (_sav is null) return;
        foreach (var pk in _sav.PartyData)
        {
            if (pk.Species == 0) continue;
            _party.Add(new SlotVm(SafeSprite(pk), SpeciesName.GetSpeciesName(pk.Species, LanguageEnglish)));
        }
    }

    private void RenderBox()
    {
        _slots.Clear();
        if (_sav is null) { UpdateChrome(); return; }

        _wallpaper.Source = TryAvalonia(_sav.WallpaperImage(_box));

        var data = _sav.GetBoxData(_box);
        for (int i = 0; i < data.Length; i++)
        {
            var pk = data[i];
            if (pk.Species == 0)
            {
                _slots.Add(new SlotVm(null, string.Empty, i, pk));
                continue;
            }
            var name = SpeciesName.GetSpeciesName(pk.Species, LanguageEnglish);
            _slots.Add(new SlotVm(SafeSprite(pk), name, i, pk));
        }
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        if (_sav is null) { _boxLabel.Text = "—"; return; }
        var name = (_sav as IBoxDetailNameRead)?.GetBoxName(_box);
        var label = string.IsNullOrWhiteSpace(name) ? $"Box {_box + 1}" : name;
        _boxLabel.Text = $"{label}   ({_box + 1}/{_sav.BoxCount})";
    }

    private static Bitmap? SafeSprite(PKM pk)
    {
        try { return TryAvalonia(pk.Sprite()); }
        catch (Exception ex) { Console.Error.WriteLine($"sprite {pk.Species}: {ex.Message}"); return null; }
    }

    private static Bitmap? TryAvalonia(SKBitmap? skb)
    {
        if (skb is null) return null;
        using var image = SKImage.FromBitmap(skb);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }
}

public sealed class SlotVm(Bitmap? image, string label, int index = -1, PKM? pk = null)
{
    public Bitmap? Image { get; } = image;
    public string Label { get; } = label;
    public int Index { get; } = index;
    public PKM? Pk { get; } = pk;
}
