namespace PoMemeVideo.Shared.Enums;

// Single canonical definition — accessible by Domain, Application, and Client
// without Domain coupling (VisualEffectType is used in ScriptEntry and ScriptEntryDto)
public enum VisualEffectType
{
    None,
    DeepFry,

    // Retired. SnapZoom cropped to half size and scaled back up, but the filter was added to the
    // base chain instead of the cue's time window, so it cropped the ENTIRE video. Nothing
    // produces or renders it now. The member itself must stay: entries are persisted as JSON via
    // JsonStringEnumConverter, so every already-stored script contains "SnapZoom" and removing the
    // member would throw JsonException when loading those sessions.
    SnapZoom,
    MotionBlur,
    Overlay
}
