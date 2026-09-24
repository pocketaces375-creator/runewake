using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.World;

namespace Runewake.Client;

/// <summary>
/// FABLE-020: one page of the shared endless world.
///
/// What the player sees follows Trikzos's rules exactly (engine WorldProgress):
///   beaten places         gold dot with a tick
///   places you've been    full-colour dot
///   one step past what you've beaten, and SOMEONE has found it
///                         a dimmed dot
///   one step past, and NOBODY has ever found it
///                         no dot at all — the road runs out into nothing;
///                         tap the road's end to be the first
///   anything further      not shown
/// </summary>
public partial class WorldMapScene : Control
{
    private WorldPage _page = null!;
    private WorldProgress _progress = null!;
    private Dictionary<int, BlipVisibility> _vis = new();
    private Control _canvas = null!;
    private RoadLayer _roads = null!;
    private Control _panel = null!;
    private VBoxContainer _panelBody = null!;
    private Label _title = null!, _subtitle = null!, _count = null!;
    private HBoxContainer _pageNav = null!;
    private int? _selected;
    private Rect2 _mapRect;
    private bool _busy;

    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private static readonly Color Dim = new(0.62f, 0.58f, 0.50f);

    public override void _Ready() => SceneGuard.Build(this, "WorldMapScene", ReadyBody, WorldService.CrossroadsScenePath, "Back to the Crossroads");

    private void ReadyBody()
    {
        var vp = GetViewportRect().Size;
        _mapRect = new Rect2(40, 130, vp.X - 40 - 480, vp.Y - 130 - 120);
        _progress = WorldService.Progress;
        var addr = WorldService.CurrentPage;
        if (!_progress.IsPageOpen(addr)) addr = FirstOpenPage() ?? addr;
        LoadPage(addr, refresh: true);
    }

    private PageAddress? FirstOpenPage()
    {
        foreach (var a in _progress.CrossroadsAreas.Concat(_progress.OpenedAreas))
            if (_progress.IsPageOpen(new PageAddress(a.Biome, a.Instance, 0))) return new PageAddress(a.Biome, a.Instance, 0);
        return null;
    }

    private void LoadPage(PageAddress addr, bool refresh)
    {
        _page = WorldService.Generator.Page(addr);
        WorldService.CurrentPage = addr;
        _selected = null;
        Rebuild();
        if (refresh) _ = RefreshDiscoveries();
    }

    private async System.Threading.Tasks.Task RefreshDiscoveries()
    {
        var key = _page.Key;
        await WorldService.RefreshDiscoveries(_page);
        Callable.From(() => { if (IsInstanceValid(this) && _page.Key == key) Rebuild(); }).CallDeferred();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Layout
    // ═══════════════════════════════════════════════════════════════════

    private void Rebuild()
    {
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        _progress = WorldService.Progress;
        _vis = _progress.Visibility(_page, WorldService.Discovered(_page));
        var vp = GetViewportRect().Size;
        var tint = ZoneTint(_page.Strata);

        var bg = new ColorRect { Color = new Color(0.055f, 0.05f, 0.04f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var fog = new TextureRect
        {
            Texture = new NoiseTexture2D { Width = 512, Height = 256, Seamless = true, Noise = new FastNoiseLite { Seed = (int)(StableHash.Of(_page.Key) & 0x7FFFFFFF), Frequency = 0.012f } },
            StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Modulate = new Color(tint.R, tint.G, tint.B, 0.22f), MouseFilter = MouseFilterEnum.Ignore,
        };
        fog.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(fog);

        // The page itself: a framed plate.
        var plate = new Panel { MouseFilter = MouseFilterEnum.Ignore, Position = _mapRect.Position - new Vector2(16, 16), Size = _mapRect.Size + new Vector2(32, 32) };
        plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(tint.R * 0.18f, tint.G * 0.18f, tint.B * 0.18f, 0.55f),
            BorderColor = new Color(0.62f, 0.52f, 0.30f, 0.7f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        });
        AddChild(plate);

        _canvas = new Control { Position = _mapRect.Position, Size = _mapRect.Size, MouseFilter = MouseFilterEnum.Pass };
        AddChild(_canvas);
        _roads = new RoadLayer { Scene = this, Size = _mapRect.Size, MouseFilter = MouseFilterEnum.Ignore };
        _canvas.AddChild(_roads);
        BuildBlips();
        BuildTopBar(vp);
        BuildPanel(vp);
        BuildPageNav(vp);
        BuildLegend(vp);
        ShowSelection();
    }

    private Vector2 ToScreen(Blip b) => new(b.X / 1000f * _mapRect.Size.X, b.Y / 600f * _mapRect.Size.Y);

    private static Color ZoneTint(string strata) => strata switch
    {
        "VERDANT" => new Color(0.30f, 0.62f, 0.32f),
        "EMBER" => new Color(0.86f, 0.40f, 0.16f),
        "TIDE" => new Color(0.20f, 0.52f, 0.78f),
        "HOLLOW" => new Color(0.50f, 0.34f, 0.70f),
        "DAWN" => new Color(0.92f, 0.76f, 0.36f),
        _ => new Color(0.6f, 0.55f, 0.45f),
    };

    private static Color KindColour(Blip b) => b.Kind switch
    {
        BlipKind.Duel => new Color(0.78f, 0.62f, 0.38f),
        BlipKind.Elite => new Color(0.86f, 0.36f, 0.26f),
        BlipKind.Warden => new Color(0.95f, 0.78f, 0.30f),
        BlipKind.AreaBoss => new Color(0.92f, 0.22f, 0.20f),
        BlipKind.Event => new Color(0.36f, 0.74f, 0.70f),
        BlipKind.Lore => new Color(0.70f, 0.52f, 0.92f),
        BlipKind.Gateway => new Color(0.95f, 0.95f, 1.00f),
        _ => Parchment,
    };

    private static string KindName(Blip b) => b.Kind switch
    {
        BlipKind.Duel => "A fight",
        BlipKind.Elite => "Elite fight",
        BlipKind.Warden => "Warden — opens the next page",
        BlipKind.AreaBoss => "Area boss — opens the next area",
        BlipKind.Event => b.Event switch
        {
            EventKind.Shrine => "Shrine — rest",
            EventKind.Merchant => "Merchant",
            EventKind.Dig => "Dig site",
            EventKind.Cache => "Hidden cache",
            _ => "Something to find",
        },
        BlipKind.Lore => "Lore",
        BlipKind.Gateway => "A hidden road",
        _ => "",
    };

    private void BuildBlips()
    {
        foreach (var b in _page.Blips)
        {
            var v = _vis[b.Index];
            if (v == BlipVisibility.Hidden) continue;
            var pos = ToScreen(b);
            if (v == BlipVisibility.Uncharted)
            {
                // No dot. A faint glint where the road gives out — tap it to go first.
                var from = _page.Blips.FirstOrDefault(p => p.Next.Contains(b.Index) && _progress.Cleared.Contains(p.Id));
                var start = from != null ? ToScreen(from) : new Vector2(6, pos.Y);
                var tip = start + (pos - start) * 0.62f;
                var glint = new Button { Flat = true, Position = tip - new Vector2(34, 34), Size = new Vector2(68, 68), TooltipText = "Uncharted" };
                glint.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
                glint.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
                glint.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
                glint.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
                var spark = new Label { Text = "✦", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
                spark.SetAnchorsPreset(LayoutPreset.FullRect);
                spark.AddThemeFontSizeOverride("font_size", 34);
                spark.AddThemeColorOverride("font_color", new Color(1, 0.95f, 0.8f, 0.85f));
                glint.AddChild(spark);
                if (_selected == b.Index)
                {
                    var ring = new Panel { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(10, 10), Size = new Vector2(48, 48) };
                    ring.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                    {
                        BgColor = new Color(1, 0.92f, 0.6f, 0.08f), BorderColor = new Color(1, 0.92f, 0.6f, 0.9f),
                        BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
                        CornerRadiusTopLeft = 24, CornerRadiusTopRight = 24, CornerRadiusBottomLeft = 24, CornerRadiusBottomRight = 24,
                    });
                    glint.AddChild(ring);
                }
                var t = spark.CreateTween().SetLoops();
                t.TweenProperty(spark, "modulate:a", 0.25f, 1.1f);
                t.TweenProperty(spark, "modulate:a", 1.0f, 1.1f);
                int idx = b.Index;
                glint.Pressed += () => Select(idx);
                _canvas.AddChild(glint);
                continue;
            }

            float d = b.Kind switch { BlipKind.AreaBoss => 92, BlipKind.Warden => 78, BlipKind.Elite => 58, _ => 48 };
            var col = KindColour(b);
            bool dim = v == BlipVisibility.Known;
            bool done = v == BlipVisibility.Cleared;
            var btn = new Button { Position = pos - new Vector2(d, d) / 2, Size = new Vector2(d, d), TooltipText = b.Name, FocusMode = FocusModeEnum.None };
            StyleBoxFlat Dot(float alpha, float rimBoost) => new()
            {
                BgColor = done ? new Color(0.20f, 0.16f, 0.08f, 0.95f) : new Color(col.R * 0.55f, col.G * 0.55f, col.B * 0.55f, alpha),
                BorderColor = done ? ThemeTokens.Gold : new Color(col.R, col.G, col.B, Math.Min(1f, alpha + rimBoost)),
                BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
                CornerRadiusTopLeft = (int)d, CornerRadiusTopRight = (int)d, CornerRadiusBottomLeft = (int)d, CornerRadiusBottomRight = (int)d,
                ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 4, AntiAliasing = true,
            };
            float a = dim ? 0.40f : 0.95f;
            btn.AddThemeStyleboxOverride("normal", Dot(a, 0.1f));
            btn.AddThemeStyleboxOverride("hover", Dot(Math.Min(1, a + 0.15f), 0.3f));
            btn.AddThemeStyleboxOverride("pressed", Dot(a, 0.4f));
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            string glyph = done ? "✓" : b.Kind switch
            {
                BlipKind.Warden => "♜", BlipKind.AreaBoss => "☠", BlipKind.Elite => "!", BlipKind.Event => "◆",
                BlipKind.Lore => "✎", BlipKind.Gateway => "✧", _ => "",
            };
            if (glyph != "")
            {
                var g = new Label { Text = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
                g.SetAnchorsPreset(LayoutPreset.FullRect);
                g.AddThemeFontSizeOverride("font_size", (int)(d * 0.46f));
                g.AddThemeColorOverride("font_color", done ? ThemeTokens.Gold : new Color(1, 0.97f, 0.9f, dim ? 0.5f : 1f));
                btn.AddChild(g);
            }
            if (_selected == b.Index)
            {
                var ring = new Panel { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(-8, -8), Size = new Vector2(d + 16, d + 16) };
                ring.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = Colors.Transparent, BorderColor = new Color(1, 0.92f, 0.6f, 0.9f),
                    BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
                    CornerRadiusTopLeft = (int)d, CornerRadiusTopRight = (int)d, CornerRadiusBottomLeft = (int)d, CornerRadiusBottomRight = (int)d,
                });
                btn.AddChild(ring);
            }
            int i2 = b.Index;
            btn.Pressed += () => Select(i2);
            _canvas.AddChild(btn);
        }
    }

    /// <summary>Roads: drawn under the dots. Roads into uncharted places fade out and stop.</summary>
    private partial class RoadLayer : Control
    {
        public WorldMapScene Scene = null!;
        public override void _Draw()
        {
            var s = Scene;
            var road = new Color(0.80f, 0.70f, 0.50f, 0.75f);
            var faint = new Color(0.80f, 0.70f, 0.50f, 0.28f);
            foreach (var b in s._page.Blips)
            {
                var vb = s._vis[b.Index];
                bool fromHere = vb is BlipVisibility.Cleared;
                // Entry roads come in from the left edge of the page.
                if (b.IsEntry && vb != BlipVisibility.Hidden)
                    Seg(new Vector2(6, s.ToScreen(b).Y), b, vb);
                if (!fromHere) { foreach (var n in b.Next) if (s._vis[n] is BlipVisibility.Cleared or BlipVisibility.Visited) DrawLine(s.ToScreen(b), s.ToScreen(s._page.Blips[n]), faint, 3, true); continue; }
                foreach (var n in b.Next) Seg(s.ToScreen(b), s._page.Blips[n], s._vis[n]);
            }

            void Seg(Vector2 from, Blip to, BlipVisibility v)
            {
                var p = s.ToScreen(to);
                if (v == BlipVisibility.Hidden) return;
                if (v == BlipVisibility.Uncharted)
                {
                    // The road runs out into nothing.
                    var end = from + (p - from) * 0.62f;
                    const int steps = 10;
                    for (int i = 0; i < steps; i++)
                    {
                        var a = from + (end - from) * (i / (float)steps);
                        var c = from + (end - from) * ((i + 1) / (float)steps);
                        DrawLine(a, c, new Color(road.R, road.G, road.B, road.A * (1f - i / (float)steps)), 4, true);
                    }
                    return;
                }
                DrawLine(from, p, v == BlipVisibility.Known ? faint : road, v == BlipVisibility.Known ? 3 : 5, true);
            }
        }
    }

    private void BuildTopBar(Vector2 vp)
    {
        var back = new Button { Text = "◀  Crossroads", Position = new Vector2(30, 28), Size = new Vector2(250, 70) };
        StyleButton(back);
        back.Pressed += () => Go(WorldService.CrossroadsScenePath);
        AddChild(back);

        _title = new Label { Text = _page.BiomeName, Position = new Vector2(300, 18), Size = new Vector2(vp.X - 900, 60), HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontOverride("font", ThemeTokens.GetCardNameFont(50));
        _title.AddThemeFontSizeOverride("font_size", 50);
        _title.AddThemeColorOverride("font_color", ThemeTokens.Gold);
        AddChild(_title);
        _subtitle = new Label
        {
            Text = $"Area {_page.Address.Instance + 1}  ·  Page {_page.Address.Page + 1} of {_page.PagesInArea}",
            Position = new Vector2(300, 78), Size = new Vector2(vp.X - 900, 36), HorizontalAlignment = HorizontalAlignment.Center,
        };
        _subtitle.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(28));
        _subtitle.AddThemeFontSizeOverride("font_size", 28);
        _subtitle.AddThemeColorOverride("font_color", Parchment);
        AddChild(_subtitle);

        int beaten = _page.Blips.Count(b => _progress.Cleared.Contains(b.Id));
        int open = _progress.Frontier(_page).Count();
        _count = new Label { Text = $"{beaten} of {_page.Blips.Count} places beaten\n{open} road{(open == 1 ? "" : "s")} open", Position = new Vector2(vp.X - 590, 26), Size = new Vector2(560, 80), HorizontalAlignment = HorizontalAlignment.Right };
        _count.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(26));
        _count.AddThemeFontSizeOverride("font_size", 26);
        _count.AddThemeColorOverride("font_color", Dim);
        AddChild(_count);
    }

    private void BuildPanel(Vector2 vp)
    {
        _panel = new Panel { Position = new Vector2(vp.X - 440, 130), Size = new Vector2(410, vp.Y - 130 - 120) };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.08f, 0.065f, 0.92f), BorderColor = new Color(0.62f, 0.52f, 0.30f, 0.8f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        });
        AddChild(_panel);
        var m = new MarginContainer();
        m.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) m.AddThemeConstantOverride("margin_" + side, 22);
        _panel.AddChild(m);
        _panelBody = new VBoxContainer();
        _panelBody.AddThemeConstantOverride("separation", 14);
        m.AddChild(_panelBody);
    }

    private Label PanelLabel(string text, int size, Color c, Font? font = null)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontOverride("font", font ?? ThemeTokens.GetBodyFont(size));
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", c);
        _panelBody.AddChild(l);
        return l;
    }

    private void Select(int idx)
    {
        _selected = idx;
        Rebuild();
    }

    private void ShowSelection()
    {
        foreach (var c in _panelBody.GetChildren()) { _panelBody.RemoveChild(c); c.QueueFree(); }
        if (_selected is not int idx)
        {
            PanelLabel("Choose a place", 34, ThemeTokens.Gold, ThemeTokens.GetHeaderFont(34));
            PanelLabel("Every road from a place you've beaten shows you one step further.\n\nA dimmed circle: another Delver has been there.\n\nA road that runs out into nothing: no one has EVER been there. Follow it and you are the first.", 24, Parchment);
            return;
        }
        var b = _page.Blips[idx];
        var v = _vis[idx];
        bool uncharted = v == BlipVisibility.Uncharted;
        PanelLabel(uncharted ? "Uncharted" : b.Name, 36, ThemeTokens.Gold, ThemeTokens.GetHeaderFont(36));
        PanelLabel(uncharted ? "No Delver has ever walked this road." : KindName(b), 26, Parchment);
        if (!uncharted && EncounterForge.IsFight(b))
        {
            var enc = Runewake.Engine.World.EncounterForge.For(WorldService.Atlas, _page, b, WorldService.Pool);
            PanelLabel(enc.Name, 28, Parchment, ThemeTokens.GetButtonFont(28));
            int stars = 1 + (int)Math.Floor(b.Difficulty * 5);
            PanelLabel(new string('★', Math.Min(5, stars)) + new string('☆', Math.Max(0, 5 - stars)) + $"   Vigor {enc.EnemyVigor ?? 25}", 26, Dim);
            PanelLabel($"Reward: {enc.ShardReward} shards", 24, Dim);
        }
        string action = v switch
        {
            BlipVisibility.Cleared => "",
            BlipVisibility.Uncharted => "Be the first",
            _ => EncounterForge.IsFight(b) ? "Fight" : b.Kind == BlipKind.Gateway ? "Follow the road" : "Go there",
        };
        if (v == BlipVisibility.Cleared) PanelLabel("You have beaten this place.", 24, ThemeTokens.Gold);
        if (action != "" && !_busy)
        {
            var go = new Button { Text = action, CustomMinimumSize = new Vector2(0, 84) };
            StyleButton(go, gold: true);
            go.Pressed += () => Travel(b);
            _panelBody.AddChild(go);
        }
    }

    private void BuildPageNav(Vector2 vp)
    {
        _pageNav = new HBoxContainer { Position = new Vector2(40, vp.Y - 100), Size = new Vector2(vp.X - 560, 80) };
        _pageNav.AddThemeConstantOverride("separation", 12);
        AddChild(_pageNav);
        var a = _page.Address;
        for (int p = 0; p < _page.PagesInArea; p++)
        {
            var addr = a with { Page = p };
            bool open = _progress.IsPageOpen(addr);
            var b = new Button { Text = p == a.Page ? $"Page {p + 1}" : $"{p + 1}", CustomMinimumSize = new Vector2(p == a.Page ? 150 : 70, 70), Disabled = !open };
            StyleButton(b, gold: p == a.Page);
            b.Pressed += () => LoadPage(addr, refresh: true);
            _pageNav.AddChild(b);
        }
        var next = WorldService.Generator.NextPage(a with { Page = _page.PagesInArea - 1 });
        if (_progress.IsPageOpen(next))
        {
            var b = new Button { Text = $"Area {next.Instance + 1}  ▶", CustomMinimumSize = new Vector2(200, 70) };
            StyleButton(b, gold: true);
            b.Pressed += () => LoadPage(next, refresh: true);
            _pageNav.AddChild(b);
        }
    }

    private void BuildLegend(Vector2 vp)
    {
        var l = new Label
        {
            Text = "✓ beaten    ● dimmed: found by others    ✦ road into nothing: be the first",
            Position = new Vector2(vp.X - 1300, vp.Y - 84), Size = new Vector2(1270, 40), HorizontalAlignment = HorizontalAlignment.Right,
        };
        l.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(22));
        l.AddThemeFontSizeOverride("font_size", 22);
        l.AddThemeColorOverride("font_color", Dim);
        AddChild(l);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Travel
    // ═══════════════════════════════════════════════════════════════════

    private async void Travel(Blip b)
    {
        if (_busy) return;
        if (!_progress.Visit(_page, b)) { Toast("You can't reach that yet."); return; }
        _busy = true;
        GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
        WorldService.MarkVisited(b);

        bool first = false;
        try
        {
            var t = WorldService.Discover(_page, b);
            var done = await System.Threading.Tasks.Task.WhenAny(t, System.Threading.Tasks.Task.Delay(2500));
            first = done == t && t.Result;
        }
        catch (Exception ex) { GD.Print($"[World] discover threw: {ex.Message}"); }

        // Continue on the main thread whatever thread the await resumed on.
        Callable.From(() => Arrive(b, first)).CallDeferred();
    }

    private void Arrive(Blip b, bool first)
    {
        if (!IsInstanceValid(this)) return;
        if (first) FirstDiscovery(b);
        _busy = false;
        if (EncounterForge.IsFight(b))
        {
            WorldService.ArmFight(_page, b);
            if (first) GetTree().CreateTimer(2.2).Timeout += () => Go(CampaignRun.DuelScenePath);
            else Go(CampaignRun.DuelScenePath);
            return;
        }

        // Non-fights resolve here: record the clear, redraw, then show what happened.
        string title = b.Name, text;
        string? actionLabel = null; Action? action = null;
        var prog = CampaignContext.Progression;
        switch (b.Kind)
        {
            case BlipKind.Lore:
                var lore = WorldService.Atlas.Biome(_page.Address.Biome).Lore;
                text = lore.Count > 0 ? lore[(int)(StableHash.Of(b.Id) % (ulong)lore.Count)] : "Old writing, too worn to read.";
                break;
            case BlipKind.Gateway:
                title = "A hidden road";
                var to = b.GatewayTo != null ? WorldService.Atlas.Biome(b.GatewayTo).Name : "somewhere no map shows";
                text = $"Behind a fallen stone the road goes on — down into {to}.";
                break;
            default:
                switch (b.Event)
                {
                    case EventKind.Shrine: prog.Shards += 25; text = "You rest at the shrine. +25 shards."; break;
                    case EventKind.Merchant: prog.Shards += 15; text = "A travelling merchant trades you a few shards for news of the road. +15 shards.\n(Proper merchant stock comes with the shop update.)"; break;
                    case EventKind.Dig: prog.DigCharges += 1; text = "Loose earth and old tools. +1 dig charge."; break;
                    default: prog.Shards += 40; prog.DigCharges += 1; text = "A cache someone meant to come back for. +40 shards, +1 dig charge."; break;
                }
                break;
        }
        var unlock = WorldService.MarkCleared(_page, b);
        if (unlock.Kind == WorldUnlockKind.HiddenArea && unlock.Page is PageAddress hidden)
        {
            actionLabel = "Follow it";
            action = () => LoadPage(hidden, refresh: true);
        }
        Rebuild();
        Popup(title, text, actionLabel, action);
    }

    private void FirstDiscovery(Blip b)
    {
        var vp = GetViewportRect().Size;
        var banner = new PanelContainer { Position = new Vector2(vp.X * 0.18f, vp.Y * 0.36f), Size = new Vector2(vp.X * 0.64f, 220), ZIndex = 100, MouseFilter = MouseFilterEnum.Ignore };
        banner.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.06f, 0.03f, 0.95f), BorderColor = ThemeTokens.Gold,
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
        });
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        banner.AddChild(col);
        foreach (var (text, size, font) in new[] { ("FIRST DISCOVERY", 34, ThemeTokens.GetHeaderFont(34)), (b.Name, 58, ThemeTokens.GetCardNameFont(58)), ("No Delver has ever been here before you.", 28, ThemeTokens.GetBodyFont(28)) })
        {
            var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", font); l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", size == 58 ? ThemeTokens.Gold : Parchment);
            col.AddChild(l);
        }
        GetTree().Root.AddChild(banner);   // survives the scene change into the duel
        var t = banner.CreateTween();
        banner.Modulate = new Color(1, 1, 1, 0);
        t.TweenProperty(banner, "modulate:a", 1f, 0.3f);
        t.TweenInterval(2.4);
        t.TweenProperty(banner, "modulate:a", 0f, 0.5f);
        t.TweenCallback(Callable.From(() => banner.QueueFree()));
        GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("victory");
    }

    private void Popup(string title, string text, string? actionLabel = null, Action? action = null)
    {
        var vp = GetViewportRect().Size;
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), ZIndex = 90 };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        var box = new PanelContainer { Position = new Vector2(vp.X * 0.22f, vp.Y * 0.22f), Size = new Vector2(vp.X * 0.56f, vp.Y * 0.5f) };
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.08f, 0.065f, 0.98f), BorderColor = ThemeTokens.Gold,
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            ContentMarginLeft = 40, ContentMarginRight = 40, ContentMarginTop = 30, ContentMarginBottom = 30,
        });
        dim.AddChild(box);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 22);
        box.AddChild(col);
        var t = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
        t.AddThemeFontOverride("font", ThemeTokens.GetCardNameFont(48)); t.AddThemeFontSizeOverride("font_size", 48);
        t.AddThemeColorOverride("font_color", ThemeTokens.Gold);
        col.AddChild(t);
        var body = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(32)); body.AddThemeFontSizeOverride("font_size", 32);
        body.AddThemeColorOverride("font_color", Parchment);
        col.AddChild(body);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 20);
        col.AddChild(row);
        if (actionLabel != null)
        {
            var a = new Button { Text = actionLabel, CustomMinimumSize = new Vector2(300, 84) };
            StyleButton(a, gold: true);
            a.Pressed += () => { dim.QueueFree(); action?.Invoke(); };
            row.AddChild(a);
        }
        var ok = new Button { Text = actionLabel == null ? "Onward" : "Later", CustomMinimumSize = new Vector2(240, 84) };
        StyleButton(ok);
        ok.Pressed += () => dim.QueueFree();
        row.AddChild(ok);
    }

    private void Toast(string text)
    {
        var l = new Label { Text = text, Position = new Vector2(60, GetViewportRect().Size.Y - 170), ZIndex = 80 };
        l.AddThemeFontSizeOverride("font_size", 30);
        l.AddThemeColorOverride("font_color", new Color(1, 0.6f, 0.5f));
        AddChild(l);
        GetTree().CreateTimer(2.5).Timeout += () => { if (IsInstanceValid(l)) l.QueueFree(); };
    }

    private void Go(string path)
    {
        DuelScene.ExitTrace($"world map → {path.GetFile()}");
        GetTree().ChangeSceneToFile(path);
    }

    internal static void StyleButton(Button b, bool gold = false)
    {
        b.AddThemeFontOverride("font", ThemeTokens.GetButtonFont(32));
        b.AddThemeFontSizeOverride("font_size", 32);
        b.AddThemeColorOverride("font_color", gold ? ThemeTokens.Gold : new Color(0.91f, 0.86f, 0.78f));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.45f, 0.42f, 0.36f));
        b.AddThemeStyleboxOverride("normal", MenuButtons.Normal());
        b.AddThemeStyleboxOverride("hover", MenuButtons.Hover());
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.AddThemeStyleboxOverride("disabled", MenuButtons.Pressed());
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
    }
}
