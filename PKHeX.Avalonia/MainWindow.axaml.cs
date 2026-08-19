using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PKHeX.Core;

namespace PKHeX.Avalonia;

public partial class MainWindow : Window
{
    private readonly SavePanel _left;
    private readonly SavePanel _right;
    private readonly Button _moveRight;
    private readonly Button _moveLeft;
    private readonly CheckBox _removeSource;
    private readonly TextBlock _moveStatus;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _left = this.FindControl<SavePanel>("LeftPanel")!;
        _right = this.FindControl<SavePanel>("RightPanel")!;
        _moveRight = this.FindControl<Button>("MoveRightButton")!;
        _moveLeft = this.FindControl<Button>("MoveLeftButton")!;
        _removeSource = this.FindControl<CheckBox>("RemoveSourceCheck")!;
        _moveStatus = this.FindControl<TextBlock>("MoveStatus")!;

        _left.Title = "Save A";
        _right.Title = "Save B";

        _moveRight.Click += (_, _) => Move(_left, _right);
        _moveLeft.Click += (_, _) => Move(_right, _left);
    }

    private void Move(SavePanel from, SavePanel to)
    {
        if (from.Sav is null || to.Sav is null)
        {
            _moveStatus.Text = "Load a save on both sides first.";
            return;
        }
        var pk = from.SelectedPk;
        if (pk is null || pk.Species == 0)
        {
            _moveStatus.Text = "Select a Pokémon in the source panel first.";
            return;
        }
        if (!to.TryGetEmptySlot(out var slot))
        {
            _moveStatus.Text = "Destination box has no free slot — switch to a box with space.";
            return;
        }

        // Convert to the destination's format if the two saves are different generations.
        if (!EntityConverter.TryMakePKMCompatible(pk, to.Sav.BlankPKM, out var result, out var converted) || !result.IsSuccess)
        {
            _moveStatus.Text = $"Can't transfer: {result} (incompatible between these games).";
            return;
        }

        try
        {
            converted.RefreshChecksum();
            to.Sav.SetBoxSlotAtIndex(converted, to.CurrentBox, slot);
            to.Refresh();

            var name = SpeciesName.GetSpeciesName(converted.Species, 2);
            if (_removeSource.IsChecked == true)
            {
                from.ClearSelectedSlot();
                _moveStatus.Text = $"Moved {name} → {(from == _left ? "B" : "A")} (slot {slot + 1}). Removed from source.";
            }
            else
            {
                _moveStatus.Text = $"Copied {name} → {(from == _left ? "B" : "A")} (slot {slot + 1}). Use each side's Save to write to disk.";
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            _moveStatus.Text = $"Transfer failed: {ex.Message}";
        }
    }
}
