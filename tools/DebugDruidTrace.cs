using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Sim;

class DebugDruidTrace
{
    static readonly string ProjectRoot = "/home/fictive/runewake";

    static void Main()
    {
        // Load artifacts
        ArtifactLoader.LoadPack(Path.Combine(ProjectRoot, "content/artifacts/launch_artifacts.json"));
        var variantsDir = Path.Combine(ProjectRoot, "content/artifacts/variants");
        if (Directory.Exists(variantsDir))
            ArtifactLoader.LoadAllVariants(variantsDir);

        // Load Druid deck
        CardRegistry.Clear();
        var druidStarter = CardLoader.LoadPack(Path.Combine(ProjectRoot, "tmp/starter_druid.json"));
        CardRegistry.RegisterRange(druidStarter);

        // Load full stratum definitions
        var cardsDir = Path.Combine(ProjectRoot, "content/cards");
        foreach (var packFile in Directory.GetFiles(cardsDir, "*.json"))
        {
            var stratumPack = CardLoader.LoadPack(packFile);
            CardRegistry.RegisterRange(stratumPack);
        }

        var deckIds = druidStarter.Select(d => d.Id).ToList();

        // Set up Druid vs Druid mirror
        var gameCfg = new GameConfig
        {
            Seed = 42,
            ContentVersion = 1,
            Player0DeckIds = new List<string>(deckIds),
            Player1DeckIds = new List<string>(deckIds),
            Player0ArtifactIds = new[] { "artf_druid_book_of_familiar", "artf_druid_elemental_bond" },
            Player1ArtifactIds = new[] { "artf_druid_book_of_familiar", "artf_druid_elemental_bond" },
            Player0Class = "druid",
            Player1Class = "druid",
        };

        var state = GameState.Initialize(gameCfg);

        Console.WriteLine("=== INITIAL STATE (Turn 1, P0's turn just started) ===");
        DumpState(state);

        var bot = new GreedyBot();
        int maxSteps = 100;
        int step = 0;

        while (step < maxSteps && !state.IsGameOver)
        {
            int player = state.CurrentPlayerIndex;
            int turn = state.TurnNumber;
            var action = bot.ChooseAction(state, player);

            if (action is EndTurnAction)
            {
                int oldPlayer = state.CurrentPlayerIndex;
                state = DuelEngine.Apply(state, action);

                Console.WriteLine($"\n=== AFTER P{oldPlayer} ENDED TURN → Turn={state.TurnNumber}, Current=P{state.CurrentPlayerIndex} ===");
                DumpState(state);
            }
            else if (action is PlayCardAction pca)
            {
                var card = state.Players[player].Hand.FirstOrDefault(c => c.InstanceId == pca.CardInstanceId);
                state = DuelEngine.Apply(state, action);
                Console.WriteLine($"      P{player} played {(card?.CardDefId ?? "?")} → lane {pca.LaneIndex}");
            }
            else if (action is AttackAction aa)
            {
                var attacker = state.Players[player].Lanes[aa.SourceLane].Occupant;
                state = DuelEngine.Apply(state, action);
                Console.WriteLine($"      P{player} attacked from lane {aa.SourceLane} → lane {aa.TargetLane} ({(attacker?.CardDefId ?? "?")})");
            }
            else
            {
                state = DuelEngine.Apply(state, action);
            }

            step++;
            if (state.IsGameOver)
                Console.WriteLine($"\n=== GAME OVER — Winner: P{state.WinnerIndex} ===");
        }
    }

    static void DumpState(GameState state)
    {
        Console.WriteLine($"P0: hand={state.Players[0].Hand.Count}, att={state.Players[0].Attunement}/{state.Players[0].AttunementMax}, vig={state.Players[0].Vigor}/{state.Players[0].MaxVigor}");
        for (int i = 0; i < 5; i++)
        {
            var occ = state.Players[0].Lanes[i].Occupant;
            if (occ is not null)
                Console.WriteLine($"  P0 lane {i}: {occ.CardDefId}({occ.CurrentAttack}/{occ.CurrentVigor} {string.Join(",", occ.EffectiveKeywords)})");
        }
        foreach (var slot in state.Players[0].ArtifactSlots)
            if (slot.Occupant is not null)
                Console.WriteLine($"  P0 art: {slot.Occupant.CardDefId} chg={slot.Charges}/{slot.MaxCharges}");

        Console.WriteLine($"P1: hand={state.Players[1].Hand.Count}, att={state.Players[1].Attunement}/{state.Players[1].AttunementMax}, vig={state.Players[1].Vigor}/{state.Players[1].MaxVigor}");
        for (int i = 0; i < 5; i++)
        {
            var occ = state.Players[1].Lanes[i].Occupant;
            if (occ is not null)
                Console.WriteLine($"  P1 lane {i}: {occ.CardDefId}({occ.CurrentAttack}/{occ.CurrentVigor} {string.Join(",", occ.EffectiveKeywords)})");
        }
        foreach (var slot in state.Players[1].ArtifactSlots)
            if (slot.Occupant is not null)
                Console.WriteLine($"  P1 art: {slot.Occupant.CardDefId} chg={slot.Charges}/{slot.MaxCharges}");
    }
}