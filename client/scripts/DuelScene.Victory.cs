using System;
using System.Collections.Generic;
using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// FABLE-033: the end of a duel on ONE screen.
///
/// Trikzos: "Yes, one screen, go ahead with the victory screen." Before this the
/// result was a stone panel in the middle (VICTORY, the outro, a turn count, two
/// buttons) and a separate "Spoils" list pinned to the right edge — numbers in a
/// side window, the cards you won never actually shown. Now:
///
///   VICTORY, and who fell                          — the headline
///   the loot, laid across the middle as real cards — every card the fight paid,
///                                                   with its art, plus a chip
///                                                   for shards / dig charges
///   Continue — Next: {what}  ·  Back to the map  ·  Fight again
///
/// The screen is built ONCE, and only after OnGameOver has banked the rewards.
/// That ordering is new and it matters: GameStateManager raises StateChanged
/// BEFORE GameOver, so the old overlay was built from an empty drop list and the
/// drops it was designed to reveal never appeared in a real fight (the capture
/// probe happened to call the two in the other order, which is why it looked
/// fine in screenshots). SettleGameOver is the one door in.
/// </summary>
public partial class DuelScene
{
    /// <summary>One card the fight paid. Count > 1 when the same card came twice.</summary>
    private sealed class LootCard
    {
        public string CardId = "";
        public string Name = "";
        public int Cost;
        public int? Attack;
        public int? Vigor;
        /// <summary>REWARD · RELIC · NEW · +1</summary>
        public string Tag = "";
        public int Rank;
        public int Count = 1;
    }

    private readonly List<LootCard> _loot = new();
    private int _arenaRuneDust;
    private bool _arenaDuelEnded;
    private bool _gameOverSettled;

    /// <summary>
    /// Record a card the player just received, so the end screen can show it.
    /// <paramref name="source"/> is reward · relic · drop · deck (the enemy deck cards a first
    /// win adds to the collection); it decides the tag and the order on the shelf.
    /// </summary>
    private void NoteLootCard(string? cardId, CardDef? def, string source, bool firstCopy = true)
    {
        if (string.IsNullOrEmpty(cardId) || def == null) return;
        var existing = _loot.Find(l => l.CardId == cardId);
        if (existing != null) { existing.Count++; return; }
        (string tag, int rank) = source switch
        {
            "reward" => ("REWARD", 0),
            "relic" => ("RELIC", 1),
            "drop" => (firstCopy ? "NEW" : "+1", firstCopy ? 2 : 4),
            _ => ("NEW", 3),
        };
        _loot.Add(new LootCard
        {
            CardId = cardId, Name = def.Name, Cost = def.Cost, Attack = def.Attack, Vigor = def.Vigor, Tag = tag, Rank = rank,
        });
    }

    /// <summary>
    /// Rewards are banked; the end screen may be built now. OnStateChanged builds it
    /// only once this has been called, so it always sees the full loot list.
    /// </summary>
    private void SettleGameOver()
    {
        if (_gameOverSettled) return;
        _gameOverSettled = true;
        if (_gameOverOverlay == null && _gsm != null && _gsm.IsGameOver) OnStateChanged();
    }

    // ── Layout, in the 2316×1080 design space ────────────────────────────────
    private const float LootCardW = 250f;
    private const float LootCardH = LootCardW * 1.45f;
    private const float LootChipW = 196f;
    private const float LootGap = 26f;
    private const float LootCardMinW = 186f;
    private const float ShelfTop = 318f;
    private const float ButtonsTop = 912f;

    private static readonly Color Parchment = Color.FromHtml("#E8DCC8");
    private static readonly Color Violet = Color.FromHtml("#8F6BD4");

    /// <summary>Build the end-of-duel screen — full-screen, one screen, everything on it.</summary>
    private void BuildGameOverOverlay()
    {
        _gameOverOverlay = new Control { Name = "GameOverOverlay" };
        _gameOverOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // FABLE-005: above every z-index used in the duel, so nothing draws over it or
        // steals its taps. Stop on the dim: the duel is over, nothing behind is tappable.
        _gameOverOverlay.ZIndex = GameOverZIndex;
        _gameOverOverlay.MouseFilter = Control.MouseFilterEnum.Stop;

        var vp = GetViewportRect().Size;
        float s = vp.Y / 1080f;
        float cx = vp.X / 2f;

        int winner = _gsm.State != null ? _gsm.WinnerIndex : -1;
        bool playerWon = winner == 0;
        var encounter = CampaignContext.CurrentEncounter;
        bool arena = _arenaDuelEnded;
        bool campaign = _isCampaignEncounter && encounter != null;
        string encName = arena ? (CampaignContext.ArenaEncounter?.Name ?? "") : (encounter?.Name ?? "");
        if (string.IsNullOrEmpty(encName)) encName = "your opponent";
        Color accent = playerWon ? Gold : Ember;

        var dim = new ColorRect { Color = new Color(BgDark.R, BgDark.G, BgDark.B, 0.92f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _gameOverOverlay.AddChild(dim);

        // A soft light behind the headline, gold for a win and ember for a loss.
        var glow = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = TwoStop(new Color(accent.R, accent.G, accent.B, playerWon ? 0.26f : 0.20f), new Color(accent.R, accent.G, accent.B, 0f)),
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.17f), FillTo = new Vector2(0.5f, 0.75f),
                Width = 64, Height = 64,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        glow.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _gameOverOverlay.AddChild(glow);

        var reveal = new List<Control>();

        // ── Headline ──
        var kicker = OverlayText(playerWon ? "THE FIGHT IS WON" : "THE FIGHT IS LOST", 58f * s, 26, GetHeaderFont((int)(26 * s)), new Color(0.72f, 0.66f, 0.52f), vp.X);
        var headline = OverlayText(playerWon ? "VICTORY" : "DEFEATED", 84f * s, 128, GetCardNameFont((int)(128 * s)), accent, vp.X);
        headline.Name = "Headline";
        string sub = playerWon ? $"You defeated {encName}" : $"{encName} prevails";
        if (_gsm.TurnNumber > 0) sub += $"   ·   turn {_gsm.TurnNumber}";
        var subline = OverlayText(sub, 244f * s, 38, GetBodyFont((int)(38 * s)), Parchment, vp.X);
        reveal.Add(kicker); reveal.Add(headline); reveal.Add(subline);

        // ── The loot shelf ──
        var tiles = BuildLootTiles(playerWon, encounter, s);
        float shelfW = 0f;
        foreach (var t in tiles) shelfW += t.Size.X;
        shelfW += LootGap * s * Math.Max(0, tiles.Count - 1);
        float x = cx - shelfW / 2f;
        int i = 0;
        foreach (var t in tiles)
        {
            t.Position = new Vector2(x, ShelfTop * s);
            x += t.Size.X + LootGap * s;
            _gameOverOverlay.AddChild(t);
            reveal.Add(t);
            RiseIn(t, 0.18f + i * 0.08f, s);
            i++;
        }

        // ── The outro line, under the shelf ──
        string flavour = playerWon && encounter?.DialogueOutro is { Count: > 0 } ? string.Join("  ", encounter.DialogueOutro) : "";
        if (!string.IsNullOrEmpty(flavour))
        {
            var outro = new Label
            {
                Text = flavour, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore,
                Position = new Vector2(cx - 760f * s, 772f * s), Size = new Vector2(1520f * s, 120f * s),
            };
            outro.AddThemeFontOverride("font", GetBodyFont((int)(29 * s)));
            outro.AddThemeFontSizeOverride("font_size", (int)(29 * s));
            outro.AddThemeColorOverride("font_color", TextSecondary);
            _gameOverOverlay.AddChild(outro);
            reveal.Add(outro);
        }

        // ── The way out ──
        Button? primary = null, quietA = null, quietB = null;
        if (arena)
        {
            var line = OverlayText("Returning to the arena…", 940f * s, 32, GetBodyFont((int)(32 * s)), TextMuted, vp.X);
            reveal.Add(line);
        }
        else
        {
            string currentSeed = CampaignContext.DebugSeed?.ToString() ?? "";
            void Retry(string why) => LeaveDuelFor("res://scenes/duel/DuelScene.tscn", why, () =>
            {
                // A retry keeps the seed, so the fight replays the same way.
                if (!string.IsNullOrEmpty(currentSeed)) CampaignContext.DebugSeed = ulong.Parse(currentSeed);
            });

            if (!campaign)
            {
                primary = PlateButton("Play again", null, true, s);
                ArmEndOfDuelButton(primary, "Fight Again", () => Retry("Play again pressed"));
                quietA = PlateButton("Back to title", null, false, s);
                ArmEndOfDuelButton(quietA, "Back to title", () => LeaveDuelFor(MainMenuScenePath, "Back to title pressed"));
            }
            else if (playerWon)
            {
                string next = CampaignRun.PeekAfterVictory();
                primary = PlateButton("Continue", next, true, s);
                ArmEndOfDuelButton(primary, "Continue", () =>
                {
                    var (step, scene, what) = CampaignRun.AdvanceAfterVictory();
                    LeaveDuelFor(scene, $"Continue pressed → {step} ({what})");
                });
                quietA = PlateButton("Back to the map", null, false, s);
                ArmEndOfDuelButton(quietA, "Back to the map", () =>
                {
                    // The win is banked either way; the map just does not auto-arm the next fight.
                    CampaignRun.BankVictory();
                    LeaveDuelFor(CampaignRun.MapPathFor(CampaignContext.CurrentNodeId), "Back to the map pressed");
                });
                quietB = PlateButton("Fight again", null, false, s);
                ArmEndOfDuelButton(quietB, "Fight Again", () => Retry("Fight again pressed"));
            }
            else
            {
                primary = PlateButton("Try again", null, true, s);
                ArmEndOfDuelButton(primary, "Fight Again", () => Retry("Try again pressed"));
                quietA = PlateButton("Back to the map", null, false, s);
                ArmEndOfDuelButton(quietA, "Back to the map", () =>
                    LeaveDuelFor(CampaignRun.MapPathFor(CampaignContext.CurrentNodeId), "Back to the map pressed"));
            }

            // Primary in the centre, the quiet ones flanking it.
            float pw = primary.Size.X, qw = quietA.Size.X, gap = 34f * s;
            primary.Position = new Vector2(cx - pw / 2f, ButtonsTop * s);
            quietA.Position = new Vector2(cx - pw / 2f - gap - qw, ButtonsTop * s + (primary.Size.Y - quietA.Size.Y) / 2f);
            _gameOverOverlay.AddChild(quietA);
            _gameOverOverlay.AddChild(primary);
            reveal.Add(quietA); reveal.Add(primary);
            if (quietB != null)
            {
                quietB.Position = new Vector2(cx + pw / 2f + gap, ButtonsTop * s + (primary.Size.Y - quietB.Size.Y) / 2f);
                _gameOverOverlay.AddChild(quietB);
                reveal.Add(quietB);
            }
        }

        AddChild(_gameOverOverlay);
        MenuButtons.RevealStagger(reveal, 0.06f);

        if (playerWon) RitualEffects.PlayVictoryLight(this, CampaignContext.ReduceMotion);
        else RitualEffects.PlayDefeatDrain(this, CampaignContext.ReduceMotion);

        // FABLE-015/019: prove the buttons can be touched, two frames after layout.
        if (primary != null && quietA != null) _ = AuditAfterLayout(quietB == null ? new[] { primary, quietA } : new[] { primary, quietA, quietB });

        // ═══ SOAK MODE: auto-press Continue/Return to Map after the screen shows ═══
        if (CampaignContext.SoakActive)
        {
            bool isDefeatRetry = CampaignContext.SoakDefeatPhase && !playerWon && !CampaignContext.SoakDefeatHasRetried;
            GD.Print($"[DUELSOAK] Soak mode — auto-continue (defeatRetry={isDefeatRetry}, won={playerWon}, hasRetried={CampaignContext.SoakDefeatHasRetried})");
            var soakTimer = new Godot.Timer { WaitTime = 1.5f, OneShot = true };
            soakTimer.Timeout += () =>
            {
                if (isDefeatRetry)
                {
                    CampaignContext.SoakDefeatHasRetried = true;
                    GD.Print("[DUELSOAK] Defeat test — pressing Try Again to retry");
                    GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
                }
                else
                {
                    GD.Print("[DUELSOAK] Auto-pressing Continue");
                    if (CampaignContext.CurrentNodeId != null)
                        CampaignContext.Progression.MarkNodeCleared(CampaignContext.CurrentNodeId);
                    CampaignContext.SaveManager.Save();
                    if (CampaignContext.SoakStopAfterRetry && CampaignContext.SoakDefeatHasRetried)
                    {
                        GD.Print("[DUELSOAK] SoakStopAfterRetry — quitting after retry cycle");
                        GetTree().Quit(0);
                    }
                    else
                    {
                        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
                    }
                }
            };
            _gameOverOverlay.AddChild(soakTimer);
            soakTimer.Start();
        }
    }

    /// <summary>
    /// The shelf: every card the fight paid as a real plate with its art, then a chip
    /// for each currency. On a loss the chips show what a win would have paid, dimmed.
    /// </summary>
    private List<Control> BuildLootTiles(bool playerWon, EncounterDef? encounter, float s)
    {
        var tiles = new List<Control>();
        var chips = new List<Control>();
        if (_arenaRuneDust > 0)
            chips.Add(ChipTile("dust", $"+{_arenaRuneDust}", "RUNE DUST", Violet, s, true));
        if (encounter != null)
        {
            if (encounter.ShardReward > 0)
                chips.Add(ChipTile("coin", $"+{encounter.ShardReward}", "SHARDS", Gold, s, playerWon));
            if (encounter.DigChargeReward > 0)
                chips.Add(ChipTile("diamond", $"+{encounter.DigChargeReward}", encounter.DigChargeReward == 1 ? "DIG CHARGE" : "DIG CHARGES", Moss, s, playerWon));
            if (!string.IsNullOrEmpty(encounter.FragmentReward))
            {
                var parts = encounter.FragmentReward.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int frag) && frag > 0)
                    chips.Add(ChipTile("shard", $"+{frag}", parts[0].ToUpperInvariant() + " FRAGMENTS", Amber, s, playerWon));
            }
        }

        if (playerWon && _loot.Count > 0)
        {
            // Reward and relic first, then new drops, then the deck's new cards, then duplicates.
            var ordered = new List<LootCard>(_loot);
            ordered.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : string.CompareOrdinal(a.CardId, b.CardId));

            // As many cards as fit across the screen at full size; fewer cards shrink before
            // they are dropped, and what still does not fit becomes one "+N more" chip.
            float avail = GetViewportRect().Size.X - 120f * s;
            float chipsW = chips.Count * LootChipW * s;
            int shown = ordered.Count;
            float cardW = LootCardW;
            while (true)
            {
                int extra = ordered.Count - shown;
                int nTiles = shown + chips.Count + (extra > 0 ? 1 : 0);
                float need = shown * cardW * s + chipsW + (extra > 0 ? LootChipW * s : 0f) + LootGap * s * (nTiles - 1);
                if (need <= avail || shown == 0) break;
                if (cardW > LootCardMinW) cardW = Math.Max(LootCardMinW, cardW - 10f);
                else shown--;
            }
            for (int i = 0; i < shown; i++) tiles.Add(CardTile(ordered[i], s, cardW));
            int more = ordered.Count - shown;
            if (more > 0) tiles.Add(ChipTile("cards", $"+{more}", more == 1 ? "MORE CARD" : "MORE CARDS", TextSecondary, s, false, "to your collection"));
        }
        tiles.AddRange(chips);

        if (tiles.Count == 0)
        {
            var none = new Label
            {
                Text = playerWon ? "Nothing but the win." : "Nothing at stake.",
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(600f * s, LootCardH * s),
            };
            none.AddThemeFontOverride("font", GetBodyFont((int)(34 * s)));
            none.AddThemeFontSizeOverride("font_size", (int)(34 * s));
            none.AddThemeColorOverride("font_color", TextMuted);
            tiles.Add(none);
        }
        return tiles;
    }

    /// <summary>A real card plate with a tag pill under it: REWARD, RELIC, NEW or +1.</summary>
    private Control CardTile(LootCard loot, float s, float cardW)
    {
        float w = cardW * s, h = cardW * 1.45f * s;
        var tile = new Control { Name = "Loot_" + loot.CardId, Size = new Vector2(w, h + 46f * s), MouseFilter = Control.MouseFilterEnum.Ignore };

        var plate = new CardPlate { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(w, h) };
        plate.Setup(loot.CardId, loot.Attack, loot.Vigor, w, h, loot.Cost);
        tile.AddChild(plate);

        (string text, Color tint) = loot.Tag switch
        {
            "REWARD" => ("REWARD", Gold),
            "RELIC" => ("LOST RELIC", Violet),
            "NEW" => ("NEW", Moss),
            _ => ("+1 COPY", Amber),
        };
        if (loot.Count > 1) text += $"  ×{loot.Count}";
        var pill = new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(0, h + 10f * s), Size = new Vector2(w, 32f * s),
        };
        pill.AddThemeFontOverride("font", GetHeaderFont((int)(24 * s)));
        pill.AddThemeFontSizeOverride("font_size", (int)(24 * s));
        pill.AddThemeColorOverride("font_color", tint);
        tile.AddChild(pill);
        return tile;
    }

    /// <summary>A currency chip, the same height as a card: glyph, big number, caption.</summary>
    private Control ChipTile(string gem, string amount, string caption, Color tint, float s, bool earned, string? note = null)
    {
        float w = LootChipW * s, h = LootCardH * s;
        var tile = new PanelContainer { Name = "Chip_" + caption.Replace(' ', '_'), CustomMinimumSize = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        tile.Size = new Vector2(w, h);
        tile.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(tint.R * 0.16f + 0.05f, tint.G * 0.16f + 0.045f, tint.B * 0.16f + 0.04f, 0.92f),
            BorderColor = new Color(tint.R, tint.G, tint.B, earned ? 0.85f : 0.35f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ShadowColor = new Color(0, 0, 0, 0.45f), ShadowSize = 10,
        });
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", (int)(6 * s));
        tile.AddChild(col);

        Label L(string text, Font font, int size, Color color)
        {
            var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", font);
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            col.AddChild(l);
            return l;
        }
        col.AddChild(Gem(gem, earned ? tint : new Color(tint, 0.45f), s));
        var number = L(earned ? "0" : amount.TrimStart('+'), GetCardNameFont((int)(74 * s)), (int)(74 * s), earned ? Parchment : TextMuted);
        L(caption, GetHeaderFont((int)(21 * s)), (int)(21 * s), earned ? tint : TextMuted);
        if (note != null) L(note, GetBodyFont((int)(22 * s)), (int)(22 * s), TextMuted);
        else if (!earned) L("forfeited", GetBodyFont((int)(22 * s)), (int)(22 * s), TextMuted);

        // Count up to the amount, bound to the label so it dies with it (FABLE-029).
        if (earned && amount.Length > 1 && int.TryParse(amount[1..], out int target) && !CampaignContext.ReduceMotion)
        {
            var t = number.CreateTween();
            t.TweenMethod(Callable.From<double>(v => { if (GodotObject.IsInstanceValid(number)) number.Text = "+" + (int)v; }), 0.0, (double)target, 1.1f)
                .SetDelay(0.55f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        else number.Text = earned ? amount : amount.TrimStart('+');
        return tile;
    }

    /// <summary>A drawn token for a chip: a coin, a diamond, a shard, a mote, a card back.</summary>
    private static Control Gem(string kind, Color tint, float s)
    {
        float d = 54f * s, box = d + 20f * s;
        // A plain Control, laid out by hand: a container would own the token's rect and
        // rotation-with-pivot is only dependable when nothing else is writing the rect.
        var holder = new Control { CustomMinimumSize = new Vector2(box, box), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        var rim = new Color(tint.R * 0.55f, tint.G * 0.55f, tint.B * 0.55f, tint.A);
        var face = new Color(tint.R * 0.85f + 0.12f, tint.G * 0.85f + 0.12f, tint.B * 0.85f + 0.10f, tint.A);
        StyleBoxFlat Box(int radius) => new()
        {
            BgColor = face, BorderColor = rim,
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ShadowColor = new Color(0, 0, 0, 0.45f), ShadowSize = tint.A < 1f ? 0 : 6,
        };
        (float w, float h, int radius, float rot) = kind switch
        {
            "diamond" => (d * 0.70f, d * 0.70f, 6, Mathf.Pi / 4f),      // dig charge: a square on its point
            "shard" => (d * 0.58f, d * 0.84f, 4, Mathf.Pi / 4f),        // fragment: a splinter
            "cards" => (d * 0.70f, d, 5, -0.12f),                        // a card back, a little askew
            _ => (d, d, (int)(d / 2f), 0f),                              // coin / mote: a disc
        };
        var p = new Panel { Position = new Vector2((box - w) / 2f, (box - h) / 2f), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", Box(radius));
        p.PivotOffset = new Vector2(w / 2f, h / 2f);
        p.Rotation = rot;
        holder.AddChild(p);
        return holder;
    }

    /// <summary>
    /// A title-style plate. The primary is the gold plate from the title screen; the
    /// others the quiet stone plate. Text is drawn by child labels so the primary can
    /// carry a second line ("Next: Thornbark"); the Button's own Text stays for the
    /// tooling that finds buttons by name, in a transparent colour.
    /// </summary>
    private Button PlateButton(string label, string? subline, bool primary, float s)
    {
        float w = (primary ? 640f : 380f) * s, h = (primary ? 118f : 96f) * s;
        var btn = new Button
        {
            Name = "Btn_" + label.Replace(' ', '_'), Text = label, Size = new Vector2(w, h), CustomMinimumSize = new Vector2(w, h),
            MouseFilter = Control.MouseFilterEnum.Stop, FocusMode = Control.FocusModeEnum.None,
        };
        var clear = new Color(0, 0, 0, 0);
        foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_disabled_color", "font_hover_pressed_color" })
            btn.AddThemeColorOverride(k, clear);
        btn.AddThemeStyleboxOverride("normal", primary ? MenuButtons.PrimaryNormal() : MenuButtons.QuietNormal());
        btn.AddThemeStyleboxOverride("hover", primary ? MenuButtons.PrimaryHover() : MenuButtons.Hover());
        btn.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        var col = new VBoxContainer { Name = "Col", Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        col.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", (int)(2 * s));
        btn.AddChild(col);
        var main = new Label { Name = "Face", Text = label, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        int mainSize = (int)((primary ? 44 : 30) * s);
        main.AddThemeFontOverride("font", GetButtonFont(mainSize));
        main.AddThemeFontSizeOverride("font_size", mainSize);
        main.AddThemeColorOverride("font_color", primary ? Color.FromHtml("#F2DFA6") : Color.FromHtml("#D8CBB0"));
        col.AddChild(main);
        if (!string.IsNullOrEmpty(subline))
        {
            var subl = new Label { Text = subline, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            subl.AddThemeFontOverride("font", GetBodyFont((int)(26 * s)));
            subl.AddThemeFontSizeOverride("font_size", (int)(26 * s));
            subl.AddThemeColorOverride("font_color", new Color(0.86f, 0.78f, 0.56f));
            col.AddChild(subl);
        }
        // ArmEndOfDuelButton rewrites Text when the press lands; that is invisible
        // here, so it mirrors the new text onto Col/Face. See MirrorButtonFace.
        MenuButtons.Animate(btn);
        return btn;
    }

    private Label OverlayText(string text, float y, int size, Font font, Color color, float width)
    {
        var l = new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(0, y), Size = new Vector2(width, size * 1.4f),
        };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        _gameOverOverlay!.AddChild(l);
        return l;
    }

    /// <summary>Each tile rises a little into place. Bound to the tile (FABLE-029).</summary>
    private static void RiseIn(Control tile, float delay, float s)
    {
        if (CampaignContext.ReduceMotion) return;
        var rest = tile.Position;
        tile.Position = rest + new Vector2(0, 28f * s);
        var t = tile.CreateTween();
        t.TweenProperty(tile, "position", rest, 0.5f).SetDelay(delay).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    /// <summary>A plate button draws its text on a child label; keep it in step with Button.Text.</summary>
    private static void MirrorButtonFace(Button btn)
    {
        if (btn.GetNodeOrNull<Label>("Col/Face") is { } face) face.Text = btn.Text;
    }

    private static Gradient TwoStop(Color a, Color b)
    {
        var g = new Gradient();
        g.SetColor(0, a);
        g.SetColor(1, b);
        return g;
    }
}
