using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Duel Arena — FABLE-040 rebuild.
///
/// Pick a deck, pick an opponent, fight the machine. Rune Dust per win (10, or 25 against a
/// Warden) plus one card from the opponent's deck; a win/loss ledger in the save.
///
/// Why it was empty before: BuildTopBar called UpdateRecordLabel() one line BEFORE the label it
/// writes to was created. The NullReferenceException left _Ready half-done — the title bar had
/// been added, nothing after it ever was — and Godot swallowed it. SceneGuard now wraps the
/// build so a throw paints itself on screen instead of a blank page.
///
/// The layout follows the Play Online screen: dark ground, a warm glow, kicker + title, two
/// glass panels (YOUR DECK on the left, OPPONENT on the right), one primary Fight plate and a
/// quiet Back plate. Decks come from three places — the campaign deck, saved Forge decks, and
/// every class starter — so there is always something to fight with.
/// </summary>
public partial class ArenaScene : Control
{
    public const string ScenePath = "res://scenes/arena/ArenaScene.tscn";

    private static readonly Color Gold = ThemeTokens.Gold;
    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private static readonly Color Muted = new(0.62f, 0.57f, 0.47f);
    private static readonly Color EmberText = Color.FromHtml("#E0865A");
    private static readonly Color Rule = new(0.79f, 0.66f, 0.30f, 0.22f);

    private sealed class DeckChoice
    {
        public string Key = "", Name = "", Sub = "", ClassId = "";
        public List<string> Cards = new();
    }

    private sealed class Foe
    {
        public EncounterDef Def = null!;
        public string Kind = "";          // "starter" | "duel" | "elite" | "warden"
        public string ClassId = "";       // for starters
        public bool Warden;
        public string Note = "";          // opening rule / modifier
    }

    private readonly List<DeckChoice> _decks = new();
    private readonly List<Foe> _pool = new();
    private DeckChoice? _deck;
    private int _foeIdx;
    private SeededRng _rng = null!;

    private Control _deckList = null!;
    private Control _foePane = null!;
    private Button _fight = null!;
    private Label _status = null!;
    private float _statusFade;

    public override void _Ready() => SceneGuard.Build(this, "ArenaScene", Build, "res://scenes/main/Main.tscn", "Back to title");

    private void Build()
    {
        CampaignContext.IsArenaDuel = false;
        if (!CampaignContext.SaveManager.IsLoaded) CampaignContext.SaveManager.Initialize();
        if (CampaignContext.EncounterIndex.Count == 0) CampaignContext.LoadEncounters();
        CampaignContext.LoadStarterDecks();
        CampaignContext.LoadDeckLibrary();

        GatherDecks();
        GatherPool();
        _rng = new SeededRng(CampaignContext.ArenaSeed > 0 ? CampaignContext.ArenaSeed : (ulong)(GD.Randi() & 0x7FFFFFFF));
        CampaignContext.ArenaSeed = _rng.NextU64();
        _foeIdx = _pool.Count > 0 ? (int)(_rng.NextU64() % (ulong)_pool.Count) : 0;

        var vp = GetViewportRect().Size;
        float cx = vp.X / 2f;

        // ── ground + ember glow ──
        var bg = new ColorRect { Color = new Color(0.05f, 0.045f, 0.035f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var glow = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = Grad(new Color(0.86f, 0.52f, 0.28f, 0.20f), new Color(0.86f, 0.52f, 0.28f, 0f)),
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.25f), FillTo = new Vector2(0.5f, 1f), Width = 64, Height = 64,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore,
        };
        glow.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(glow);

        // ── header ──
        Text("FIGHT THE MACHINE", 26, 28, ThemeTokens.GetHeaderFont(28), new Color(0.75f, 0.68f, 0.52f));
        Text("Duel Arena", 60, 84, ThemeTokens.GetCardNameFont(84), Gold);

        // record + dust, top-right
        var prog = CampaignContext.Progression;
        var rec = new Label
        {
            Text = $"{prog.ArenaWins} W  ·  {prog.ArenaLosses} L        ✦ {prog.RuneDust} Rune Dust",
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(vp.X - 880, 52), Size = new Vector2(800, 50), MouseFilter = MouseFilterEnum.Ignore,
        };
        rec.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(30)); rec.AddThemeFontSizeOverride("font_size", 30);
        rec.AddThemeColorOverride("font_color", new Color(0.86f, 0.76f, 0.56f));
        AddChild(rec);

        // ── panels ──
        const float panelTop = 180f, panelH = 690f, panelW = 960f, gapX = 60f;
        var left = Glass(new Vector2(cx - gapX - panelW, panelTop), new Vector2(panelW, panelH));
        var right = Glass(new Vector2(cx + gapX, panelTop), new Vector2(panelW, panelH));
        AddChild(left); AddChild(right);
        BuildDeckPane(left, panelW, panelH);
        BuildFoePane(right, panelW, panelH);

        // ── the way in / out ──
        _fight = Plate("Fight", true, 600, 96);
        _fight.Position = new Vector2(cx - 300, panelTop + panelH + 26);
        _fight.Pressed += OnFight;
        AddChild(_fight);

        var back = Plate("◀  Back to title", false, 360, 84);
        back.Position = new Vector2(80, vp.Y - 120);
        back.Pressed += () => { Click(); GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn"); };
        AddChild(back);

        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(cx - 500, vp.Y - 68), Size = new Vector2(1000, 44), MouseFilter = MouseFilterEnum.Ignore,
        };
        _status.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(28)); _status.AddThemeFontSizeOverride("font_size", 28);
        _status.AddThemeColorOverride("font_color", new Color(0.86f, 0.76f, 0.56f));
        AddChild(_status);

        // Preselect: the deck you're playing the campaign with, else the first one.
        _deck = _decks.FirstOrDefault(d => d.Key == "campaign") ?? _decks.FirstOrDefault();
        RefreshDecks();
        RefreshFoe();
    }

    public override void _Process(double delta)
    {
        if (_statusFade <= 0) return;
        _statusFade -= (float)delta;
        _status.Modulate = new Color(1, 1, 1, Mathf.Clamp(_statusFade / 0.6f, 0, 1));
    }

    // ════════════════════════════════════════════════════════════════
    //  Data
    // ════════════════════════════════════════════════════════════════

    private void GatherDecks()
    {
        _decks.Clear();
        string cls = CampaignContext.ChosenClass;
        var campaign = CampaignContext.Progression.DeckCardIds;
        if (campaign.Count >= DeckRules.MinSize)
            _decks.Add(new DeckChoice { Key = "campaign", Name = "Campaign deck", Sub = $"{campaign.Count} cards  ·  {Title(cls)}", ClassId = cls, Cards = new List<string>(campaign) });
        foreach (var d in CampaignContext.DeckLibrary)
        {
            if (d.Cards == null || d.Cards.Count < DeckRules.MinSize) continue;
            if (_decks.Any(x => x.Cards.SequenceEqual(d.Cards))) continue;
            _decks.Add(new DeckChoice { Key = "saved:" + d.DeckId, Name = d.Name, Sub = $"{d.Cards.Count} cards  ·  {Title(d.ClassId)}  ·  from the Forge", ClassId = d.ClassId, Cards = new List<string>(d.Cards) });
        }
        foreach (var (cid, st) in CampaignContext.StarterDeckIndex.OrderBy(k => k.Key))
        {
            if (st.Cards.Count < DeckRules.MinSize) continue;
            if (_decks.Any(x => x.Cards.SequenceEqual(st.Cards))) continue;
            _decks.Add(new DeckChoice { Key = "starter:" + cid, Name = st.DeckName, Sub = $"{st.Cards.Count} cards  ·  {Title(cid)} starter", ClassId = cid, Cards = new List<string>(st.Cards) });
        }
    }

    private void GatherPool()
    {
        _pool.Clear();
        foreach (var (cid, st) in CampaignContext.StarterDeckIndex.OrderBy(k => k.Key))
        {
            if (st.Cards.Count < DeckRules.MinSize) continue;
            _pool.Add(new Foe
            {
                Def = new EncounterDef { Id = $"arena_starter_{cid}", Name = $"{Title(cid)} of the Old Guard", Deck = new List<string>(st.Cards) },
                Kind = "starter", ClassId = cid,
            });
        }
        foreach (var (encId, enc) in CampaignContext.EncounterIndex)
        {
            if (enc.Deck == null || enc.Deck.Count < DeckRules.MinSize || enc.IsTutorial) continue;
            bool warden = encId.Contains("warden") || encId.Contains("boss") || enc.Modifier == "ELITE" || enc.Modifier == "WARDEN" || !string.IsNullOrEmpty(enc.OpeningRule);
            bool elite = encId.Contains("elite") || !string.IsNullOrEmpty(enc.Modifier);
            _pool.Add(new Foe
            {
                Def = new EncounterDef { Id = $"arena_{encId}", Name = enc.Name, Deck = new List<string>(enc.Deck), OpeningRule = enc.OpeningRule, Modifier = enc.Modifier },
                Kind = warden ? "warden" : elite ? "elite" : "duel",
                Warden = warden,
                Note = !string.IsNullOrEmpty(enc.Modifier) ? enc.Modifier! : enc.OpeningRule ?? "",
            });
        }
        GD.Print($"[ArenaScene] Opponent pool: {_pool.Count} opponents, {_decks.Count} decks to bring");
    }

    private static string Title(string classId) => string.IsNullOrEmpty(classId) ? "Any class" : char.ToUpperInvariant(classId[0]) + classId.Substring(1);

    /// <summary>The card that fronts a deck: rarest first, then the dearest.</summary>
    private static string Signature(List<string> deck)
    {
        int Rank(string id)
        {
            var parts = id.Split('_');
            string r = parts.Length > 1 ? parts[1] : "c";
            int rr = r switch { "m" => 4, "r" => 3, "u" => 2, "x" => 1, _ => 0 };
            int cost = 0;
            try { cost = CardRegistry.Get(id)?.Cost ?? 0; } catch { /* registry may be empty here */ }
            return rr * 100 + cost;
        }
        return deck.OrderByDescending(Rank).ThenBy(x => x).FirstOrDefault() ?? "";
    }

    private static string Strata(List<string> deck)
    {
        var counts = deck.GroupBy(id => id.Split('_')[0]).OrderByDescending(g => g.Count()).Select(g => g.Key).ToList();
        string top = counts.FirstOrDefault() ?? "";
        return top switch { "vrd" => "Verdant", "emb" => "Ember", "tid" => "Tide", "hol" => "Hollow", "dwn" => "Dawn", _ => "" };
    }

    // ════════════════════════════════════════════════════════════════
    //  Panels
    // ════════════════════════════════════════════════════════════════

    private void BuildDeckPane(PanelContainer panel, float w, float h)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        panel.AddChild(col);
        SectionHeader(col, "YOUR DECK", $"{_decks.Count} to choose from");

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        col.AddChild(scroll);
        _deckList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        ((VBoxContainer)_deckList).AddThemeConstantOverride("separation", 12);
        scroll.AddChild(_deckList);
        DragScroll.Attach(scroll);
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24), MouseFilter = MouseFilterEnum.Ignore });
    }

    private void RefreshDecks()
    {
        foreach (var c in _deckList.GetChildren()) c.QueueFree();
        if (_decks.Count == 0)
        {
            var l = Body("No deck yet — start a campaign or build one in the Forge.", 30, Muted);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _deckList.AddChild(l);
            return;
        }
        foreach (var d in _decks)
        {
            bool sel = _deck == d;
            var wrap = new MarginContainer();
            wrap.AddThemeConstantOverride("margin_left", 36); wrap.AddThemeConstantOverride("margin_right", 36);
            _deckList.AddChild(wrap);
            var b = new Button { CustomMinimumSize = new Vector2(0, 96), FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
            b.AddThemeStyleboxOverride("normal", sel ? MenuButtons.PrimaryNormal() : RowBox(false));
            b.AddThemeStyleboxOverride("hover", sel ? MenuButtons.PrimaryHover() : RowBox(true));
            b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
            b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            wrap.AddChild(b);
            var inner = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            inner.SetAnchorsPreset(LayoutPreset.FullRect);
            inner.OffsetLeft = 26; inner.OffsetRight = -26;
            inner.AddThemeConstantOverride("separation", 20);
            b.AddChild(inner);
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
            text.AddThemeConstantOverride("separation", 0);
            text.AddChild(Body(d.Name, 34, sel ? Color.FromHtml("#F2DFA6") : Parchment));
            text.AddChild(Body(d.Sub, 24, sel ? new Color(0.85f, 0.78f, 0.60f) : Muted));
            inner.AddChild(text);
            inner.AddChild(Body(sel ? "✓" : "", 36, Gold));
            var captured = d;
            b.Pressed += () => { Click(); _deck = captured; RefreshDecks(); UpdateFight(); };
        }
    }

    private void BuildFoePane(PanelContainer panel, float w, float h)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        panel.AddChild(col);
        SectionHeader(col, "OPPONENT", $"{_pool.Count} in the pool");
        _foePane = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        col.AddChild(_foePane);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, CustomMinimumSize = new Vector2(0, 120) };
        row.AddThemeConstantOverride("separation", 18);
        col.AddChild(row);
        var prev = Plate("◀", false, 110, 80); prev.Pressed += () => Step(-1); row.AddChild(prev);
        var rnd = Plate("Random opponent", false, 420, 80); rnd.Pressed += () => { Click(); if (_pool.Count > 0) _foeIdx = (int)(_rng.NextU64() % (ulong)_pool.Count); RefreshFoe(); }; row.AddChild(rnd);
        var next = Plate("▶", false, 110, 80); next.Pressed += () => Step(1); row.AddChild(next);
    }

    private void Step(int d)
    {
        Click();
        if (_pool.Count == 0) return;
        _foeIdx = ((_foeIdx + d) % _pool.Count + _pool.Count) % _pool.Count;
        RefreshFoe();
    }

    private void RefreshFoe()
    {
        foreach (var c in _foePane.GetChildren()) c.QueueFree();
        if (_pool.Count == 0) { _foePane.AddChild(Body("No opponents loaded.", 30, Muted)); UpdateFight(); return; }
        var foe = _pool[_foeIdx];
        float w = 960f;

        // the face of the deck: its signature card, or the class portrait for a starter
        const float cardW = 280f, cardH = cardW * 608f / 416f;
        var face = new Control { Position = new Vector2(48, 24), Size = new Vector2(cardW, cardH), MouseFilter = MouseFilterEnum.Ignore };
        _foePane.AddChild(face);
        var shadow = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        shadow.SetAnchorsPreset(LayoutPreset.FullRect);
        shadow.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), ShadowColor = new Color(0, 0, 0, 0.6f), ShadowSize = 22, ShadowOffset = new Vector2(0, 10), CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14 });
        face.AddChild(shadow);
        string sig = Signature(foe.Def.Deck);
        string portrait = foe.Kind == "starter" ? CampaignContext.GetClassPortraitPath(foe.ClassId) : "";
        if (!string.IsNullOrEmpty(portrait) && ResourceLoader.Exists(portrait))
        {
            var art = new TextureRect
            {
                Texture = GD.Load<Texture2D>(portrait), StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore,
                Position = Vector2.Zero, Size = new Vector2(cardW, cardH), ClipContents = true,
            };
            face.AddChild(art);
            var rim = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            rim.SetAnchorsPreset(LayoutPreset.FullRect);
            rim.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = new Color(0.79f, 0.66f, 0.30f, 0.7f), BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3, CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10 });
            face.AddChild(rim);
        }
        else if (!string.IsNullOrEmpty(sig))
        {
            var plate = new CardPlate();
            face.AddChild(plate);
            CardDef? def = null;
            try { def = CardRegistry.Get(sig); } catch { }
            plate.Setup(sig, def?.Attack, def?.Vigor, cardW, cardH, def?.Cost ?? 0);
        }

        // words to the right of it
        float tx = 48 + cardW + 40, tw = w - tx - 48;
        float y = 30;
        var kind = Body(foe.Kind switch { "warden" => "WARDEN", "elite" => "ELITE", "starter" => "CLASS DECK", _ => "DUELIST" }, 24, foe.Warden ? EmberText : new Color(0.75f, 0.68f, 0.52f));
        kind.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont(24));
        Place(kind, tx, y, tw, 34); y += 40;
        var name = Body(foe.Def.Name, 44, Parchment);
        name.AddThemeFontOverride("font", ThemeTokens.GetCardNameFont(44));
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Place(name, tx, y, tw, 110); y += 118;

        var chips = new HBoxContainer { Position = new Vector2(tx, y), MouseFilter = MouseFilterEnum.Ignore };
        chips.AddThemeConstantOverride("separation", 12);
        _foePane.AddChild(chips);
        chips.AddChild(Chip($"{foe.Def.Deck.Count} cards"));
        string strata = foe.Kind == "starter" ? foe.ClassId.ToLowerInvariant() switch
        {
            "warrior" => "Ember", "druid" => "Verdant", "battlemage" or "astrologist" => "Tide",
            "necromancer" or "rogue" or "occultist" => "Hollow", "paladin" => "Dawn", _ => Strata(foe.Def.Deck),
        } : Strata(foe.Def.Deck);
        if (!string.IsNullOrEmpty(strata)) chips.AddChild(Chip(strata));
        if (foe.Warden) chips.AddChild(Chip("Warden", true));
        y += 66;

        if (!string.IsNullOrEmpty(foe.Note))
        {
            var note = Body(foe.Note, 26, new Color(0.85f, 0.78f, 0.60f));
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            Place(note, tx, y, tw, 70); y += 78;
        }

        int dust = foe.Warden ? 25 : 10;
        var reward = Body($"Win:  ✦ {dust} Rune Dust  +  one card from their deck", 28, Gold);
        reward.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Place(reward, tx, Mathf.Max(y + 10, cardH - 60), tw, 70);

        var counter = Body($"{_foeIdx + 1} / {_pool.Count}", 24, Muted);
        counter.HorizontalAlignment = HorizontalAlignment.Right;
        Place(counter, w - 48 - 200, 4, 200, 30);

        UpdateFight();
    }

    private void UpdateFight()
    {
        bool ok = _deck != null && _pool.Count > 0;
        _fight.Disabled = !ok;
        _fight.Text = _deck == null ? "Pick a deck" : "Fight";
    }

    private void OnFight()
    {
        Click();
        if (_deck == null) { Say("Pick a deck first."); return; }
        if (_pool.Count == 0) return;
        var foe = _pool[_foeIdx];

        CampaignContext.PlayerDeckIds = new List<string>(_deck.Cards);
        var enc = new EncounterDef { Id = foe.Def.Id, Name = foe.Def.Name, Deck = new List<string>(foe.Def.Deck) };
        CampaignContext.ArenaEncounter = enc;
        CampaignContext.CurrentEncounter = enc;
        CampaignContext.IsArenaDuel = true;
        CampaignContext.ArenaOpponentName = foe.Def.Name;
        CampaignContext.IsWardenOpponent = foe.Warden;
        CampaignContext.CurrentNodeId = "arena_duel";
        GD.Print($"[ArenaScene] Starting arena duel: {_deck.Name} vs {foe.Def.Name} (warden={foe.Warden})");
        GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
    }

    /// <summary>After an arena duel ends (any road out), clear the arena flags.</summary>
    public static void ReturnFromDuel()
    {
        CampaignContext.IsArenaDuel = false;
        CampaignContext.ArenaEncounter = null;
    }

    // ════════════════════════════════════════════════════════════════
    //  Bits
    // ════════════════════════════════════════════════════════════════

    private void Place(Control c, float x, float y, float w, float h)
    {
        c.Position = new Vector2(x, y); c.Size = new Vector2(w, h);
        _foePane.AddChild(c);
    }

    private static PanelContainer Glass(Vector2 pos, Vector2 size)
    {
        var p = new PanelContainer { Position = pos, Size = size, MouseFilter = MouseFilterEnum.Stop };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.082f, 0.072f, 0.061f, 0.84f),
            BorderColor = new Color(0.79f, 0.66f, 0.30f, 0.38f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            ShadowColor = new Color(0, 0, 0, 0.55f), ShadowSize = 28,
        });
        return p;
    }

    private static StyleBoxFlat RowBox(bool hover) => new()
    {
        BgColor = hover ? new Color(0.79f, 0.66f, 0.30f, 0.10f) : new Color(1, 1, 1, 0.03f),
        BorderColor = new Color(0.79f, 0.66f, 0.30f, hover ? 0.5f : 0.22f),
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
    };

    private static void SectionHeader(VBoxContainer col, string text, string right)
    {
        var wrap = new MarginContainer { CustomMinimumSize = new Vector2(0, 92) };
        wrap.AddThemeConstantOverride("margin_left", 48); wrap.AddThemeConstantOverride("margin_right", 48); wrap.AddThemeConstantOverride("margin_top", 30);
        col.AddChild(wrap);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        wrap.AddChild(v);
        var row = new HBoxContainer();
        v.AddChild(row);
        var l = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont(30)); l.AddThemeFontSizeOverride("font_size", 30);
        l.AddThemeColorOverride("font_color", Gold);
        row.AddChild(l);
        row.AddChild(Body(right, 24, Muted));
        v.AddChild(new ColorRect { Color = Rule, CustomMinimumSize = new Vector2(0, 1), MouseFilter = MouseFilterEnum.Ignore });
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14), MouseFilter = MouseFilterEnum.Ignore });
    }

    private static Label Body(string text, int size, Color colour)
    {
        var l = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(size)); l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        return l;
    }

    private static Control Chip(string text, bool ember = false)
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = ember ? new Color(0.55f, 0.22f, 0.10f, 0.35f) : new Color(0.79f, 0.66f, 0.30f, 0.12f),
            BorderColor = ember ? new Color(0.9f, 0.5f, 0.3f, 0.8f) : new Color(0.79f, 0.66f, 0.30f, 0.6f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 20, CornerRadiusTopRight = 20, CornerRadiusBottomLeft = 20, CornerRadiusBottomRight = 20,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        p.AddChild(Body(text, 24, ember ? EmberText : new Color(0.9f, 0.84f, 0.68f)));
        return p;
    }

    private Label Text(string text, float y, int size, Font font, Color colour)
    {
        var vp = GetViewportRect().Size;
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Position = new Vector2(0, y), Size = new Vector2(vp.X, size + 20), MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", font); l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        AddChild(l);
        return l;
    }

    private static Button Plate(string text, bool primary, float w, float h)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(w, h), Size = new Vector2(w, h), FocusMode = FocusModeEnum.None };
        b.AddThemeFontOverride("font", ThemeTokens.GetButtonFont(34)); b.AddThemeFontSizeOverride("font_size", 34);
        b.AddThemeColorOverride("font_color", primary ? Color.FromHtml("#F2DFA6") : Color.FromHtml("#D8CBB0"));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.45f, 0.42f, 0.36f));
        b.AddThemeStyleboxOverride("normal", primary ? MenuButtons.PrimaryNormal() : MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("hover", primary ? MenuButtons.PrimaryHover() : MenuButtons.Hover());
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.AddThemeStyleboxOverride("disabled", MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        MenuButtons.Animate(b);
        return b;
    }

    private static Gradient Grad(Color a, Color b) { var g = new Gradient(); g.SetColor(0, a); g.SetColor(1, b); return g; }
    private void Click() => GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
    private void Say(string t) { _status.Text = t; _status.Modulate = Colors.White; _statusFade = 3f; }
}
