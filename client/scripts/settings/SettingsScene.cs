using Godot;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Settings screen — code-driven Godot UI.
/// Controls for volume, accessibility, and language display.
/// </summary>
public partial class SettingsScene : Control
{
    private HSlider? _masterSlider;
    private HSlider? _musicSlider;
    private HSlider? _sfxSlider;
    private CheckButton? _reduceMotionToggle;
    private CheckButton? _largeTextToggle;
    private CheckButton? _highContrastToggle;
    private Label? _languageLabel;

    public override void _Ready()
    {
        BuildUI();
        LoadCurrentSettings();
    }

    private void BuildUI()
    {
        // Dark background
        var bg = new ColorRect
        {
            Color = new Color(0.08f, 0.08f, 0.12f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f
        };
        AddChild(bg);

        // Title
        var title = new Label
        {
            Text = "Settings",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.2f, AnchorRight = 0.8f,
            AnchorTop = 0.02f, AnchorBottom = 0.1f
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        AddChild(title);

        // Content vbox
        var vbox = new VBoxContainer
        {
            AnchorLeft = 0.1f, AnchorRight = 0.9f,
            AnchorTop = 0.12f, AnchorBottom = 0.85f
        };
        AddChild(vbox);

        // ——— Volume section ———
        var volHeader = new Label { Text = "Volume" };
        volHeader.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(volHeader);

        AddSlider(vbox, "Master Volume", out _masterSlider, OnSliderChanged);
        AddSlider(vbox, "Music Volume", out _musicSlider, OnSliderChanged);
        AddSlider(vbox, "SFX Volume", out _sfxSlider, OnSliderChanged);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        // ——— Accessibility section ———
        var accHeader = new Label { Text = "Accessibility" };
        accHeader.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(accHeader);

        _reduceMotionToggle = AddToggle(vbox, "Reduce Motion");
        _largeTextToggle = AddToggle(vbox, "Large Text");
        _highContrastToggle = AddToggle(vbox, "High Contrast");

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        // Language (read-only)
        _languageLabel = new Label
        {
            Text = "Language: English",
            Modulate = new Color(0.6f, 0.6f, 0.7f)
        };
        vbox.AddChild(_languageLabel);

        // ——— Buttons ———
        var btnHbox = new HBoxContainer
        {
            AnchorLeft = 0.1f, AnchorRight = 0.9f,
            AnchorTop = 0.88f, AnchorBottom = 0.96f
        };
        AddChild(btnHbox);

        var saveBtn = new Button { Text = "Save" };
        saveBtn.Pressed += OnSavePressed;
        btnHbox.AddChild(saveBtn);

        btnHbox.AddChild(new Control { CustomMinimumSize = new Vector2(20, 0) });

        var backBtn = new Button { Text = "Back" };
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
        btnHbox.AddChild(backBtn);
    }

    private static void AddSlider(VBoxContainer parent, string label, out HSlider slider, Action<double> handler)
    {
        var hbox = new HBoxContainer();
        var lbl = new Label { Text = label, CustomMinimumSize = new Vector2(140, 0) };
        hbox.AddChild(lbl);

        slider = new HSlider
        {
            MinValue = 0, MaxValue = 1.0, Step = 0.05f,
            SizeFlagsHorizontal = (Control.SizeFlags)3
        };
        slider.ValueChanged += v => handler(v);
        hbox.AddChild(slider);

        var valLabel = new Label { Text = "1.00", CustomMinimumSize = new Vector2(40, 0) };
        slider.ValueChanged += v => valLabel.Text = v.ToString("F2");
        hbox.AddChild(valLabel);

        parent.AddChild(hbox);
    }

    private static CheckButton AddToggle(VBoxContainer parent, string label)
    {
        var hbox = new HBoxContainer();
        var lbl = new Label { Text = label, CustomMinimumSize = new Vector2(160, 0) };
        hbox.AddChild(lbl);
        var toggle = new CheckButton();
        hbox.AddChild(toggle);
        parent.AddChild(hbox);
        return toggle;
    }

    private void LoadCurrentSettings()
    {
        var s = CampaignContext.Settings;
        if (_masterSlider != null) _masterSlider.Value = s.MasterVolume;
        if (_musicSlider != null) _musicSlider.Value = s.MusicVolume;
        if (_sfxSlider != null) _sfxSlider.Value = s.SfxVolume;
        if (_reduceMotionToggle != null) _reduceMotionToggle.ButtonPressed = s.ReduceMotion;
        if (_largeTextToggle != null) _largeTextToggle.ButtonPressed = s.LargeText;
        if (_highContrastToggle != null) _highContrastToggle.ButtonPressed = s.HighContrast;
    }

    private void OnSliderChanged(double value)
    {
        // Preview is live (values update instantly, applied on Save)
    }

    private void OnSavePressed()
    {
        var s = CampaignContext.Settings;

        if (_masterSlider != null) s.MasterVolume = (float)_masterSlider.Value;
        if (_musicSlider != null) s.MusicVolume = (float)_musicSlider.Value;
        if (_sfxSlider != null) s.SfxVolume = (float)_sfxSlider.Value;
        if (_reduceMotionToggle != null) s.ReduceMotion = _reduceMotionToggle.ButtonPressed;
        if (_largeTextToggle != null) s.LargeText = _largeTextToggle.ButtonPressed;
        if (_highContrastToggle != null) s.HighContrast = _highContrastToggle.ButtonPressed;

        CampaignContext.SaveManager!.SaveSettings(s);
        ApplyAudioSettings(s);

        GD.Print("[Settings] Saved and applied.");
    }

    private static void ApplyAudioSettings(SettingsState s)
    {
        // Apply volume levels to Godot audio buses
        int masterIdx = AudioServer.GetBusIndex("Master");
        if (masterIdx >= 0)
            AudioServer.SetBusVolumeDb(masterIdx, Mathf.LinearToDb(s.MasterVolume));

        int musicIdx = AudioServer.GetBusIndex("Music");
        if (musicIdx >= 0)
            AudioServer.SetBusVolumeDb(musicIdx, Mathf.LinearToDb(s.MusicVolume));

        int sfxIdx = AudioServer.GetBusIndex("SFX");
        if (sfxIdx >= 0)
            AudioServer.SetBusVolumeDb(sfxIdx, Mathf.LinearToDb(s.SfxVolume));
    }
}