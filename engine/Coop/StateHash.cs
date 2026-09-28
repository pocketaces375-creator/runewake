using System.Collections.Generic;
using Runewake.Engine.State;
using Runewake.Engine.World;

namespace Runewake.Engine.Coop;

/// <summary>
/// FABLE-020: a fingerprint of a duel state for lockstep desync checks. Covers
/// everything a move can change that the player can see or that steers the
/// future (the RNG position). Two phones that applied the same moves to the
/// same start produce the same number; any divergence shows up within a round.
/// </summary>
public static class StateHash
{
    public static ulong Of(GameState s)
    {
        var parts = new List<object>
        {
            s.TurnNumber, s.CurrentPlayerIndex, s.IsGameOver, s.WinnerIndex ?? -1,
            s.Rng.Clone().NextU64(),
        };
        foreach (var p in s.Players)
        {
            parts.Add(p.Vigor); parts.Add(p.MaxVigor); parts.Add(p.Attunement); parts.Add(p.AttunementMax);
            parts.Add(p.Deck.Count); parts.Add(p.Discard.Count); parts.Add(p.Barrow.Count);
            foreach (var c in p.Hand) { parts.Add(c.InstanceId); parts.Add(c.CardDefId); }
            // FABLE-042: what is still to come — two sides with different card data or different
            // decks disagree here from the very first hash, not three turns later.
            foreach (var c in p.Deck) parts.Add(c.CardDefId);
            parts.Add('|');
            foreach (var lane in p.Lanes)
            {
                var o = lane.Occupant;
                if (o == null) { parts.Add('_'); continue; }
                parts.Add(o.InstanceId); parts.Add(o.CardDefId); parts.Add(o.BaseAttack + o.AttackModifier);
                parts.Add(o.BaseVigor + o.VigorModifier - o.Damage); parts.Add(o.IsExhausted);
            }
            foreach (var slot in p.ArtifactSlots) { parts.Add(slot.Charges); parts.Add(slot.IsSuppressed); parts.Add(slot.Occupant?.InstanceId ?? -1); parts.Add(slot.Occupant?.CardDefId ?? ""); }
        }
        return StableHash.Of(parts.ToArray());
    }
}
