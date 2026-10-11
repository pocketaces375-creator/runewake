using System;
using System.IO;
using System.Linq;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;
using Runewake.Persistence;
using Xunit;

namespace Runewake.Tests.Supabase;

/// <summary>
/// FABLE-056: Trikzos — "I don't think you have the save feature working". Decks the player saved must
/// survive a relaunch, the cloud round trip, and signing in on the phone.
/// </summary>
public class DeckSaveSyncTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rw_decks_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_path) + "*"))
            try { File.Delete(f); } catch { }
    }

    private static ProgressionState WithDecks()
    {
        var s = new ProgressionState();
        s.AddCard("vrd_c_root_warden", 2);
        s.SavedDecks["Half Built"] = new() { "vrd_c_root_warden", "vrd_c_root_warden" };
        s.DeckArtifacts["Half Built"] = new() { "artf_druid_heartroot_totem", "artf_druid_grove_seedbook" };
        return s;
    }

    private static CloudSaveBundle Bundle(ProgressionState st)
    {
        var b = new CloudSaveBundle();
        b.Slots["0"] = ProgressionSnapshot.FromState(st);
        return b;
    }

    [Fact]
    public void Saved_decks_and_their_artifacts_survive_a_relaunch()
    {
        Assert.True(new SaveRepository(_path).Save(WithDecks()));
        var back = new SaveRepository(_path).Load();          // a fresh repository = the next launch
        Assert.Equal(new[] { "vrd_c_root_warden", "vrd_c_root_warden" }, back.SavedDecks["Half Built"]);
        Assert.Equal(new[] { "artf_druid_heartroot_totem", "artf_druid_grove_seedbook" }, back.DeckArtifacts["Half Built"]);
    }

    [Fact]
    public void Saved_decks_survive_the_cloud_round_trip()
    {
        var json = Bundle(WithDecks()).ToJson();
        var st = new ProgressionState();
        CloudSaveBundle.FromJson(json)!.Slots["0"].ApplyTo(st);
        Assert.Equal(2, st.SavedDecks["Half Built"].Count);
        Assert.Equal(2, st.DeckArtifacts["Half Built"].Count);
    }

    [Fact]
    public void A_phone_with_only_saved_decks_has_played()
    {
        Assert.True(Bundle(WithDecks()).HasPlayed);
        var bare = new ProgressionState(); bare.AddCard("vrd_c_root_warden", 2);
        Assert.False(Bundle(bare).HasPlayed);
    }

    [Fact]
    public void Signing_in_never_silently_replaces_a_phone_that_has_decks()
    {
        var cloud = new ProgressionState(); cloud.ClearedNodes.Add("r1_n1"); cloud.DelverXp = 40;
        var cloudSave = new CloudSaveSync.CloudSave { Bundle = CloudSaveBundle.FromJson(Bundle(cloud).ToJson())!, UpdatedAt = DateTimeOffset.UtcNow };
        // before FABLE-056 this was PullCloud: the decks were overwritten without a question
        Assert.Equal(CloudSaveSync.SignInDecision.Ask, CloudSaveSync.DecideOnSignIn(cloudSave, Bundle(WithDecks())));
        // and an account that never played takes the phone's decks
        var emptyCloud = new CloudSaveSync.CloudSave { Bundle = CloudSaveBundle.FromJson(Bundle(new ProgressionState()).ToJson())!, UpdatedAt = DateTimeOffset.UtcNow };
        Assert.Equal(CloudSaveSync.SignInDecision.PushLocal, CloudSaveSync.DecideOnSignIn(emptyCloud, Bundle(WithDecks())));
    }
}
