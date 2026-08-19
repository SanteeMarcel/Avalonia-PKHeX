using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace PKHeX.Avalonia;

/// <summary>
/// Avalonia's <see cref="ComboBox"/> changes its selection when the mouse wheel moves over it while
/// the dropdown is closed. In scroll-heavy forms (the Pokémon editor, the Gen5 tool tabs) that turns
/// "scroll down to reach a button" into a silent edit of whatever combo the pointer crossed — e.g.
/// the ability slot bumping from "Slot 2" to "Hidden", which the next Apply/Re-check then persists.
/// This guard intercepts those wheel events and scrolls the surrounding <see cref="ScrollViewer"/>
/// instead, so combos only change via an explicit click/keyboard selection.
/// </summary>
internal static class ComboWheelGuard
{
    /// <summary>Pixels scrolled per wheel notch when redirecting a swallowed wheel event.</summary>
    private const double WheelStep = 48;

    /// <summary>Attach to a window so all its closed ComboBoxes ignore the mouse wheel.
    /// Open dropdowns are unaffected (their popup is a separate visual root).</summary>
    public static void Attach(TopLevel root) => root.AddHandler(
        InputElement.PointerWheelChangedEvent,
        static (_, e) =>
        {
            if (e.Source is not Visual src)
                return;
            if (src.FindAncestorOfType<ComboBox>(includeSelf: true) is not { IsDropDownOpen: false } combo)
                return;
            e.Handled = true; // stop the combo from cycling its selection
            var scroll = combo.FindAncestorOfType<ScrollViewer>();
            if (scroll is not null)
                scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y - (e.Delta.Y * WheelStep));
        },
        RoutingStrategies.Tunnel);
}
