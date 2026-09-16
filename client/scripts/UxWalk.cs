using Godot;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Runewake.Client;

public partial class UxWalk : Node
{
    private DuelScene _d = null!;
    private GameStateManager _gsm = null!;
    private InputController _input = null!;
    private int _step, _pass, _fail;
    private string _selId = "";
    private List<string> _fails = new();
    private string _captureDir;
    const string T = "[UXWALK]";

    public override void _Ready()
    {
        _d = GetParent() as DuelScene ?? throw new Exception("UxWalk must be child of DuelScene");
        _gsm = _d.UxGsm;
        _input = _d.UxInput;
        _captureDir = ProjectSettings.GlobalizePath("res://../artifacts/captures");
        System.IO.Directory.CreateDirectory(_captureDir);
        GD.Print($"{T} Starting UX walkthrough, captures → {_captureDir}");

        var t = new Godot.Timer { OneShot = true, WaitTime = 1f };
        t.Timeout += Step01;
        AddChild(t); t.Start();
    }

    void Step01() { _step = 1; GD.Print($"{T} BEGIN s01");
        var p = _gsm.GetPlayerHud(0);
        Check("player att>0", p.Attunement > 0);
        Check("player vig>0", p.Vigor > 0);
        Check("player deck>0", p.DeckCount > 0);
        var e = _gsm.GetPlayerHud(1);
        Check("enemy att>=0", e.Attunement >= 0);
        Check("enemy vig>0", e.Vigor > 0);
        Snap();
        Next(Step02);
    }

    void Step02() { _step = 2; GD.Print($"{T} BEGIN s02");
        var hand = _gsm.GetHand(0);
        int att = _gsm.GetPlayerHud(0).Attunement;
        var card = hand.FirstOrDefault(h => h.Cost <= att);
        if (string.IsNullOrEmpty(card.CardDefId))
        {
            GD.Print($"{T} B1: no affordable card at att={att} — checking banner");
            // Check banner is visible
            var banner = _d.FindChild("NoPlayableBg", true, false) as ColorRect;
            Check("banner visible when no cards", banner != null && banner.Visible);
            Snap(); SnapSkipped(3); SnapSkipped(4); SnapSkipped(5); SnapSkipped(6);
            // End turn and retry on turn 2 with attunement 2
            _d.UxEndTurn();
            var t = new Godot.Timer { OneShot = true, WaitTime = 6f };
            t.Timeout += () =>
            {
                GD.Print($"{T} B1: turn 2 retry");
                var hand2 = _gsm.GetHand(0);
                int att2 = _gsm.GetPlayerHud(0).Attunement;
                var card2 = hand2.FirstOrDefault(h => h.Cost <= att2);
                if (string.IsNullOrEmpty(card2.CardDefId))
                    Fail($"still no affordable card on turn 2 att={att2}");
                else
                {
                    Check("affordable on turn 2", true);
                    // Check banner cleared
                    var banner2 = _d.FindChild("NoPlayableBg", true, false) as ColorRect;
                    Check("banner cleared when affordable", banner2 == null || !banner2.Visible);
                    // Select the card
                    var hc = _d.UxHandCards.FirstOrDefault(c => c.CardId == card2.CardDefId);
                    if (hc == null) { Fail("hand card node not found t2"); Snap(); Next(Step10); return; }
                    _d.UxSelectCard(hc);
                    _selId = card2.CardDefId;
                    Snap();
                    // Skip to placing on turn 2
                    var t2 = new Godot.Timer { OneShot = true, WaitTime = 0.3f };
                    t2.Timeout += () =>
                    {
                        _d.UxTapLane(4, true);
                        var t3 = new Godot.Timer { OneShot = true, WaitTime = 0.5f };
                        t3.Timeout += () =>
                        {
                            var l4 = _gsm.GetLanes(0)[4];
                            Check($"lane4=={_selId}", l4.CardDefId == _selId);
                            Snap();
                            Next(Step09);
                        };
                        AddChild(t3); t3.Start();
                    };
                    AddChild(t2); t2.Start();
                }
            };
            AddChild(t); t.Start();
            return;
        }
        var hc = _d.UxHandCards.FirstOrDefault(c => c.CardId == card.CardDefId);
        if (hc == null) { Fail("hand card node not found"); Snap(); Next(Step07); return; }
        _d.UxSelectCard(hc);
        _selId = card.CardDefId;
        Check("sel->SelectingLane", _input.State == InputController.InputState.SelectingLane);
        Check("card in hand (y<0)", hc.Position.Y < 0f);
        Check("lane slots exist", _d.UxPlayerSlots.Count > 0);
        Snap();
        Next(Step03);
    }

    void Step03() { _step = 3; GD.Print($"{T} BEGIN s03");
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
            Snap();
            Next(Step04);
        };
        AddChild(t); t.Start();
    }

    void Step04() { _step = 4; GD.Print($"{T} BEGIN s04");
        int att = _gsm.GetPlayerHud(0).Attunement;
        var hand = _gsm.GetHand(0);
        var unaff = hand.FirstOrDefault(h => h.Cost > att);
        if (string.IsNullOrEmpty(unaff.CardDefId)) { Check("no unaffordable", true); Snap(); Next(Step05); return; }
        var hc = _d.UxHandCards.FirstOrDefault(c => c.CardId == unaff.CardDefId);
        if (hc == null) { Fail("unaff card node null"); Snap(); Next(Step05); return; }
        var before = _input.State;
        _d.UxSelectCard(hc);
        Check("state unchanged", _input.State == before);
        Snap();
        Next(Step05);
    }

    void Step05() { _step = 5; GD.Print($"{T} BEGIN s05");
        var hand = _gsm.GetHand(0);
        if (hand.Count == 0) { Fail("no cards"); Snap(); Next(Step06); return; }
        var card = hand[0];
        var hc = _d.UxHandCards.FirstOrDefault(c => c.CardId == card.CardDefId);
        if (hc == null) { Fail("card node null"); Snap(); Next(Step06); return; }
        hc.LongPressStarted?.Invoke(null);
        var t = new Godot.Timer { OneShot = true, WaitTime = 0.3f };
        t.Timeout += () =>
        {
            var slab = _d.FindChild("RulesSlab", true, false) as Control;
            Check("slab visible", slab != null && slab.Visible);
            if (slab != null) Check("plaque w<=420", slab.Size.X <= 420f);
            Snap();
            Next(Step06);
        };
        AddChild(t); t.Start();
    }

    void Step06() { _step = 6; GD.Print($"{T} BEGIN s06");
        var hc = _d.UxHandCards.FirstOrDefault(c => c.CardId == _selId);
        if (hc != null) hc.LongPressEnded?.Invoke();
        var t = new Godot.Timer { OneShot = true, WaitTime = 0.3f };
        t.Timeout += () =>
        {
            var slab = _d.FindChild("RulesSlab", true, false) as Control;
            Check("slab hidden", slab == null || !slab.Visible);
            Snap();
            Next(Step07);
        };
        AddChild(t); t.Start();
    }

    void Step07() { _step = 7; GD.Print($"{T} BEGIN s07");
        _d.UxEndTurn();
        var t = new Godot.Timer { OneShot = true, WaitTime = 6f };
        t.Timeout += () =>
        {
            Check("player turn back", _gsm.CurrentPlayerIndex == 0);
            Check("bot done", !_d.UxBot.IsThinking);
            Snap();
            Next(Step08);
        };
        AddChild(t); t.Start();
    }

    void Step08() { _step = 8; GD.Print($"{T} BEGIN s08");
        int att = _gsm.GetPlayerHud(0).Attunement;
        var hand = _gsm.GetHand(0);
        var card = hand.FirstOrDefault(h => h.Cost <= att);
        if (string.IsNullOrEmpty(card.CardDefId)) { Fail("no affordable t2"); Snap(); Next(Step10); return; }
        var hc = _d.UxHandCards.FirstOrDefault(c => c.CardId == card.CardDefId);
        if (hc == null) { Fail("card node null"); Snap(); Next(Step10); return; }
        _d.UxSelectCard(hc);
        _selId = card.CardDefId;
        Snap();
        var t = new Godot.Timer { OneShot = true, WaitTime = 0.3f };
        t.Timeout += () =>
        {
            _d.UxTapLane(4, true);
            var t2 = new Godot.Timer { OneShot = true, WaitTime = 0.5f };
            t2.Timeout += () =>
            {
                var l4 = _gsm.GetLanes(0)[4];
                Check($"lane4=={_selId}", l4.CardDefId == _selId);
                Snap();
                Next(Step09);
            };
            AddChild(t2); t2.Start();
        };
        AddChild(t); t.Start();
    }

    void Step09() { _step = 9; GD.Print($"{T} BEGIN s09");
        GD.Print($"{T} s09: board state check");
        int p0Board = _gsm.GetLanes(0).Count(l => !l.IsEmpty);
        Check("player has creatures on board", p0Board > 0);
        Check("player vigor positive", _gsm.GetPlayerHud(0).Vigor > 0);
        Check("enemy vigor positive", _gsm.GetPlayerHud(1).Vigor > 0);
        Snap();
        Next(Step10);
    }

    void Step10() { _step = 10; GD.Print($"{T} BEGIN s10");
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
        Snap();
        Next(Step11);
    }

    void Step11() { _step = 11; GD.Print($"{T} BEGIN s11");
        var vp = GetViewport().GetVisibleRect().Size;
        float vw = vp.X, vh = vp.Y;
        WalkLabels(_d, vw, vh);
        Snap();
        Next(Step12);
    }

    void WalkLabels(Node node, float vw, float vh, int depth = 0)
    {
        if (depth > 50) return;
        if (node is Label label && label.Visible)
        {
            string path = node.GetPath();
            var textSize = label.GetCombinedMinimumSize();
            float rectW = label.Size.X;
            bool overflow = textSize.X > rectW + 1f && rectW > 0
                && label.AutowrapMode != TextServer.AutowrapMode.Word
                && label.AutowrapMode != TextServer.AutowrapMode.Arbitrary;
            if (overflow)
            {
                float delta = textSize.X - rectW;
                Fail($"text overflow at {path} by {delta:F0}px (textW={textSize.X:F0} rectW={rectW:F0})");
            }
            var global = label.GlobalPosition;
            if (global.X + label.Size.X > vw + 2f || global.Y + label.Size.Y > vh + 2f)
                Fail($"label outside viewport at {path} (pos={global} size={label.Size})");
        }
        foreach (var child in node.GetChildren())
        {
            if (child is Node n)
                WalkLabels(n, vw, vh, depth + 1);
        }
    }

    void Step12() { _step = 12; GD.Print($"{T} BEGIN s12");
        GD.Print($"[UXWALK] COMPLETE steps=12 pass={_pass} fail={_fail}");
        GD.Print($"\n{T} === WALKTHROUGH DONE === {_pass} pass, {_fail} fail");
        if (_fail > 0)
            foreach (var f in _fails) GD.PrintErr(f);
        Snap();
        var qt = new Godot.Timer { OneShot = true, WaitTime = 0.1f };
        qt.Timeout += () => {
            if (_fail > 0) GetTree().Quit(1);
            else { GetTree().Quit(0); GD.Print("[UXWALK] Process exit 0"); }
        };
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

    void SnapSkipped(int s)
    {
        var prev = _step; _step = s; Snap(); _step = prev;
    }

    void Snap()
    {
        var img = GetViewport().GetTexture().GetImage();
        string pngPath = System.IO.Path.Combine(_captureDir, $"uxwalk_s{_step:D2}.png");
        Error err = img.SavePng(pngPath);
        if (err == Error.Ok)
            GD.Print($"{T} Captured s{_step:D2} → {pngPath}");
        else
            GD.PrintErr($"{T} FAIL: SavePng returned {err} for s{_step:D2}");

        var layout = new Godot.Collections.Dictionary();
        try { WriteLayout(layout); }
        catch (System.Exception ex) { GD.PrintErr($"{T} layout.json error: {ex.Message}"); }
        string json = Json.Stringify(layout);
        string jsonPath = System.IO.Path.Combine(_captureDir, $"uxwalk_s{_step:D2}.layout.json");
        System.IO.File.WriteAllText(jsonPath, json);
    }

    void WriteLayout(Godot.Collections.Dictionary layout)
    {
        var tile = GetViewport().GetVisibleRect().Size;
        layout["viewport_w"] = (double)tile.X;
        layout["viewport_h"] = (double)tile.Y;

        AddLayout(layout, "rules_slab", _d.FindChild("RulesSlab", true, false) as Control);
        AddLayout(layout, "turn_label", _d.FindChild("TurnLabel", true, false) as Control);
        AddLayout(layout, "end_turn_btn", _d.FindChild("EndTurnBtn", true, false) as Control);

        for (int i = 0; i < _d.UxPlayerSlots.Count; i++)
            AddLayout(layout, $"player_lane_{i}", _d.UxPlayerSlots[i] as Control);
        for (int i = 0; i < _d.UxEnemySlots.Count; i++)
            AddLayout(layout, $"enemy_lane_{i}", _d.UxEnemySlots[i] as Control);
        for (int i = 0; i < _d.UxHandCards.Count; i++)
            AddLayout(layout, $"hand_card_{i}", _d.UxHandCards[i] as Control);

        var relicEnemy = _d.FindChild("EnemyRelicContainer", true, false) as Control;
        var relicPlayer = _d.FindChild("PlayerRelicContainer", true, false) as Control;
        if (relicEnemy != null)
            foreach (var c in relicEnemy.GetChildren()) AddLayout(layout, "relic", c as Control);
        if (relicPlayer != null)
            foreach (var c in relicPlayer.GetChildren()) AddLayout(layout, "relic", c as Control);
    }

    void AddLayout(Godot.Collections.Dictionary layout, string key, Control? c)
    {
        if (c == null) return;
        if (layout.ContainsKey(key)) key = key + "_dup";
        layout[key + "_x"] = (double)c.Position.X;
        layout[key + "_y"] = (double)c.Position.Y;
        layout[key + "_w"] = (double)c.Size.X;
        layout[key + "_h"] = (double)c.Size.Y;
    }
}