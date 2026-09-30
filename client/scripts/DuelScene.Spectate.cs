using Godot;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// FABLE-COOP-1: watching an ally's board in co-op, drawn by THIS scene — the same lanes, card
/// faces, artifacts, Crescent Dial and hand as your own board — instead of the old stand-in panel
/// of grey boxes. Trikzos: "Can we make this look more like our current board please?"
///
/// How: every render path reads the board through <c>_gsm</c>. While watching, <c>_gsm</c> points at
/// a private read-only GameStateManager holding the ally's state; your own GameStateManager is
/// parked in <c>_homeGsm</c> and keeps receiving your board's updates untouched. Leaving swaps
/// back. Each swap renders as a first frame (no death/summon animations between two different
/// boards); updates to the ally's board animate as they would on theirs.
///
/// Nothing can be played while watching: CoopOverlay puts a blocker over the board, and End
/// Turn / Concede are stood down until you're back.
/// </summary>
public partial class DuelScene
{
    private GameStateManager? _homeGsm;
    private GameStateManager? _watchGsm;
    private string _homeHudName = "";

    public bool IsSpectating => _homeGsm != null;

    /// <summary>Show <paramref name="allyState"/> on this board (read-only). Cheap to call every frame.</summary>
    public void Spectate(GameState allyState, string allyName)
    {
        if (_gsm == null || allyState == null) return;
        if (_homeGsm == null)
        {
            CloseConcedeConfirm();
            _homeGsm = _gsm;
            _homeHudName = _myHudName;
            // Read-only: any move that slips through is refused (the sink returns null), never applied.
            _watchGsm = new GameStateManager { Name = "WatchGsm", DeferGameOver = true, ActionSink = _ => null };
            _watchGsm.SetState(allyState);
            _gsm = _watchGsm;
            _myHudName = Short(allyName);
            RenderAsFirstFrame();
            return;
        }
        _myHudName = Short(allyName);
        if (ReferenceEquals(_watchGsm!.State, allyState)) return;
        _watchGsm.SetState(allyState);
        OnStateChanged();
        StandDownForWatching();
    }

    /// <summary>Back to your own board.</summary>
    public void EndSpectate()
    {
        if (_homeGsm == null) return;
        _gsm = _homeGsm;
        _homeGsm = null;
        _myHudName = _homeHudName;
        _watchGsm?.QueueFree();
        _watchGsm = null;
        RenderAsFirstFrame();
    }

    private void RenderAsFirstFrame()
    {
        _firstRender = true;
        if (_gsm?.State != null) _prevHandSize = _gsm.Me.Hand.Count;   // no draw sound for a board swap
        _prevPlayerChargesFull = ChargesFullMask();                      // nor a charge clink
        OnStateChanged();
        StandDownForWatching();
    }

    /// <summary>End Turn and Concede belong to your own board.</summary>
    private void StandDownForWatching()
    {
        bool watching = IsSpectating;
        if (_endTurnButton != null && !_gameOverSettled) _endTurnButton.Visible = !watching;
        if (_concedeBtn != null && !_conceded) _concedeBtn.Visible = !watching;
    }

    private int ChargesFullMask()
    {
        if (_gsm?.State == null) return 0;
        int mask = 0;
        for (int side = 0; side <= 1; side++)
        for (int ai = 0; ai < (_gsm.Side(side).ArtifactSlots?.Length ?? 0); ai++)
        {
            var slot = _gsm.Side(side).ArtifactSlots[ai];
            if (slot.Occupant != null && slot.MaxCharges > 0 && slot.Charges >= slot.MaxCharges)
                mask |= 1 << (side * 2 + ai);
        }
        return mask;
    }

    private static string Short(string name)
    {
        name = (name ?? "").Trim().ToUpperInvariant();
        return name.Length > 16 ? name.Substring(0, 15) + "…" : name;
    }
}
