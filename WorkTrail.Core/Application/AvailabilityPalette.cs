// SPDX-License-Identifier: MIT

namespace WorkTrail.Application;

/// <summary>Supplies matching availability fills for the planner and PDF.</summary>
public static class AvailabilityPalette
{
    /// <summary>Gets the RGB fill for an availability state and palette.</summary>
    public static (byte Red, byte Green, byte Blue) GetFill(AvailabilityState state, int palette = 0) => (palette, state) switch
    {
        (1, AvailabilityState.Available) => (239, 248, 242),
        (1, AvailabilityState.Reserve) => (255, 249, 231),
        (1, AvailabilityState.Unavailable) => (252, 239, 242),
        (2, AvailabilityState.Available) => (189, 224, 201),
        (2, AvailabilityState.Reserve) => (247, 223, 172),
        (2, AvailabilityState.Unavailable) => (233, 194, 203),
        (3, AvailabilityState.Available) => (213, 232, 247),
        (3, AvailabilityState.Reserve) => (236, 226, 247),
        (3, AvailabilityState.Unavailable) => (246, 221, 211),
        (4, AvailabilityState.Available) => (219, 237, 228),
        (4, AvailabilityState.Reserve) => (241, 230, 207),
        (4, AvailabilityState.Unavailable) => (229, 220, 228),
        (_, AvailabilityState.Available) => (229, 242, 233),
        (_, AvailabilityState.Reserve) => (250, 239, 215),
        (_, AvailabilityState.Unavailable) => (245, 229, 234),
        _ => (239, 241, 244)
    };
}
