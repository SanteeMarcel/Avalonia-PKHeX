using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
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
    private int _box;
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
    private readonly TextBlock _otText;
    private readonly TextBlock _genderText;
    private readonly TextBlock _idText;
    private readonly TextBlock _gameText;
    private readonly TextBlock _moneyText;
    private readonly TextBlock _timeText;
    private readonly TextBlock _statusText;

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

        _boxGrid.ItemsSource = _slots;
        _partyGrid.ItemsSource = _party;
        _loadButton.Click += (_, _) => LoadFromPath(_pathBox.Text);
        _prevButton.Click += (_, _) => ChangeBox(-1);
        _nextButton.Click += (_, _) => ChangeBox(+1);

        UpdateChrome();
    }

    private void LoadFromPath(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _statusText.Text = "Enter or paste a save file path.";
                return;
            }
            path = path.Trim().Trim('"');
            if (!File.Exists(path))
            {
                _statusText.Text = "File not found.";
                return;
            }

            var sav = SaveUtil.GetSaveFile(path);
            if (sav is null)
            {
                _statusText.Text = "Unrecognized save file.";
                return;
            }

            _sav = sav;
            SpriteUtil.Initialize(sav);
            _box = 0;
            RenderTrainer();
            RenderParty();
            RenderBox();
            _statusText.Text = $"Loaded {sav.GetType().Name}";
        }
        catch (System.Exception ex)
        {
            System.Console.Error.WriteLine(ex);
            _statusText.Text = $"Load failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private void ChangeBox(int delta)
    {
        if (_sav is null)
            return;
        var count = _sav.BoxCount;
        if (count <= 0)
            return;
        _box = (((_box + delta) % count) + count) % count;
        RenderBox();
    }

    private void RenderTrainer()
    {
        if (_sav is null)
            return;
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
        if (_sav is null)
            return;
        foreach (var pk in _sav.PartyData)
        {
            if (pk.Species == 0)
                continue;
            _party.Add(new SlotVm(SafeSprite(pk), SpeciesName.GetSpeciesName(pk.Species, LanguageEnglish)));
        }
    }

    private void RenderBox()
    {
        _slots.Clear();
        if (_sav is null)
        {
            UpdateChrome();
            return;
        }

        _wallpaper.Source = TryAvalonia(_sav.WallpaperImage(_box));

        foreach (var pk in _sav.GetBoxData(_box))
        {
            if (pk.Species == 0)
            {
                _slots.Add(new SlotVm(null, string.Empty));
                continue;
            }
            var name = SpeciesName.GetSpeciesName(pk.Species, LanguageEnglish);
            _slots.Add(new SlotVm(SafeSprite(pk), name));
        }
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        if (_sav is null)
        {
            _boxLabel.Text = "—";
            return;
        }
        var name = (_sav as IBoxDetailNameRead)?.GetBoxName(_box);
        var label = string.IsNullOrWhiteSpace(name) ? $"Box {_box + 1}" : name;
        _boxLabel.Text = $"{label}   ({_box + 1}/{_sav.BoxCount})";
    }

    private static Bitmap? SafeSprite(PKM pk)
    {
        try { return TryAvalonia(pk.Sprite()); }
        catch (System.Exception ex) { System.Console.Error.WriteLine($"sprite {pk.Species}: {ex.Message}"); return null; }
    }

    private static Bitmap? TryAvalonia(SKBitmap? skb)
    {
        if (skb is null)
            return null;
        using var image = SKImage.FromBitmap(skb);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }
}

public sealed class SlotVm(Bitmap? image, string label)
{
    public Bitmap? Image { get; } = image;
    public string Label { get; } = label;
}
