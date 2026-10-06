using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Runewake.Engine.Supabase;

/// <summary>
/// FABLE-054: what a username may be. Other players see your username — never your email — on lobbies,
/// challenges and duels, so it has to be readable and not obscene.
///
/// Rules: 3–16 characters; letters, numbers, spaces, '_' and '-'; starts and ends with a letter or number;
/// no double spaces. The filter blocks heavy profanity, slurs and crude sexual words, including the
/// usual disguises (l33t letters, dots/spaces between letters, repeated letters), while leaving ordinary
/// words that merely contain one alone (Cassandra, Scunthorpe, Dickens, peacock, raccoon, analyst).
/// The server re-checks the shape and enforces uniqueness (supabase/usernames.sql); the word filter
/// lives here so every place a name is typed uses one list.
/// </summary>
public static class Usernames
{
    public const int MinLength = 3, MaxLength = 16;

    /// <summary>Blocked wherever they appear inside the name (they don't occur inside ordinary words).</summary>
    private static readonly string[] Anywhere =
    {
        "fuck", "fuk", "fck", "phuck", "shit", "sh1t", "cunt", "bitch", "biatch", "whore", "slut", "bastard",
        "asshole", "arsehole", "dickhead", "motherf", "wanker", "twat", "bollock", "jizz", "cumshot", "cumslut",
        "dildo", "blowjob", "handjob", "rimjob", "pussy", "vagina", "penis", "boobs", "titties", "porn", "hentai",
        "anal sex", "analsex", "buttsex", "orgasm", "masturb", "ejacul", "sperm", "milf",
        "molest", "paedo", "pedophil", "incest", "bestiality", "necrophil",
        "nigger", "nigga", "nigg", "faggot", "fagot", "retard", "tranny", "kike", "wetback",
        "raghead", "towelhead", "nazi", "hitler", "kkk", "heilh", "siegheil", "jihad",
        "killyourself", "suicide", "selfharm",
    };

    /// <summary>Real words and places that happen to contain a blocked one.</summary>
    private static readonly string[] Harmless = { "scunthorpe", "shiitake", "shitake", "cockatrice", "spermaceti", "assassin" };

    /// <summary>Blocked only as a whole word of the name (they hide inside harmless words otherwise).</summary>
    private static readonly HashSet<string> WholeWord = new(StringComparer.Ordinal)
    {
        "ass", "arse", "cum", "dick", "cock", "tit", "tits", "fag", "fags", "coon", "spic", "gook", "dyke", "homo",
        "anal", "sex", "sexy", "nude", "nudes", "naked", "hoe", "hoes", "thot", "piss", "poop", "turd", "crap",
        "balls", "knob", "boner", "queef", "smegma", "scrotum", "testicle", "puss", "negro", "wank", "prick",
        "rape", "raped", "rapes", "rapist", "pedo", "pedos", "kys", "horny", "semen", "chink", "beaner", "niga",
    };

    /// <summary>A reason the name can't be used, or null when it's fine.</summary>
    public static string? Problem(string? raw)
    {
        string name = (raw ?? "").Trim();
        if (name.Length < MinLength) return $"At least {MinLength} characters.";
        if (name.Length > MaxLength) return $"At most {MaxLength} characters.";
        if (!name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-')) return "Letters, numbers, spaces, _ and - only.";
        if (!char.IsLetterOrDigit(name[0]) || !char.IsLetterOrDigit(name[^1])) return "Start and end with a letter or number.";
        if (name.Contains("  ")) return "No double spaces.";
        if (!name.Any(char.IsLetter)) return "Use at least one letter.";
        if (IsOffensive(name)) return "Please pick a different name.";
        return null;
    }

    public static bool IsValid(string? raw) => Problem(raw) == null;

    /// <summary>True when the text contains heavy profanity, a slur or something crude.</summary>
    public static bool IsOffensive(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string words = Normalize(text, keepBreaks: true);        // "big ass" → "big ass"
        string squashed = Normalize(text, keepBreaks: false);    // "f.u.c.k" → "fuck"
        foreach (var ok in Harmless) squashed = squashed.Replace(ok, "");
        string collapsed = CollapseRepeats(squashed);            // "fuuuuck" → "fuck"
        foreach (var w in Anywhere)
        {
            string ww = w.Replace(" ", "");
            if (squashed.Contains(ww) || collapsed.Contains(ww)) return true;
        }
        foreach (var token in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (WholeWord.Contains(token) || WholeWord.Contains(CollapseRepeats(token))) return true;
        // the whole name as one word ("A.S.S", "c u m")
        return WholeWord.Contains(squashed) || WholeWord.Contains(collapsed);
    }

    /// <summary>Lower-case, look-alike digits and symbols turned to letters, other separators dropped or kept as spaces.</summary>
    private static string Normalize(string text, bool keepBreaks)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char raw in text.ToLowerInvariant())
        {
            char c = raw switch
            {
                '0' => 'o', '1' => 'i', '!' => 'i', '|' => 'i', '3' => 'e', '4' => 'a', '@' => 'a',
                '5' => 's', '$' => 's', '7' => 't', '+' => 't', '8' => 'b', '9' => 'g', '€' => 'e',
                _ => raw,
            };
            if (c is >= 'a' and <= 'z') sb.Append(c);
            else if (keepBreaks && (c is ' ' or '_' or '-' or '.')) sb.Append(' ');
        }
        return keepBreaks ? string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)) : sb.ToString();
    }

    private static string CollapseRepeats(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) if (sb.Length == 0 || sb[^1] != c) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>"trikzos@gmail.com" → "t•••@gmail.com" — enough for you to recognise, no use to anyone else.</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return "";
        int at = email.IndexOf('@');
        if (at <= 0) return "•••";
        return email[0] + "•••" + email[at..];
    }
}
