using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-ITEMS-0: Artifact variant files. Tests that variant artifacts loaded
/// from content/artifacts/variants/*.json are registered, can be looked up,
/// and can be equipped in a headless duel via GameConfig.Player0ArtifactIds.
/// </summary>
[Collection("NonParallel")]
public class ArtifactVariantTests
{
    private static readonly string ContentRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content"));

    private static readonly string ArtifactsDir = Path.Combine(ContentRoot, "artifacts");

    private static readonly string VariantsDir = Path.Combine(ArtifactsDir, "variants");

    [Fact]
    public void FixtureVariant_LoadsAndRegisters()
    {
        try
        {
            // Arrange
            ArtifactRegistry.Clear();
            CardRegistry.Clear();

            // Load launch artifacts
            var launchPath = Path.Combine(ArtifactsDir, "launch_artifacts.json");
            Assert.True(File.Exists(launchPath), $"launch_artifacts.json not found at {launchPath}");
            int launchCount = ArtifactLoader.LoadPack(launchPath);
            Assert.True(launchCount > 0, "Should load launch artifacts");

            // Load variant files
            Assert.True(Directory.Exists(VariantsDir), $"Variants directory not found at {VariantsDir}");
            int variantCount = ArtifactLoader.LoadAllVariants(VariantsDir);
            Assert.True(variantCount > 0, "Should load at least one variant file");

            // Act: verify fixture artifact is registered
            var fixture = ArtifactRegistry.Get("artf_warrior_fixture_blade");
            Assert.NotNull(fixture);
            Assert.Equal("warrior", fixture.Class);
            Assert.Equal("sword", fixture.SlotPool);
            Assert.Equal("Fixture Blade", fixture.Name);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void FixtureVariant_EquipsInHeadlessDuel()
    {
        try
        {
            // Arrange
            ArtifactRegistry.Clear();
            CardRegistry.Clear();

            // Load all cards for deck building
            var cardsDir = Path.Combine(ContentRoot, "cards");
            foreach (var file in Directory.GetFiles(cardsDir, "*.json"))
            {
                var cards = CardLoader.LoadPack(file);
                CardRegistry.RegisterRange(cards);
            }

            // Load launch artifacts + variants
            ArtifactLoader.LoadPack(Path.Combine(ArtifactsDir, "launch_artifacts.json"));
            ArtifactLoader.LoadAllVariants(VariantsDir);

            // Verify fixture is registered
            var fixtureDef = ArtifactRegistry.Get("artf_warrior_fixture_blade");
            Assert.NotNull(fixtureDef);

            // Build a simple deck from available cards
            var allCardIds = CardRegistry.GetAll().Select(c => c.Id).Take(30).ToList();
            if (allCardIds.Count < 30)
            {
                // Pad if fewer than 30 unique cards
                var pad = CardRegistry.GetAll().Select(c => c.Id).FirstOrDefault() ?? "vrd_c_root_warden";
                while (allCardIds.Count < 30)
                    allCardIds.Add(pad);
            }

            var config = new GameConfig
            {
                Seed = 42,
                ContentVersion = 1,
                Player0DeckIds = allCardIds,
                Player1DeckIds = allCardIds,
                Player0Class = "warrior",
                Player0ArtifactIds = new[] { "artf_warrior_fixture_blade" },
                Player1Class = "warrior",
                Player1ArtifactIds = new[] { "artf_warrior_sword" },
            };

            // Act
            var state = GameState.Initialize(config);

            // Assert: fixture artifact is equipped
            Assert.NotNull(state.Players[0].ArtifactSlots);
            Assert.Equal(1, state.Players[0].ArtifactSlots.Length);

            var fixtureSlot = state.Players[0].ArtifactSlots[0];
            Assert.NotNull(fixtureSlot.Occupant);
            Assert.Equal("artf_warrior_fixture_blade", fixtureSlot.Occupant.CardDefId);

            // Run a few engine actions to prove the duel functions with variant artifacts
            // End P0's turn (no cards played — just test the cycle)
            state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });
            Assert.False(state.IsGameOver, "Game should not be over after one turn cycle");
        }
        finally
        {
            ArtifactRegistry.Clear();
            CardRegistry.Clear();
        }
    }
}