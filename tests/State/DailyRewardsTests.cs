using System;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;
using Runewake.Persistence;
using Xunit;

namespace Runewake.Tests.State;

/// <summary>FABLE-059: the Rewards page — daily login calendar, today's match goals, the daily chest.</summary>
public class DailyRewardsTests
{
    private static readonly DateOnly D0 = new(2026, 10, 14);
    private static string? NoCard(Rarity r) => null;
    private static string? Card(Rarity r) => "vrd_c_root_warden";

    [Fact]
    public void A_new_save_can_claim_its_login_and_nothing_else()
    {
        var p = new ProgressionState();
        Assert.Equal(RewardStatus.Ready, DailyRewards.LoginStatus(p, D0));
        Assert.All(DailyRewards.MatchTiers, t => Assert.Equal(RewardStatus.Locked, DailyRewards.TierStatus(p, t, D0)));
        Assert.Equal(RewardStatus.Locked, DailyRewards.ChestStatus(p, D0));
        Assert.Equal(1, DailyRewards.ReadyCount(p, D0));
    }

    [Fact]
    public void Login_pays_once_a_day_and_walks_the_calendar()
    {
        var p = new ProgressionState();
        var r = DailyRewards.ClaimLogin(p, D0, NoCard);
        Assert.NotNull(r);
        Assert.Equal(50, p.Shards);
        Assert.Null(DailyRewards.ClaimLogin(p, D0, NoCard));          // second tap the same day: nothing
        Assert.Equal(50, p.Shards);
        Assert.Equal((1, -1), DailyRewards.LoginCalendarView(p, D0));

        DailyRewards.ClaimLogin(p, D0.AddDays(1), NoCard);
        Assert.Equal(15, p.RuneDust);                                  // day 2
        Assert.Equal(2, p.Daily.LoginDaysClaimed);
    }

    [Fact]
    public void Missing_days_does_not_reset_the_calendar()
    {
        var p = new ProgressionState();
        DailyRewards.ClaimLogin(p, D0, NoCard);
        Assert.Equal((1, 1), DailyRewards.LoginCalendarView(p, D0.AddDays(4)));   // four days away: day 2 waits
        DailyRewards.ClaimLogin(p, D0.AddDays(4), NoCard);
        Assert.Equal(15, p.RuneDust);
    }

    [Fact]
    public void A_finished_week_starts_again_at_day_one()
    {
        var p = new ProgressionState();
        for (int i = 0; i < 7; i++) DailyRewards.ClaimLogin(p, D0.AddDays(i), Card);
        Assert.Equal(2, p.Collection["vrd_c_root_warden"]);           // day 6 + day 7 cards
        Assert.Equal((7, -1), DailyRewards.LoginCalendarView(p, D0.AddDays(6)));
        Assert.Equal((0, 0), DailyRewards.LoginCalendarView(p, D0.AddDays(7)));
        int shards = p.Shards;
        DailyRewards.ClaimLogin(p, D0.AddDays(7), Card);
        Assert.Equal(shards + 50, p.Shards);
        Assert.Equal(1, p.Daily.LoginDaysClaimed);
        Assert.Equal(8, p.Daily.TotalLoginClaims);
    }

    [Fact]
    public void Match_goals_open_as_matches_are_played_and_reset_tomorrow()
    {
        var p = new ProgressionState();
        DailyRewards.RecordMatch(p, true, D0);
        DailyRewards.RecordMatch(p, false, D0);
        DailyRewards.RecordMatch(p, true, D0);
        var t = DailyRewards.MatchTiers.ToDictionary(x => x.Id);
        Assert.Equal(RewardStatus.Ready, DailyRewards.TierStatus(p, t["play1"], D0));
        Assert.Equal(RewardStatus.Ready, DailyRewards.TierStatus(p, t["play3"], D0));
        Assert.Equal(RewardStatus.Locked, DailyRewards.TierStatus(p, t["play5"], D0));
        Assert.Equal(2, DailyRewards.Progress(p, t["win3"], D0));
        Assert.Equal(3, DailyRewards.ReadyCount(p, D0));               // login + play1 + play3

        Assert.Null(DailyRewards.ClaimTier(p, "play5", D0, NoCard));   // not reached
        Assert.NotNull(DailyRewards.ClaimTier(p, "play1", D0, NoCard));
        Assert.Null(DailyRewards.ClaimTier(p, "play1", D0, NoCard));   // once
        Assert.Equal(25, p.Shards);

        var tomorrow = D0.AddDays(1);
        Assert.Equal(0, DailyRewards.Progress(p, t["play1"], tomorrow));
        Assert.Equal(RewardStatus.Locked, DailyRewards.TierStatus(p, t["play1"], tomorrow));
        DailyRewards.RecordMatch(p, false, tomorrow);
        Assert.Equal(RewardStatus.Ready, DailyRewards.TierStatus(p, t["play1"], tomorrow));
    }

    [Fact]
    public void The_chest_opens_only_when_everything_else_is_claimed()
    {
        var p = new ProgressionState();
        for (int i = 0; i < 5; i++) DailyRewards.RecordMatch(p, i < 3, D0);
        DailyRewards.ClaimLogin(p, D0, Card);
        foreach (var t in DailyRewards.MatchTiers.Take(3)) DailyRewards.ClaimTier(p, t.Id, D0, Card);
        Assert.Equal(RewardStatus.Locked, DailyRewards.ChestStatus(p, D0));
        Assert.Null(DailyRewards.ClaimChest(p, D0, Card));
        var win = DailyRewards.ClaimTier(p, "win3", D0, Card);
        Assert.Equal("vrd_c_root_warden", win!.CardId);
        Assert.Equal(RewardStatus.Ready, DailyRewards.ChestStatus(p, D0));
        int runes = p.RuneDust;
        Assert.NotNull(DailyRewards.ClaimChest(p, D0, Card));
        Assert.Equal(runes + DailyRewards.Chest.Runes, p.RuneDust);
        Assert.Equal(0, DailyRewards.ReadyCount(p, D0));
    }

    [Fact]
    public void Turning_the_clock_back_never_reopens_a_day()
    {
        var p = new ProgressionState();
        DailyRewards.ClaimLogin(p, D0.AddDays(1), NoCard);            // clock pushed forward a day
        Assert.Equal(RewardStatus.Claimed, DailyRewards.LoginStatus(p, D0));   // back to the real day: still claimed
        Assert.Null(DailyRewards.ClaimLogin(p, D0, NoCard));
        Assert.NotNull(DailyRewards.ClaimLogin(p, D0.AddDays(2), NoCard));
    }

    [Fact]
    public void A_card_reward_with_no_card_to_give_pays_runes_instead()
    {
        var p = new ProgressionState();
        for (int i = 0; i < 3; i++) DailyRewards.RecordMatch(p, true, D0);
        var r = DailyRewards.ClaimTier(p, "win3", D0, NoCard);
        Assert.Null(r!.CardId);
        Assert.Equal(15 + ProgressionState.GetRuneDustValue(Rarity.UNCOMMON) * 3, p.RuneDust);
        Assert.DoesNotContain("card", r.Describe());
    }

    [Fact]
    public void Daily_rewards_survive_a_relaunch_and_the_cloud()
    {
        var p = new ProgressionState();
        DailyRewards.ClaimLogin(p, D0, NoCard);
        DailyRewards.RecordMatch(p, true, D0);
        DailyRewards.ClaimTier(p, "play1", D0, NoCard);
        var path = Path.Combine(Path.GetTempPath(), $"rw_daily_{Guid.NewGuid():N}.db");
        try
        {
            new SaveRepository(path).Save(p);
            var back = new SaveRepository(path).Load();
            Assert.Equal(RewardStatus.Claimed, DailyRewards.LoginStatus(back, D0));
            Assert.Equal(RewardStatus.Claimed, DailyRewards.TierStatus(back, DailyRewards.MatchTiers[0], D0));
            Assert.Equal(1, back.Daily.MatchesToday);

            var cloud = new ProgressionState();
            ProgressionSnapshot.FromJson(ProgressionSnapshot.FromState(back).ToJson())!.ApplyTo(cloud);
            Assert.Equal(RewardStatus.Claimed, DailyRewards.LoginStatus(cloud, D0));
            Assert.Equal(1, cloud.Daily.LoginDaysClaimed);
        }
        finally { foreach (var ext in new[] { "", "-wal", "-shm" }) try { File.Delete(path + ext); } catch { } }
    }

    [Fact]
    public void An_older_cloud_save_without_daily_rewards_starts_a_fresh_calendar()
    {
        var json = ProgressionSnapshot.FromState(new ProgressionState { Shards = 9 }).ToJson().Replace(",\"daily\":", ",\"x_daily\":");
        var s = new ProgressionState();
        ProgressionSnapshot.FromJson(json)!.ApplyTo(s);
        Assert.Equal(RewardStatus.Ready, DailyRewards.LoginStatus(s, D0));
        Assert.Equal(9, s.Shards);
    }

    [Fact]
    public void Card_rewards_prefer_cards_the_player_does_not_own_and_never_tokens()
    {
        var cards = new[]
        {
            new CardDef { Id = "a_owned", Name = "Owned", Set = "s", Rarity = Rarity.RARE, Type = CardType.CREATURE },
            new CardDef { Id = "b_new", Name = "New", Set = "s", Rarity = Rarity.RARE, Type = CardType.RITUAL },
            new CardDef { Id = "c_token", Name = "Token", Set = "s", Rarity = Rarity.RARE, Type = CardType.TOKEN },
            new CardDef { Id = "d_common", Name = "Common", Set = "s", Rarity = Rarity.COMMON, Type = CardType.CREATURE },
        };
        var p = new ProgressionState();
        p.AddCard("a_owned");
        for (int seed = 0; seed < 10; seed++)
            Assert.Equal("b_new", DailyRewards.PickCard(p, cards, Rarity.RARE, new Random(seed)));
        Assert.Null(DailyRewards.PickCard(p, cards, Rarity.UNCOMMON, new Random(1)));
    }
}
