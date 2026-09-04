using Godot;
using System;

namespace Runewake.Client;

/// <summary>
/// WORLD-POLISH-1: 56px round stone/bronze medallion on the campaign map.
/// States: available=lit bronze, cleared=gold ring+check, locked=dark.
/// Vector-drawn icons (no font glyphs — the tofu-box era is over).
/// </summary>
public partial class MapNodeIcon : Button
{
    private Label _nameLabel;
    private PanelContainer _medPanel;
    private Control _checkMark;
    private Control _selectedRing;
    private Control _iconContainer;
    
    // Lock padlock (ColorRect assembly)
    private Control _lockGroup;
    
    /// <summary>Node ID from the map region JSON.</summary>
    public string NodeId { get; private set; } = string.Empty;
    public string NodeName { get; private set; } = string.Empty;
    public bool IsLocked { get; private set; } = true;
    private bool _isCleared;
    private bool _isSelected;
    private Tween? _pulseTween;

    [Signal]
    public delegate void NodeSelectedEventHandler(string nodeId);

    // Node type configuration
    private string _nodeType = "Duel";

    public override void _Ready()
    {
        // Minimal container — 56px medallion + auto-fit name chip below
        CustomMinimumSize = new Vector2(80, 80);
        Size = new Vector2(80, 80);
        MouseFilter = MouseFilterEnum.Pass;
        FocusMode = FocusModeEnum.None;

        // Name label — Cinzel with dark outline so it reads over the painted map
        _nameLabel = new Label
        {
            Name = "NameLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Off
        };
        ThemeTokens.ApplyHeaderFont(_nameLabel, 10);
        _nameLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.9f, 0.76f, 1));
        _nameLabel.AddThemeColorOverride("font_outline_color", new Color(0.06f, 0.05f, 0.03f, 0.9f));
        _nameLabel.AddThemeConstantOverride("outline_size", 6);
        _nameLabel.Position = new Vector2(-30, 60);
        _nameLabel.Size = new Vector2(140, 18);
        AddChild(_nameLabel);

        // Medallion background (56px round) via StyleBoxFlat on a container
        var medPanel = new PanelContainer();
        medPanel.Position = new Vector2(12, 4);
        medPanel.Size = new Vector2(56, 56);
        medPanel.MouseFilter = MouseFilterEnum.Ignore;
        var medStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.35f, 0.28f, 0.15f, 1),
            BorderColor = new Color(0.55f, 0.45f, 0.25f, 1),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 28, CornerRadiusTopRight = 28,
            CornerRadiusBottomLeft = 28, CornerRadiusBottomRight = 28
        };
        medPanel.AddThemeStyleboxOverride("panel", medStyle);
        AddChild(medPanel);
        _medPanel = medPanel;

        // Icon container — for vector-drawn shapes
        _iconContainer = new Control();
        _iconContainer.Position = new Vector2(12, 4);
        _iconContainer.Size = new Vector2(56, 56);
        _iconContainer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_iconContainer);

        // Selection ring (hidden until selected)
        _selectedRing = new ColorRect
        {
            Name = "SelectedRing",
            Color = Colors.Transparent,
            Position = new Vector2(8, 0),
            Size = new Vector2(64, 64),
            MouseFilter = MouseFilterEnum.Ignore
        };
        var ringStyle = new StyleBoxFlat
        {
            BgColor = Colors.Transparent,
            BorderColor = new Color(1f, 0.85f, 0.2f, 0.7f),
            BorderWidthLeft = 3, BorderWidthTop = 3,
            BorderWidthRight = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 32, CornerRadiusTopRight = 32,
            CornerRadiusBottomLeft = 32, CornerRadiusBottomRight = 32
        };
        var ringPanel = new PanelContainer();
        ringPanel.Position = new Vector2(8, 0);
        ringPanel.Size = new Vector2(64, 64);
        ringPanel.MouseFilter = MouseFilterEnum.Ignore;
        ringPanel.AddThemeStyleboxOverride("panel", ringStyle);
        ringPanel.Visible = false;
        AddChild(ringPanel);
        // Store reference for visibility
        _selectedRing = ringPanel;
        
        // Check mark (cleared state)
        _checkMark = new ColorRect
        {
            Name = "CheckMark",
            Color = Colors.Transparent,
            Position = new Vector2(32, 24),
            Size = new Vector2(16, 16),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        var checkStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.3f, 0.7f, 0.3f, 0.9f),
            BorderColor = new Color(0.1f, 0.5f, 0.1f, 1),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
        };
        var checkPanel = new PanelContainer();
        checkPanel.Position = new Vector2(32, 24);
        checkPanel.Size = new Vector2(16, 16);
        checkPanel.MouseFilter = MouseFilterEnum.Ignore;
        checkPanel.AddThemeStyleboxOverride("panel", checkStyle);
        checkPanel.Visible = false;
        AddChild(checkPanel);
        _checkMark = checkPanel;

        // Lock group
        _lockGroup = new Control { Name = "LockGroup" };
        _lockGroup.Position = new Vector2(12, 4);
        AddChild(_lockGroup);
        BuildLockIcon();
        _lockGroup.Hide();

        // Click detection
        GuiInput += OnGuiInput;
    }

    private void BuildLockIcon()
    {
        Color lc = new Color(0.82f, 0.78f, 0.68f, 0.95f);
        // Shackle — open arch: two posts + a top bar (no solid fill)
        var shTop = new ColorRect { Color = lc, Position = new Vector2(23, 19), Size = new Vector2(10, 3), MouseFilter = MouseFilterEnum.Ignore };
        _lockGroup.AddChild(shTop);
        var shL = new ColorRect { Color = lc, Position = new Vector2(23, 19), Size = new Vector2(3, 11), MouseFilter = MouseFilterEnum.Ignore };
        _lockGroup.AddChild(shL);
        var shR = new ColorRect { Color = lc, Position = new Vector2(30, 19), Size = new Vector2(3, 11), MouseFilter = MouseFilterEnum.Ignore };
        _lockGroup.AddChild(shR);
        // Body — slightly wider than the shackle, rounded
        var body = new PanelContainer { Position = new Vector2(19, 30), Size = new Vector2(18, 14), MouseFilter = MouseFilterEnum.Ignore };
        body.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = lc,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3
        });
        _lockGroup.AddChild(body);
        // Keyhole
        var kh = new ColorRect { Color = new Color(0.22f, 0.18f, 0.12f, 1), Position = new Vector2(26.5f, 34), Size = new Vector2(3, 6), MouseFilter = MouseFilterEnum.Ignore };
        _lockGroup.AddChild(kh);
    }

    private void OnGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            EmitSignal(SignalName.NodeSelected, NodeId);
            GetViewport().SetInputAsHandled();
        }
    }

    public void Setup(string nodeId, string displayName, string nodeType, bool locked)
    {
        NodeId = nodeId;
        NodeName = displayName;
        IsLocked = locked;
        _nodeType = nodeType;
        _nameLabel.Text = TruncateName(displayName);
        DrawNodeIcon(nodeType);
        ApplyLockState(locked);
    }

    private void DrawNodeIcon(string nodeType)
    {
        // Clear previous icon
        foreach (var c in _iconContainer.GetChildren())
            c.QueueFree();

        float cx = 28f;
        float cy = 28f;
        var iconColor = new Color(0.9f, 0.85f, 0.7f, 0.9f); // gold icon on dark bronze
        var subColor = new Color(0.95f, 0.7f, 0.3f, 0.8f);

        switch (nodeType)
        {
            case "Duel": // Crossed swords
                DrawLine(cx - 7, cy - 10, cx + 7, cy + 10, iconColor, 2.5f); // blade 1
                DrawLine(cx + 7, cy - 10, cx - 7, cy + 10, iconColor, 2.5f); // blade 2
                // hilts
                DrawLine(cx - 7, cy + 10, cx - 5, cy + 13, iconColor, 2f);
                DrawLine(cx + 7, cy + 10, cx + 5, cy + 13, iconColor, 2f);
                // crossguards
                DrawLine(cx - 11, cy - 3, cx - 3, cy - 3, iconColor, 1.5f);
                DrawLine(cx + 11, cy - 3, cx + 3, cy - 3, iconColor, 1.5f);
                break;

            case "Elite": // Star burst
                for (int i = 0; i < 4; i++)
                {
                    float a1 = i * Mathf.Pi / 2f;
                    float a2 = a1 + Mathf.Pi / 4f;
                    DrawLine(cx + 10 * Mathf.Cos(a1), cy + 10 * Mathf.Sin(a1),
                             cx + 4 * Mathf.Cos(a2), cy + 4 * Mathf.Sin(a2), iconColor, 2f);
                }
                break;

            case "Warden":
            case "WardenBoss": // Crown
                DrawLine(cx - 10, cy + 10, cx - 10, cy + 3, iconColor, 2f);
                DrawLine(cx - 10, cy + 3, cx - 6, cy - 5, iconColor, 2f);
                DrawLine(cx - 6, cy - 5, cx, cy - 10, iconColor, 2f);
                DrawLine(cx, cy - 10, cx + 6, cy - 5, iconColor, 2f);
                DrawLine(cx + 6, cy - 5, cx + 10, cy + 3, iconColor, 2f);
                DrawLine(cx + 10, cy + 3, cx + 10, cy + 10, iconColor, 2f);
                // Base
                DrawLine(cx - 10, cy + 10, cx + 10, cy + 10, iconColor, 2f);
                if (nodeType == "WardenBoss")
                {
                    // Inner skull hint
                    DrawCircle(cx, cy - 2, 5, subColor);
                }
                break;

            case "Dig": // Pick
                DrawLine(cx - 12, cy + 10, cx, cy - 8, iconColor, 2.5f);
                DrawLine(cx, cy - 8, cx + 10, cy + 10, iconColor, 2.5f);
                DrawLine(cx - 12, cy + 10, cx - 8, cy + 12, iconColor, 2f);
                DrawLine(cx + 10, cy + 10, cx + 6, cy + 12, iconColor, 2f);
                // Handle
                DrawLine(cx, cy - 8, cx, cy - 12, new Color(0.5f, 0.4f, 0.25f, 0.9f), 2.5f);
                break;

            case "Shrine": // Flame
                DrawLine(cx, cy - 6, cx - 3, cy + 2, subColor, 2f); // left flame
                DrawLine(cx - 3, cy + 2, cx - 6, cy + 6, subColor, 1.5f);
                DrawLine(cx, cy - 6, cx + 3, cy + 2, subColor, 2f); // right flame
                DrawLine(cx + 3, cy + 2, cx + 6, cy + 6, subColor, 1.5f);
                DrawLine(cx - 1, cy - 3, cx + 1, cy - 3, subColor, 1.5f); // flame tip
                // Base altar stone
                DrawLine(cx - 7, cy + 8, cx + 7, cy + 8, new Color(0.5f, 0.4f, 0.3f, 0.8f), 2f);
                break;

            case "Cache": // Question mark curve
                // Simplified ? curve
                DrawCircle(cx - 2, cy - 3, 3, iconColor); // top circle
                DrawArc(new Vector2(cx - 2, cy + 3), 4, 0.5f, 1f, iconColor, 1.5f);
                DrawLine(cx - 2, cy + 6, cx - 2, cy + 9, iconColor, 1.5f);
                break;

            case "Merchant": // Coin
                DrawCircle(cx, cy, 10, iconColor);
                DrawLine(cx - 5, cy, cx + 5, cy, subColor, 2f);
                break;

            default: // Generic diamond
                DrawLine(cx, cy - 10, cx + 10, cy, iconColor, 2.5f);
                DrawLine(cx + 10, cy, cx, cy + 10, iconColor, 2.5f);
                DrawLine(cx, cy + 10, cx - 10, cy, iconColor, 2.5f);
                DrawLine(cx - 10, cy, cx, cy - 10, iconColor, 2.5f);
                break;
        }
    }

    private void DrawLine(float x1, float y1, float x2, float y2, Color color, float width)
    {
        // Use ColorRect rotated — Godot has no built-in CanvasItem line in Control
        // We use a thin ColorRect with rotation
        float dx = x2 - x1;
        float dy = y2 - y1;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        if (len < 1f) return;
        var rect = new ColorRect
        {
            Color = color,
            Position = new Vector2(x1, y1 - width / 2f),
            Size = new Vector2(len, width),
            MouseFilter = MouseFilterEnum.Ignore,
            PivotOffset = new Vector2(0, width / 2f)
        };
        rect.Rotation = Mathf.Atan2(dy, dx);
        _iconContainer.AddChild(rect);
    }

    private void DrawCircle(float cx, float cy, float radius, Color color)
    {
        // Approximate as a ColorRect with rounded style
        var circle = new PanelContainer();
        circle.Position = new Vector2(cx - radius, cy - radius);
        circle.Size = new Vector2(radius * 2, radius * 2);
        circle.MouseFilter = MouseFilterEnum.Ignore;
        var style = new StyleBoxFlat
        {
            BgColor = color,
            BorderColor = color,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = Mathf.RoundToInt(radius),
            CornerRadiusTopRight = Mathf.RoundToInt(radius),
            CornerRadiusBottomLeft = Mathf.RoundToInt(radius),
            CornerRadiusBottomRight = Mathf.RoundToInt(radius)
        };
        circle.AddThemeStyleboxOverride("panel", style);
        _iconContainer.AddChild(circle);
    }

    private void DrawArc(Vector2 center, float radius, float startAngle, float endAngle, Color color, float width)
    {
        // Simple arc via thin line segments
        int steps = 8;
        float aStep = (endAngle - startAngle) / steps;
        float x1 = center.X + radius * Mathf.Cos(startAngle);
        float y1 = center.Y + radius * Mathf.Sin(startAngle);
        for (int i = 1; i <= steps; i++)
        {
            float a = startAngle + i * aStep;
            float x2 = center.X + radius * Mathf.Cos(a);
            float y2 = center.Y + radius * Mathf.Sin(a);
            DrawLine(x1, y1, x2, y2, color, width);
            x1 = x2; y1 = y2;
        }
    }

    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        ApplyLockState(locked);
    }

    public void SetCleared()
    {
        _isCleared = true;
        IsLocked = false;
        _isSelected = false;
        _checkMark.Visible = true;
        _lockGroup.Hide();
        _selectedRing.Visible = false;
        _nameLabel.Modulate = new Color(0.75f, 0.75f, 0.7f, 0.9f);
        // Dim the medallion slightly
        UpdateMedallionAppearance();
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        if (_isCleared) return;

        // Kill any existing pulse tween
        if (_pulseTween != null && _pulseTween.IsValid())
        {
            _pulseTween.Kill();
            _pulseTween = null;
        }

        _selectedRing.Visible = selected;

        if (selected)
        {
            UpdateMedallionAppearance();

            // Pulse animation: border color oscillates between gold and bright gold
            _pulseTween = CreateTween().SetLoops();
            _pulseTween.TweenProperty(
                _selectedRing,
                "modulate",
                new Color(1f, 0.95f, 0.6f, 1f),
                0.8f);
            _pulseTween.TweenProperty(
                _selectedRing,
                "modulate",
                new Color(1f, 0.75f, 0.15f, 0.5f),
                0.8f);
        }
    }

    private void ApplyLockState(bool locked)
    {
        if (locked)
        {
            _selectedRing.Visible = false;
            _checkMark.Visible = false;
            _lockGroup.Show();
        }
        else
        {
            _lockGroup.Hide();
        }
        UpdateMedallionAppearance();
    }

    private void UpdateMedallionAppearance()
    {
        var medPanel = _medPanel;
        if (medPanel == null) return;
        var s = new StyleBoxFlat
        {
            CornerRadiusTopLeft = 28, CornerRadiusTopRight = 28,
            CornerRadiusBottomLeft = 28, CornerRadiusBottomRight = 28,
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            ShadowColor = new Color(0f, 0f, 0f, 0.45f),
            ShadowSize = 5,
            ShadowOffset = new Vector2(0, 2)
        };

        if (_isCleared)
        {
            // Green-tinted muted gold — distinct from available bronze
            s.BgColor = new Color(0.3f, 0.35f, 0.22f, 0.85f);
            s.BorderColor = new Color(0.55f, 0.7f, 0.35f, 1);
        }
        else if (IsLocked && !_isCleared)
        {
            // Very dark stone — almost invisible on painted terrain
            s.BgColor = new Color(0.15f, 0.12f, 0.08f, 0.8f);
            s.BorderColor = new Color(0.25f, 0.2f, 0.12f, 0.7f);
        }
        else // available — warm bronze with bright gold border
        {
            s.BgColor = new Color(0.5f, 0.38f, 0.18f, 1);
            s.BorderColor = new Color(0.85f, 0.7f, 0.3f, 1);
        }
        medPanel.AddThemeStyleboxOverride("panel", s);
    }

    private static string TruncateName(string name, int maxLen = 16)
    {
        if (name.Length <= maxLen) return name;
        return name[..(maxLen - 1)] + "\u2026";
    }
}