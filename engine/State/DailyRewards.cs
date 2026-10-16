using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// FABLE-059: Trikzos — "I'd like to start making daily rewards a thing … everything in one area/page …
/// a couple different claim buttons … playing a certain number of matches will give you daily bonuses".
///
/// What a player can claim, all on the Rewards page:
///   • Daily login — one claim a day, walking a 7-day calendar (day 7 is the big one). Missing a day does
///     NOT reset the calendar; it simply waits for the next claim. A finished week starts again at day 1.
///   • Today's matches — play 1 / 3 / 5 matches and win 3 matches; each tier is its own claim button.
///   • Daily chest — opens once everything else of the day has been claimed.
/// Days are the phone's local calendar days. A clock turned BACK never re-opens a day already seen.
///
/// The whole state is a small all-settable DTO so it goes into the save file and the cloud snapshot as is.
/// </summary>
public class DailyRewardState
{
    /// <summary>Latest local date this save has seen (yyyy-MM-dd). Guards against a clock turned back.</summary>
    [JsonPropertyName("seen")] public string? LastSeenDate { get; set; }
    /// <summary>The date of the last daily-login claim.</summary>
    [JsonPropertyName("login_date")] public string? LoginClaimedDate { get; set; }
    /// <summary>Days claimed in the current 7-day calendar (0–7).</summary>
    [JsonPropertyName("login_day")] public int LoginDaysClaimed { get; set; }
    /// <summary>Every daily-login claim ever made (for the page's "N days" note).</summary>
    [JsonPropertyName("login_total")] public int TotalLoginClaims { get; set; }
    /// <summary>The date the match counters and ClaimedToday belong to.</summary>
    [JsonPropertyName("day")] public string? MatchDate { get; set; }
    [JsonPropertyName("matches")] public int MatchesToday { get; set; }
    [JsonPropertyName("wins")] public int WinsToday { get; set; }
    /// <summary>Match tiers (and the chest) claimed on MatchDate.</summary>
    [JsonPropertyName("claimed")] public List<string> ClaimedToday { get; set; } = new();

    public DailyRewardState Clone() => new()
    {
        LastSeenDate = LastSeenDate, LoginClaimedDate = LoginClaimedDate, LoginDaysClaimed = LoginDaysClaimed,
        TotalLoginClaims = TotalLoginClaims, MatchDate = MatchDate, MatchesToday = MatchesToday,
        WinsToday = WinsToday, ClaimedToday = new List<string>(ClaimedToday ?? new()),
    };
}

public enum RewardStatus { Locked, Ready, Claimed }

/// <summary>What one claim gives. Any mix of the four.</summary>
public sealed record DailyReward(int Shards = 0, int Runes = 0, int DigCharges = 0, Rarity? Card = null)
{
    public string Describe()
    {
        var parts = new List<string>();
        if (Card is Rarity r) parts.Add(CardPhrase(r));
        if (Shards > 0) parts.Add($"{Shards} Shards");
        if (Runes > 0) parts.Add($"{Runes} Runes");
        if (DigCharges > 0) parts.Add(DigCharges == 1 ? "1 Dig charge" : $"{DigCharges} Dig charges");
        return string.Join(" + ", parts);
    }

    /// <summary>"an Uncommon card", "a Rare card".</summary>
    public static string CardPhrase(Rarity r) => (r == Rarity.UNCOMMON ? "an " : "a ") + RarityName(r) + " card";

    public static string RarityName(Rarity r) => r switch
    {
        Rarity.UNCOMMON => "Uncommon", Rarity.RARE => "Rare", Rarity.RELIC => "Relic", _ => "Common",
    };
}

/// <summary>One match goal on the page: "Play 3 matches", goal 3, counts plays (or wins).</summary>
public sealed record MatchTier(string Id, string Title, int Goal, bool CountsWins, DailyReward Reward);

/// <summary>The result of a claim: what was granted, and the card's id if one was.</summary>
public sealed record ClaimResult(DailyReward Reward, string? CardId, string? CardName)
{
    public string Describe() => CardName != null && Reward.Card is Rarity r
        ? Reward.Describe().Replace(DailyReward.CardPhrase(r), $"{CardName} ({DailyReward.RarityName(r)})")
        : Reward.Describe();
}

public static class DailyRewards
{
    public const string ChestId = "chest";

    /// <summary>The 7-day login calendar. Tune here.</summary>
    public static readonly IReadOnlyList<DailyReward> LoginCalendar = new[]
    {
        new DailyReward(Shards: 50),
        new DailyReward(Runes: 15),
        new DailyReward(DigCharges: 1),
        new DailyReward(Shards: 100),
        new DailyReward(Runes: 30),
        new DailyReward(Card: Rarity.UNCOMMON),
        new DailyReward(Runes: 40, Card: Rarity.RARE),
    };

    /// <summary>Today's match goals, in the order the page shows them. Tune here.</summary>
    public static readonly IReadOnlyList<MatchTier> MatchTiers = new[]
    {
        new MatchTier("play1", "Play 1 match", 1, false, new DailyReward(Shards: 25)),
        new MatchTier("play3", "Play 3 matches", 3, false, new DailyReward(Runes: 20)),
        new MatchTier("play5", "Play 5 matches", 5, false, new DailyReward(Shards: 60, DigCharges: 1)),
        new MatchTier("win3", "Win 3 matches", 3, true, new DailyReward(Runes: 15, Card: Rarity.UNCOMMON)),
    };

    /// <summary>Opens once the login and every match goal of the day are claimed.</summary>
    public static readonly DailyReward Chest = new(Runes: 25, Shards: 40);

    /// <summary>A match counts once it reached this turn — an instant concede is not a match played.</summary>
    public const int MinTurnsForMatch = 3;

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateOnly? P(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>The day the save is on: today, unless the clock went back behind a day already seen.</summary>
    public static DateOnly Effective(ProgressionState p, DateOnly today)
    {
        var seen = P(p.Daily.LastSeenDate);
        return seen is DateOnly s && s > today ? s : today;
    }

    /// <summary>Move the save onto today: a new day clears the match counters and the day's claims.</summary>
    public static DateOnly Touch(ProgressionState p, DateOnly today)
    {
        var day = Effective(p, today);
        var st = p.Daily;
        st.ClaimedToday ??= new();
        st.LastSeenDate = D(day);
        if (st.MatchDate != D(day))
        {
            st.MatchDate = D(day);
            st.MatchesToday = 0;
            st.WinsToday = 0;
            st.ClaimedToday.Clear();
        }
        return day;
    }

    /// <summary>A finished duel (not a tutorial). Too-short matches are ignored by the caller.</summary>
    public static void RecordMatch(ProgressionState p, bool won, DateOnly today)
    {
        Touch(p, today);
        p.Daily.MatchesToday++;
        if (won) p.Daily.WinsToday++;
    }

    // ── what the page shows ──────────────────────────────────────────────

    public static RewardStatus LoginStatus(ProgressionState p, DateOnly today) =>
        p.Daily.LoginClaimedDate == D(Effective(p, today)) ? RewardStatus.Claimed : RewardStatus.Ready;

    /// <summary>
    /// For the 7 calendar tiles: how many are ticked, and which one is the one to claim now (-1 = none,
    /// today's is already claimed). A finished week shows fresh the next day.
    /// </summary>
    public static (int ticked, int readyIndex) LoginCalendarView(ProgressionState p, DateOnly today)
    {
        int n = Math.Clamp(p.Daily.LoginDaysClaimed, 0, LoginCalendar.Count);
        if (LoginStatus(p, today) == RewardStatus.Claimed) return (n, -1);
        if (n >= LoginCalendar.Count) n = 0;
        return (n, n);
    }

    public static int Progress(ProgressionState p, MatchTier t, DateOnly today)
    {
        var day = Effective(p, today);
        if (p.Daily.MatchDate != D(day)) return 0;
        return Math.Min(t.Goal, t.CountsWins ? p.Daily.WinsToday : p.Daily.MatchesToday);
    }

    private static bool ClaimedToday(ProgressionState p, string id, DateOnly today) =>
        p.Daily.MatchDate == D(Effective(p, today)) && (p.Daily.ClaimedToday?.Contains(id) ?? false);

    public static RewardStatus TierStatus(ProgressionState p, MatchTier t, DateOnly today) =>
        ClaimedToday(p, t.Id, today) ? RewardStatus.Claimed
        : Progress(p, t, today) >= t.Goal ? RewardStatus.Ready : RewardStatus.Locked;

    public static RewardStatus ChestStatus(ProgressionState p, DateOnly today)
    {
        if (ClaimedToday(p, ChestId, today)) return RewardStatus.Claimed;
        bool all = LoginStatus(p, today) == RewardStatus.Claimed
                   && MatchTiers.All(t => ClaimedToday(p, t.Id, today));
        return all ? RewardStatus.Ready : RewardStatus.Locked;
    }

    /// <summary>How many claim buttons are lit — the title screen's badge.</summary>
    public static int ReadyCount(ProgressionState p, DateOnly today) =>
        (LoginStatus(p, today) == RewardStatus.Ready ? 1 : 0)
        + MatchTiers.Count(t => TierStatus(p, t, today) == RewardStatus.Ready)
        + (ChestStatus(p, today) == RewardStatus.Ready ? 1 : 0);

    // ── claiming ─────────────────────────────────────────────────────────

    /// <summary>Claim today's login reward. Null when it was already claimed.</summary>
    public static ClaimResult? ClaimLogin(ProgressionState p, DateOnly today, Func<Rarity, string?> pickCard)
    {
        var day = Touch(p, today);
        if (LoginStatus(p, today) == RewardStatus.Claimed) return null;
        var st = p.Daily;
        if (st.LoginDaysClaimed >= LoginCalendar.Count || st.LoginDaysClaimed < 0) st.LoginDaysClaimed = 0;
        var reward = LoginCalendar[st.LoginDaysClaimed];
        var result = Grant(p, reward, pickCard);
        st.LoginDaysClaimed++;
        st.TotalLoginClaims++;
        st.LoginClaimedDate = D(day);
        return result;
    }

    /// <summary>Claim a match tier. Null when it isn't reached yet, or already claimed.</summary>
    public static ClaimResult? ClaimTier(ProgressionState p, string tierId, DateOnly today, Func<Rarity, string?> pickCard)
    {
        Touch(p, today);
        var t = MatchTiers.FirstOrDefault(x => x.Id == tierId);
        if (t == null || TierStatus(p, t, today) != RewardStatus.Ready) return null;
        p.Daily.ClaimedToday.Add(t.Id);
        return Grant(p, t.Reward, pickCard);
    }

    public static ClaimResult? ClaimChest(ProgressionState p, DateOnly today, Func<Rarity, string?> pickCard)
    {
        Touch(p, today);
        if (ChestStatus(p, today) != RewardStatus.Ready) return null;
        p.Daily.ClaimedToday.Add(ChestId);
        return Grant(p, Chest, pickCard);
    }

    private static ClaimResult Grant(ProgressionState p, DailyReward r, Func<Rarity, string?> pickCard)
    {
        p.Shards += r.Shards;
        p.RuneDust += r.Runes;
        p.DigCharges += r.DigCharges;
        string? cardId = null, cardName = null;
        DailyReward given = r;
        if (r.Card is Rarity rarity)
        {
            cardId = pickCard(rarity);
            var def = cardId != null ? CardRegistry.Get(cardId) : null;
            if (cardId != null)
            {
                p.AddCard(cardId);
                cardName = def?.Name ?? cardId;
            }
            else
            {
                // no card of that rarity to give (content not loaded): pay its shop price in Runes instead
                int runes = ProgressionState.GetRuneDustValue(rarity) * 3;
                p.RuneDust += runes;
                given = r with { Card = null, Runes = r.Runes + runes };
            }
        }
        return new ClaimResult(given, cardId, cardName);
    }

    /// <summary>
    /// The card a reward hands out: a collectible card of that rarity, preferring ones the player owns
    /// fewest of (a new card beats a third copy). Tokens are never handed out.
    /// </summary>
    public static string? PickCard(ProgressionState p, Rarity rarity, Random rng) =>
        PickCard(p, CardRegistry.GetAll(), rarity, rng);

    public static string? PickCard(ProgressionState p, IEnumerable<CardDef> cards, Rarity rarity, Random rng)
    {
        var pool = cards
            .Where(c => c.Rarity == rarity && c.Type != CardType.TOKEN && !string.IsNullOrEmpty(c.Set))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
        if (pool.Count == 0) return null;
        int fewest = pool.Min(c => p.Collection.TryGetValue(c.Id, out var n) ? n : 0);
        var best = pool.Where(c => (p.Collection.TryGetValue(c.Id, out var n) ? n : 0) == fewest).ToList();
        return best[rng.Next(best.Count)].Id;
    }
}
