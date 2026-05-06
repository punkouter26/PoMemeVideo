namespace PoMemeVideo.Shared.Enums;

// Single canonical definition — accessible by Domain, Application, and Client
// without Domain coupling (VisualEffectType is used in ScriptEntry and ScriptEntryDto)
public enum VisualEffectType
{
    None,
    DeepFry,
    SnapZoom,
    MotionBlur,
    Overlay
}
