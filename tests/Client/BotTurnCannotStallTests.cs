using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Sim;
using Runewake.Tests.World;
using Xunit;

namespace Runewake.Tests.Client;

/// <summary>
/// FABLE-036 guard — the frozen enemy turn. The client refused cards the bot could afford only
/// with a discount (it charged the printed cost; the engine charges the discounted one), and the
/// bot retried the refused card every 0.6 s forever. A bot-vs-bot sweep hit that in 394 of 1,680
/// games. These pin both halves: the client charges what the engine charges, a refused bot
/// action ends the turn instead of repeating, and bot-vs-bot games always finish.
/// </summary>
[Collection("NonParallel")]
public class BotTurnCannotStallTests
{
    private static string Src(string file) => File.ReadAllText(Path.Combine(WorldGeneratorTests.Root(), "client", "scripts", file));

    [Fact]
    public void Client_Charges_The_Discounted_Cost_Like_The_Engine()
    {
        string gsm = Src("GameStateManager.cs");
        int start = gsm.IndexOf("public ActionResult TryPlayCard(", StringComparison.Ordinal);
        int end = gsm.IndexOf("public ActionResult TryAttack(", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "TryPlayCard / TryAttack not found");
        string body = gsm[start..end];
        Assert.Contains("CostInterceptor.GetEffectiveCost", body);
        Assert.DoesNotContain("player.Attunement < card.Cost", body);
    }

    [Fact]
    public void A_Refused_Bot_Action_Ends_The_Turn_Instead_Of_Retrying()
    {
        string bot = Src("BotController.cs");
        Assert.Contains("GiveUpTurn($\"TryPlayCard", bot);
        Assert.Contains("GiveUpTurn($\"TryAttack", bot);
        Assert.Contains("_gsm.TryEndTurn()", bot[bot.IndexOf("private void GiveUpTurn", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void Bot_Vs_Bot_Games_Always_Finish_With_Discounts_In_Play()
    {
        string root = WorldGeneratorTests.Root();
        CardRegistry.Clear();
        CardRegistry.RegisterRange(Directory.GetFiles(Path.Combine(root, "content", "cards"), "*.json").SelectMany(CardLoader.LoadPack));
        var encs = Directory.GetFiles(Path.Combine(root, "client", "content", "encounters"), "*.json")
            .SelectMany(f => EncounterLoader.LoadPack(f).Encounters).Take(8).ToList();
        using var sd = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "client", "content", "decks", "starter_decks.json")));
        var starter = sd.RootElement.GetProperty("starters")[0].GetProperty("cards").EnumerateArray().Select(x => x.GetString()!).ToList();
        var bot = new GreedyBot();
        int games = 0;
        foreach (var enc in encs)
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var s = GameState.Initialize(new GameConfig { Seed = seed, Player0DeckIds = starter, Player1DeckIds = enc.Deck });
            s.Players[0].HasMulliganed = s.Players[1].HasMulliganed = true;
            // Everything costs 1 less for the enemy: the exact situation that froze the phone.
            s.Players[1].CostMods.Add(new CostMod { Amount = 1, Filter = "ALL", SourceController = 1 });
            int steps = 0;
            while (!s.IsGameOver && steps++ < 800)
            {
                var a = bot.ChooseAction(s, s.CurrentPlayerIndex) ?? new EndTurnAction { PlayerIndex = s.CurrentPlayerIndex };
                if (a is PlayCardAction pc)
                {
                    var card = s.CurrentPlayer.Hand.First(c => c.InstanceId == pc.CardInstanceId);
                    Assert.True(CostInterceptor.GetEffectiveCost(s, card, s.CurrentPlayerIndex) <= s.CurrentPlayer.Attunement,
                        $"{enc.Id} seed {seed}: bot chose {card.CardDefId} it cannot afford");
                }
                s = DuelEngine.Apply(s, a);
            }
            Assert.True(s.IsGameOver, $"{enc.Id} seed {seed}: no winner after 800 actions");
            games++;
        }
        Assert.Equal(encs.Count * 4, games);
    }
}
