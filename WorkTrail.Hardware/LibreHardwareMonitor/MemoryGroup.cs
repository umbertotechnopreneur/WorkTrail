// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.
// Modified for WorkTrail: expose OS memory counters without SPD/SMBus discovery.

using System.Collections.Generic;

namespace LibreHardwareMonitor.Hardware.Memory;

/// <summary>
/// Exposes operating-system memory counters while deliberately disabling SPD and SMBus discovery.
/// </summary>
internal sealed class MemoryGroup : IGroup
{
    private readonly IReadOnlyList<Hardware> _hardware;

    /// <summary>
    /// Creates the virtual and total-memory sensors that do not require physical-memory probing.
    /// </summary>
    public MemoryGroup(ISettings settings)
    {
        _hardware = new Hardware[] { new VirtualMemory(settings), new TotalMemory(settings) };
    }

    /// <summary>
    /// Gets the supported operating-system memory sensors.
    /// </summary>
    public IReadOnlyList<IHardware> Hardware => _hardware;

    /// <summary>
    /// Returns the product-specific description of the restricted memory telemetry scope.
    /// </summary>
    public string GetReport() => "Memory: operating-system usage counters; SPD/SMBus discovery disabled.";

    /// <summary>
    /// Closes each memory sensor held by this group.
    /// </summary>
    public void Close()
    {
        foreach (var hardware in _hardware)
        {
            hardware.Close();
        }
    }
}
