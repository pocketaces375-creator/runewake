using System;
using System.IO;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;
using Runewake.Persistence;
using Xunit;

namespace Runewake.Tests.Data;

/// <summary>
/// FABLE-ACCOUNTS-1: every field of ProgressionState must survive a save and a
/// reload of the phone's own save file. Delver level/XP, the Duel Arena record
/// and seen cards were never written, so they reset on every launch. Compared
/// through the cloud snapshot, which lists every field (AccountsTests proves
/// that by reflection) — so a field added later without a save path fails here.
/// </summary>
public class SaveRepositoryAllFieldsTests
{
    [Fact]
    public void Every_field_survives_save_and_load()
    {
        var s = new ProgressionState
        {
            Shards = 12, DigCharges = 3, RuneDust = 40, GlobalDiscoveryIndex = 2,
            HasCompletedTutorial = true, DelverLevel = 4, DelverXp = 77,
            SavedRunePageJson = "{\"slots\":[]}", ShopRotationDay = 9, ArenaWins = 5, ArenaLosses = 2,
            Tutorial = new TutorialState { CurrentStep = (TutorialStep)1, IsComplete = true },
        };
        s.ClearedNodes.Add("r1_n1"); s.ClearedNodes.Add("r1_n2");
        s.AddCard("vrd_c_root_warden", 2);
        s.Fragments["VERDANT"] = 3;
        s.OwnedRuneIds.Add("rune_a");
        s.UnlockedTools.Add("tool_pick");
        s.DeckCardIds.AddRange(new[] { "vrd_c_root_warden", "vrd_c_root_warden" });
        s.SavedDecks["Main"] = new() { "vrd_c_root_warden" };
        s.SeenCardIds.Add("vrd_c_root_warden"); s.SeenCardIds.Add("emb_c_ember_hound");
        s.AddRelic(new LostRelicInstance { RelicInstanceId = "rel-1", CardId = "relic_x", AcquirerName = "Trik", AcquiredAt = "2026-10-04", Site = "site", DiscoveryIndex = 1, EngravingStyle = "plain" });

        var path = Path.Combine(Path.GetTempPath(), $"rw_allfields_{Guid.NewGuid():N}.db");
        try
        {
            new SaveRepository(path).Save(s);
            var back = new SaveRepository(path).Load();
            var want = ProgressionSnapshot.FromState(s).ToJson();
            var got = ProgressionSnapshot.FromState(back).ToJson();
            Assert.Equal(want, got);
        }
        finally
        {
            foreach (var ext in new[] { "", "-wal", "-shm" }) try { File.Delete(path + ext); } catch { }
        }
    }
}
