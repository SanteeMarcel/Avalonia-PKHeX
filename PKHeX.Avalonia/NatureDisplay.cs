using PKHeX.Core;

namespace PKHeX.Avalonia;

/// <summary>Shared formatting for a Pokémon's <see cref="Nature"/>.</summary>
public static class NatureDisplay
{
    /// <summary>
    /// Short stat names in the "internal" order used by <see cref="NatureAmp.GetNatureModification"/>
    /// (index 0–4 = Atk, Def, Spe, SpA, SpD — i.e. the party-stat order minus HP). Used to label each
    /// nature with the stat it raises/lowers, e.g. Adamant → "(+Atk, -SpA)".
    /// </summary>
    private static readonly string[] StatNames = ["Atk", "Def", "Spe", "SpA", "SpD"];

    /// <summary>True when the value is one of the 25 real natures (excludes Random / out-of-range).</summary>
    public static bool IsRealNature(Nature nature) => (uint)(int)nature < 25;

    /// <summary>Just the nature's name, e.g. "Adamant". Falls back to the raw value if out of range.</summary>
    public static string GetName(Nature nature) => IsRealNature(nature) ? nature.ToString() : $"Nature {(int)nature}";

    /// <summary>
    /// The nature plus the stats it raises/lowers, e.g. "Adamant (+Atk, -SpA)".
    /// Neutral natures (Hardy, Docile, ...) raise and lower the same stat, so they show "(neutral)".
    /// </summary>
    public static string GetLabel(Nature nature)
    {
        if (!IsRealNature(nature))
            return GetName(nature);
        var (up, dn) = nature.GetNatureModification();
        return up == dn
            ? $"{nature} (neutral)"
            : $"{nature} (+{StatNames[up]}, -{StatNames[dn]})";
    }
}
