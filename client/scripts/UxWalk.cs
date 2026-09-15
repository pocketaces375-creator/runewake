using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Runewake.Client;

/// <summary>
/// Scripted UX walkthrough — plays one duel and asserts the screen after each step.
/// Run with: --uxwalk
/// All steps run even if some fail; failures are reported at the end.
/// </summary>
public partial class UxWalk : Node
{
    private DuelScene _d = null!;
    private GameStateManager _gsm = null!;
    private InputController _input = null!;
    private int _step, _pass, _fail;
    private string _selId = "";
    private List<string> _fails = new();
    const string T = "[UXWALK]";

    public override void _Ready()
    {
        _d = GetParent() as DuelScene ?? throw new Exception("UxWalk must be child of DuelScene");
        _gsm = _d.UxGsm;
        _input = _d.UxInput;
        GD.Print($"{T} Starting UX walkthrough");

        var t = new Godot.Timer { OneShot = true, WaitTime = 1f };
        t.Timeout += Step01;
        AddChild(t); t.Start();
    }

    void Step01()
    {
        _step = 1;
        var p = _gsm.GetPlayerHud(0);
        Check("player att>0", p.Attunement > 0);
        Check("player vig>0", p.Vigor > 0);
        Check("player deck>0", p.DeckCount > 0);

        var e = _gsm.GetPlayerHud(1);
        Check("enemy att>=0", e.Attunement >= 0);
        Check("enemy vig>0", e.Vigor > 0);

        Next(Step02);
    }

    void Step02()
    {
        _step = 2;
        var hand = _gsm.GetHand(0);
        int att = _gsm.GetPlayerHud(0).Attunement;
        var card = hand.FirstOrDefault(h => h.Cost <= att);
        if (string.IsNullOrEmpty(card.CardDefId)) { Fail("no affordable card"); Next(Step07); return; }

        var hc = _d.UxHandCards.FirstOrDefault(c => c.CardId == card.CardDefId);
        if (hc == null) { Fail("hand card node not found"); Next(Step07); return; }

        _d.UxSelectCard(hc);
        _selId = card.CardDefId;

        Check("sel->SelectingLane", _input.State == InputController.InputState.SelectingLane);
        Check("card in hand (y<0)", hc.Position.Y < 0f);
        Check("lane slots exist", _d.UxPlayerSlots.Count > 0);

        Next(Step03);
    }

    void Step03()
    {
        _step = 3;
        int bc = _gsm.GetHand(0).Count;
        int ba = _gsm.GetPlayerHud(0).Attunement;

        _d.UxTapLane(0, true);

        var t = new Godot.Timer { OneShot = true, WaitTime = 0.5f };
        t.Timeout += () =>
        {
            var l0 = _gsm.GetLanes(0)[0];
            Check($"lane0=={_selId}", l0.CardDefId == _selId);
            Check("hand-1", _gsm.GetHand(0).Count == bc - 1);
            Check("att decreased", _gsm.GetPlayerHud(0).Attunement < ba);
            Next(Step04);
        };
        AddChild(t); t.Start();
    }

    void Step04()
    {
        _step = 4;
        int att = _gsm.GetPlayerHud(0).Attunement;
        var hand = _gsm.GetHand(0);
        var unaff = hand.FirstOrDefault(h => h.Cost > att);
        if (string.IsNullOrEmpty(unaff.CardDefId)) { Check("no unaffordable", true); Next(Step05); return; }

        var hc = _d.UxHandCards.FirstOrDefault(c => c.CardId == unaff.CardDefId);
        if (hc == null) { Fail("unaff card node null"); Next(Step05); return; }

        var before = _input.State;
        _d.UxSelectCard(hc);
        Check("state unchanged", _input.State == before);
        Next(Step05);
    }

    void Step05()
    {
        _step = 5;
        var hand = _gsm.GetHand(0);
        if (hand.Count == 0) { Fail("no cards"); Next(Step06); return; }
        var card = hand[0];
        var hc = _d.UxHandCards.FirstOrDefault(c => c.CardId == card.CardDefId);
        if (hc == null) { Fail("card node null"); Next(Step06); return; }

        hc.LongPressStarted?.Invoke(null);

        var t = new Godot.Timer { OneShot = true, WaitTime = 0.3f };
        t.Timeout += () =>
        {
            var slab = _d.FindChild("RulesSlab", true, false) as Control;
            Check("slab visible", slab != null && slab.Visible);
            if (slab != null) Check("plaque w<=420", slab.Size.X <= 420f);
            Next(Step06);
        };
        AddChild(t); t.Start();
    }

    void Step06()
    {
        _step = 6;
        var hc = _d.UxHandCards.FirstOrDefault(c => c.CardId == _selId);
        if (hc != null) hc.LongPressEnded?.Invoke();

        var t = new Godot.Timer { OneShot = true, WaitTime = 0.3f };
        t.Timeout += () =>
        {
            var slab = _d.FindChild("RulesSlab", true, false) as Control;
            Check("slab hidden", slab == null || !slab.Visible);
            Next(Step07);
        };
        AddChild(t); t.Start();
    }

    void Step07()
    {
        _step = 7;
        _d.UxEndTurn();

        var t = new Godot.Timer { OneShot = true, WaitTime = 6f };
        t.Timeout += () =>
        {
            Check("player turn back", _gsm.CurrentPlayerIndex == 0);
            Check("bot done", !_d.UxBot.IsThinking);
            Next(Step08);
        };
        AddChild(t); t.Start();
    }

    void Step08()
    {
        _step = 8;
        int att = _gsm.GetPlayerHud(0).Attunement;
        var hand = _gsm.GetHand(0);
        var card = hand.FirstOrDefault(h => h.Cost <= att);
        if (string.IsNullOrEmpty(card.CardDefId)) { Fail("no affordable t2"); Next(Step10); return; }

        var hc = _d.UxHandCards.FirstOrDefault(c => c.CardId == card.CardDefId);
        if (hc == null) { Fail("card node null"); Next(Step10); return; }

        _d.UxSelectCard(hc);
        _selId = card.CardDefId;

        var t = new Godot.Timer { OneShot = true, WaitTime = 0.3f };
        t.Timeout += () =>
        {
            _d.UxTapLane(4, true);
            var t2 = new Godot.Timer { OneShot = true, WaitTime = 0.5f };
            t2.Timeout += () =>
            {
                var l4 = _gsm.GetLanes(0)[4];
                Check($"lane4=={_selId}", l4.CardDefId == _selId);
                Next(Step10);
            };
            AddChild(t2); t2.Start();
        };
        AddChild(t); t.Start();
    }

    void Step10()
    {
        _step = 10;
        for (int s = 0; s <= 1; s++)
        {
            var lanes = _gsm.GetLanes(s);
            var slots = s == 0 ? _d.UxPlayerSlots : _d.UxEnemySlots;
            for (int i = 0; i < 5; i++)
            {
                if (lanes[i].IsEmpty) continue;
                var cp = slots[i].FindChild("CardPlate", true, false);
                bool ok = cp != null;
                GD.Print($"{(ok ? "" : "[UXWALK] FAIL ")}[BOARD] side={s} lane={i} card={lanes[i].CardDefId} tex={(ok ? "ok" : "null")}");
                if (!ok) Fail($"board art missing s={s} i={i}");
            }
        }
        Next(Step12);
    }

    void Step12()
    {
        _step = 12;
        GD.Print($"\n{T} === WALKTHROUGH DONE === {_pass} pass, {_fail} fail");
        if (_fail > 0)
            foreach (var f in _fails) GD.PrintErr(f);

        var qt = new Godot.Timer { OneShot = true, WaitTime = 0.1f };
        qt.Timeout += () => GetTree().Quit(_fail > 0 ? 1 : 0);
        AddChild(qt); qt.Start();
    }

    void Check(string label, bool ok)
    {
        if (ok) { _pass++; GD.Print($"{T} PASS s{_step:D2} {label}"); }
        else { _fail++; _fails.Add($"{T} FAIL s{_step:D2} {label}"); GD.PrintErr($"{T} FAIL s{_step:D2} {label}"); }
    }

    void Fail(string r) { _fail++; _fails.Add($"{T} FAIL s{_step:D2} {r}"); GD.PrintErr($"{T} FAIL s{_step:D2} {r}"); }

    void Next(Action a)
    {
        var t = new Godot.Timer { OneShot = true, WaitTime = 0.3f };
        t.Timeout += a;
        AddChild(t); t.Start();
    }
}