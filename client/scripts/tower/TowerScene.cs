using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Supabase;
using Runewake.Engine.Tower;

namespace Runewake.Client;

/// <summary>
/// FABLE-020: a Tower floor — an authored, themed area with many places, three
/// wings, three Keepers, and a raid boss behind all three keys.
///
/// The floor comes from content/tower/floor_NNN.json; when signed in, a newer
/// version from Supabase (tower_floors.definition) replaces it, so floors can
/// be retuned without an app update. Progress rides in the save as
/// "tw|&lt;floor&gt;|&lt;place&gt;". The raid boss runs as a co-op expedition (engine/Coop):
/// "Raid with allies" today uses AI allies on this phone; the networked lobby
/// plugs into the same expedition.
/// </summary>
public partial class TowerScene : Control
{
    public const string ScenePath = "res://scenes/tower/TowerScene.tscn";
    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private static readonly Color Dim = new(0.62f, 0.58f, 0.50f);

    public static int CurrentFloor { get; set; } = 1;
    private static readonly Dictionary<int, TowerFloorDef> _remote = new();

    private TowerFloorDef _floor = null!;
    private HashSet<string> _cleared = new();
    private string? _selected;
    private Rect2 _mapRect;
    private string _status = "";
    private VBoxContainer _panelBody = null!;

    public static string NodeId(int floor, string place) => $"tw|{floor}|{place}";

    public static bool IsTowerNode(string? id) => id != null && id.StartsWith("tw|");

    public static TowerFloorDef LoadFloor(int n)
    {
        if (_remote.TryGetValue(n, out var r)) return r;
        return TowerFloorDef.FromJson(Godot.FileAccess.GetFileAsString($"res://content/tower/floor_{n:000}.json"));
    }

    public override void _Ready()
    {
        DuelScene.ExitTrace("arrived: TowerScene");
        var vp = GetViewportRect().Size;
        _mapRect = new Rect2(40, 150, vp.X - 40 - 500, vp.Y - 150 - 60);
        _floor = LoadFloor(CurrentFloor);
        Rebuild();
        _ = RefreshFromServer();
    }

    private async System.Threading.Tasks.Task RefreshFromServer()
    {
        try
        {
            var sm = CampaignContext.SyncManager;
            if (sm == null || !IsInstanceValid(sm) || !sm.IsConfigured || sm.Session?.IsValid != true) return;
            var sync = new TowerSync(sm.Config, Http.Create(12));
            var def = await sync.FloorDefinition(sm.Session!, CurrentFloor);
            if (def.ok && def.definitionJson != null && def.version > _floor.Version)
            {
                try { _remote[CurrentFloor] = TowerFloorDef.FromJson(def.definitionJson); }
                catch (Exception ex) { GD.PrintErr($"[Tower] server floor unreadable: {ex.Message}"); }
            }
            var st = await sync.Status(sm.Session!);
            if (st.ok)
            {
                var me = st.floors.FirstOrDefault(f => f.Floor == CurrentFloor);
                var next = st.floors.FirstOrDefault(f => f.Floor == CurrentFloor + 1);
                if (me != null)
                    _status = $"{me.UniqueClears} of {me.UnlockThreshold} Delvers have felled this floor's boss." +
                              (next is { IsOpen: true } ? $"  Floor {next.Floor} is open." : $"  Floor {CurrentFloor + 1} opens at {me.UnlockThreshold}.");
            }
        }
        catch (Exception ex) { GD.Print($"[Tower] server refresh failed: {ex.Message}"); return; }
        Callable.From(() => { if (IsInstanceValid(this)) { _floor = LoadFloor(CurrentFloor); Rebuild(); } }).CallDeferred();
    }

    private void Rebuild()
    {
        foreach (var c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        var prog = CampaignContext.Progression;
        string prefix = $"tw|{_floor.Floor}|";
        _cleared = new HashSet<string>(prog?.ClearedNodes.Where(x => x.StartsWith(prefix)).Select(x => x[prefix.Length..]) ?? Enumerable.Empty<string>());
        var vp = GetViewportRect().Size;

        var bg = new ColorRect { Color = new Color(0.045f, 0.04f, 0.035f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var title = new Label { Text = $"The Tower — Floor {_floor.Floor}: {_floor.Title}", Position = new Vector2(300, 20), Size = new Vector2(vp.X - 900, 70), HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontOverride("font", ThemeTokens.GetCardNameFont(52)); title.AddThemeFontSizeOverride("font_size", 52);
        title.AddThemeColorOverride("font_color", ThemeTokens.Gold);
        AddChild(title);
        var st = new Label { Text = _status == "" ? $"The boss must fall to {_floor.UnlockThreshold} different Delvers before floor {_floor.Floor + 1} opens." : _status, Position = new Vector2(300, 90), Size = new Vector2(vp.X - 900, 40), HorizontalAlignment = HorizontalAlignment.Center };
        st.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(26)); st.AddThemeFontSizeOverride("font_size", 26);
        st.AddThemeColorOverride("font_color", Dim);
        AddChild(st);

        var back = new Button { Text = "◀  Crossroads", Position = new Vector2(30, 30), Size = new Vector2(250, 70) };
        WorldMapScene.StyleButton(back);
        back.Pressed += () => GetTree().ChangeSceneToFile(WorldService.CrossroadsScenePath);
        AddChild(back);

        var canvas = new Control { Position = _mapRect.Position, Size = _mapRect.Size };
        AddChild(canvas);
        var roads = new TowerRoads { Scene = this, Size = _mapRect.Size, MouseFilter = MouseFilterEnum.Ignore };
        canvas.AddChild(roads);

        foreach (var p in _floor.Places)
        {
            bool done = _cleared.Contains(p.Id);
            bool open = !done && TowerProgress.IsOpen(_floor, p, _cleared);
            float d = p.Kind switch { TowerPlaceKind.Boss => 120, TowerPlaceKind.Keeper => 86, TowerPlaceKind.Elite => 62, _ => 52 };
            var col = WingColour(p.Wing);
            var btn = new Button { Position = ToScreen(p) - new Vector2(d, d) / 2, Size = new Vector2(d, d), TooltipText = p.Name, FocusMode = FocusModeEnum.None };
            StyleBoxFlat Dot(float a) => new()
            {
                BgColor = done ? new Color(0.20f, 0.16f, 0.08f, 0.95f) : new Color(col.R * 0.5f, col.G * 0.5f, col.B * 0.5f, a),
                BorderColor = done ? ThemeTokens.Gold : new Color(col.R, col.G, col.B, open ? 1f : 0.35f),
                BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
                CornerRadiusTopLeft = (int)d, CornerRadiusTopRight = (int)d, CornerRadiusBottomLeft = (int)d, CornerRadiusBottomRight = (int)d,
                ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 4, AntiAliasing = true,
            };
            float alpha = done ? 1f : open ? 0.95f : 0.30f;
            btn.AddThemeStyleboxOverride("normal", Dot(alpha));
            btn.AddThemeStyleboxOverride("hover", Dot(Math.Min(1, alpha + 0.15f)));
            btn.AddThemeStyleboxOverride("pressed", Dot(alpha));
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            string glyph = done ? "✓" : p.Kind switch
            {
                TowerPlaceKind.Boss => "☠", TowerPlaceKind.Keeper => "⚷", TowerPlaceKind.Elite => "!", TowerPlaceKind.Event => "◆", TowerPlaceKind.Lore => "✎", _ => "",
            };
            if (glyph != "")
            {
                var g = new Label { Text = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
                g.SetAnchorsPreset(LayoutPreset.FullRect);
                g.AddThemeFontSizeOverride("font_size", (int)(d * 0.46f));
                g.AddThemeColorOverride("font_color", done ? ThemeTokens.Gold : new Color(1, 0.97f, 0.9f, open ? 1f : 0.4f));
                btn.AddChild(g);
            }
            string id = p.Id;
            btn.Pressed += () => { _selected = id; Rebuild(); };
            canvas.AddChild(btn);
            if (_selected == p.Id)
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
        }

        var panel = new Panel { Position = new Vector2(vp.X - 470, 150), Size = new Vector2(440, vp.Y - 210) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.08f, 0.065f, 0.92f), BorderColor = new Color(0.62f, 0.52f, 0.30f, 0.8f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        });
        AddChild(panel);
        var m = new MarginContainer();
        m.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) m.AddThemeConstantOverride("margin_" + side, 22);
        panel.AddChild(m);
        _panelBody = new VBoxContainer();
        _panelBody.AddThemeConstantOverride("separation", 14);
        m.AddChild(_panelBody);
        ShowSelection();
    }

    private Vector2 ToScreen(TowerPlaceDef p) => new(p.X / 1000f * _mapRect.Size.X, p.Y / 600f * _mapRect.Size.Y);

    private static Color WingColour(string? wing) => wing switch
    {
        "thorn" => new Color(0.36f, 0.66f, 0.34f),
        "ossuary" => new Color(0.62f, 0.52f, 0.78f),
        "flooded" => new Color(0.26f, 0.56f, 0.80f),
        "crown" => new Color(0.92f, 0.30f, 0.24f),
        _ => new Color(0.80f, 0.66f, 0.40f),
    };

    private partial class TowerRoads : Control
    {
        public TowerScene Scene = null!;
        public override void _Draw()
        {
            foreach (var p in Scene._floor.Places)
                foreach (var n in p.Next)
                {
                    var q = Scene._floor.Place(n);
                    bool lit = Scene._cleared.Contains(p.Id);
                    DrawLine(Scene.ToScreen(p), Scene.ToScreen(q), new Color(0.80f, 0.70f, 0.50f, lit ? 0.8f : 0.25f), lit ? 5 : 3, true);
                }
        }
    }

    private Label Line(string text, int size, Color c, Font? font = null)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontOverride("font", font ?? ThemeTokens.GetBodyFont(size));
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", c);
        _panelBody.AddChild(l);
        return l;
    }

    private void ShowSelection()
    {
        if (_selected == null)
        {
            Line(_floor.Title, 36, ThemeTokens.Gold, ThemeTokens.GetHeaderFont(36));
            foreach (var s in _floor.Intro) Line(s, 23, Parchment);
            Line("Three Keepers hold three keys. The keys wake the boss.", 23, Dim);
            return;
        }
        var p = _floor.Place(_selected);
        bool done = _cleared.Contains(p.Id);
        bool open = !done && TowerProgress.IsOpen(_floor, p, _cleared);
        Line(p.Name, 34, ThemeTokens.Gold, ThemeTokens.GetHeaderFont(34));
        var wing = _floor.Wings.FirstOrDefault(w => w.Id == p.Wing);
        if (wing != null) Line(wing.Name, 24, Dim);
        if (p.Encounter != null)
        {
            if (p.Encounter.DialogueIntro is { Count: > 0 } intro) Line(intro[0], 23, Parchment);
            Line(p.Kind == TowerPlaceKind.Boss
                ? $"RAID — up to {_floor.Raid.MaxPlayers} Delvers share one pool: {_floor.Raid.PoolFor(1)} Vigor alone, {_floor.Raid.PoolFor(_floor.Raid.MaxPlayers)} for a full party. {_floor.Raid.MaxRounds} rounds."
                : $"Vigor {p.Encounter.EnemyVigor ?? 25}" + (p.Encounter.EnemyBonusAttunement > 0 ? $"  ·  +{p.Encounter.EnemyBonusAttunement} Attunement" : ""), 24, Dim);
            // FABLE-021: the rules this boss bends, in plain words.
            foreach (var r in p.Encounter.BossRules ?? new System.Collections.Generic.List<string>())
                Line("◆ " + Runewake.Engine.Engine.BossRules.Describe(r), 22, new Color(0.95f, 0.62f, 0.45f));
        }
        if (p.Event != null) Line(p.Event.Text, 23, Parchment);
        if (p.Requires.Count > 0 && !open && !done)
            Line("Needs: " + string.Join(", ", p.Requires.Select(r => _floor.Place(r).Name)), 22, new Color(0.9f, 0.5f, 0.4f));
        if (done) { Line("Done.", 24, ThemeTokens.Gold); if (p.Lore != null) Line(p.Lore, 22, Parchment); return; }
        if (!open) { Line("Not reachable yet.", 24, Dim); return; }

        if (p.Kind == TowerPlaceKind.Boss)
        {
            var raid = new Button { Text = "Raid with 4 allies", CustomMinimumSize = new Vector2(0, 84) };
            WorldMapScene.StyleButton(raid, gold: true);
            raid.Pressed += () => StartRaid(p, allies: _floor.Raid.MaxPlayers - 1);
            _panelBody.AddChild(raid);
            var duo = new Button { Text = "Raid with 1 ally", CustomMinimumSize = new Vector2(0, 84) };
            WorldMapScene.StyleButton(duo);
            duo.Pressed += () => StartRaid(p, allies: 1);
            _panelBody.AddChild(duo);
            var solo = new Button { Text = "Try it alone", CustomMinimumSize = new Vector2(0, 84) };
            WorldMapScene.StyleButton(solo);
            solo.Pressed += () => StartRaid(p, allies: 0);
            _panelBody.AddChild(solo);
            Line("Allies are AI Delvers on this phone for now; the online lobby uses the same raid.", 20, Dim);
            return;
        }
        var go = new Button { Text = p.Encounter != null ? "Fight" : "Go there", CustomMinimumSize = new Vector2(0, 84) };
        WorldMapScene.StyleButton(go, gold: true);
        go.Pressed += () => Enter(p);
        _panelBody.AddChild(go);
    }

    private void Enter(TowerPlaceDef p)
    {
        GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
        var prog = CampaignContext.Progression;
        if (p.Encounter == null)
        {
            // Events and lore complete on the spot.
            if (p.Event != null)
                foreach (var r in p.Event.Rewards)
                {
                    var kv = r.Split(':');
                    if (kv.Length == 2 && int.TryParse(kv[1], out int n))
                    {
                        if (kv[0] == "shard") prog.Shards += n;
                        else if (kv[0] == "dig_charge") prog.DigCharges += n;
                    }
                }
            prog.MarkNodeCleared(NodeId(_floor.Floor, p.Id));
            try { CampaignContext.SaveManager.Save(); } catch { }
            _selected = p.Id;
            Rebuild();
            return;
        }
        Arm(p);
        GetTree().ChangeSceneToFile(CampaignRun.DuelScenePath);
    }

    private void Arm(TowerPlaceDef p)
    {
        CampaignContext.EncounterIndex[p.Encounter!.Id] = p.Encounter;
        CampaignContext.CurrentNodeId = NodeId(_floor.Floor, p.Id);
        CampaignContext.CurrentEncounter = p.Encounter;
        CampaignContext.MatchConfig = new Runewake.Engine.State.MatchConfig();
        CampaignContext.CurrentRegionSkinId = _floor.BoardSkin ?? "default";
        CampaignContext.DebugSeed = null;
    }

    private void StartRaid(TowerPlaceDef boss, int allies)
    {
        Arm(boss);
        CoopSession.StartLocalRaid(_floor, boss, allies);
        GetTree().ChangeSceneToFile(CampaignRun.DuelScenePath);
    }

    /// <summary>Called by CampaignRun after a Tower fight is won.</summary>
    public static (CampaignRun.Step, string, string) AfterTowerVictory(string nodeId)
    {
        var parts = nodeId.Split('|');
        if (parts.Length == 3 && int.TryParse(parts[1], out int floor))
        {
            var f = LoadFloor(floor);
            var place = f.Places.FirstOrDefault(p => p.Id == parts[2]);
            if (place?.Kind == TowerPlaceKind.Boss) _ = ReportBossClear(floor);
        }
        return (CampaignRun.Step.BackToMap, ScenePath, "back to the Tower");
    }

    private static async System.Threading.Tasks.Task ReportBossClear(int floor)
    {
        var sm = CampaignContext.SyncManager;
        if (sm == null || !GodotObject.IsInstanceValid(sm) || !sm.IsConfigured || sm.Session?.IsValid != true) return;
        try
        {
            var r = await new TowerSync(sm.Config, Http.Create(12)).RecordBossClear(sm.Session!, floor, CoopSession.Current?.RaidId);
            GD.Print($"[Tower] boss clear recorded: ok={r.Ok} unique={r.UniqueClears}/{r.Threshold} nextOpened={r.NextFloorOpened} firstEver={r.FirstEver} {r.Error}");
        }
        catch (Exception ex) { GD.Print($"[Tower] boss clear report failed: {ex.Message}"); }
    }
}
