using Godot;

namespace Runewake.Client;

/// <summary>
/// PAINTED-PLATE-1: The battlefield environment is now a single painted image
/// (plate_default.png) that fills the entire board area. AltarField is retained
/// as an invisible geometry container so the slot-arc layout code (PopulateLanes)
/// and the _altarContainer sibling node continue to work. Nothing is drawn here.
/// The oval ring, its fill, border, dashed ring, glow, and shadow were all
/// procedural and have been removed — they are now part of the painted plate.
/// </summary>
public partial class AltarField : Control
{
    public override void _Draw()
    {
        // Intentionally empty — the battlefield is the painted plate behind this control.
        // All geometry (ring position, size) is in ThemeTokens canonical constants.
    }
}