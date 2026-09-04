namespace Meshwright.Rendering.Gizmos;

/// <summary>
/// Which pointer button a <see cref="GizmoPointerEvent"/> originated from. A neutral enum (rather
/// than Avalonia's <c>PointerPointProperties</c>) so <c>Meshwright.Rendering</c> stays free of a
/// UI-framework dependency per AGENTS.md §6.3 — <c>MeshViewportControl</c> maps Avalonia's button
/// state onto this at the call site.
/// </summary>
public enum GizmoPointerButton
{
    /// <summary>No button is relevant to this event (always the case for move events).</summary>
    None,
    Primary,
    Secondary,
    Middle,
}
