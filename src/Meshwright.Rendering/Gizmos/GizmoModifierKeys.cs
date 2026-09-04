using System;

namespace Meshwright.Rendering.Gizmos;

/// <summary>
/// Keyboard modifiers active during a <see cref="GizmoPointerEvent"/>, e.g. for axis-snapping or
/// precision-drag behavior. A neutral flags enum (rather than Avalonia's <c>KeyModifiers</c>) so
/// <c>Meshwright.Rendering</c> stays free of a UI-framework dependency per AGENTS.md §6.3.
/// </summary>
[Flags]
public enum GizmoModifierKeys
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
}
