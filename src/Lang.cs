using System;

namespace ValheimTweaks
{
    /// <summary>
    /// In-game text, in the language the player picked in the game menu.
    ///
    /// The string pair lives at the call site (Lang.T("English", "Português"))
    /// instead of a central dictionary: no key to mistype, no missing entry, and
    /// the translator sees both versions side by side. Reuse is rare enough that
    /// the small duplication is cheaper than key management.
    ///
    /// Only text the player reads in-game goes through here. Log lines and the
    /// F1 config menu are English-only on purpose: the Configuration Manager
    /// cannot translate setting descriptions (it renders the raw string and
    /// writes it into the .cfg), and logs are developer-facing.
    /// </summary>
    internal static class Lang
    {
        internal static bool IsPortuguese
        {
            get
            {
                // Localization may not exist yet on the main menu; English is a
                // safe fallback, and the first T() call after load gets it right.
                try
                {
                    var loc = Localization.instance;
                    return loc != null
                        && loc.GetSelectedLanguage().StartsWith("Portuguese", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static string T(string english, string portuguese)
            => IsPortuguese ? portuguese : english;
    }
}
