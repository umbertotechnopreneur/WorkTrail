// SPDX-License-Identifier: MIT
/* VBWR B
 *
 * Project: WorkTrail
 * Repository: https://github.com/umbertotechnopreneur/WorkTrail
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 *
 * VBWR E */


using System.Globalization;
using WorkTrail.Services;

namespace WorkTrail.Presentation;

/// <summary>Contains one device's display values and its unmodified primary sensor reading.</summary>
public sealed record SensorMonitorRow(string Id, string Category, string Name, string Value, string Details,
    string Temperature, string Source, DateTimeOffset SampledAt, string? SensorId, double? Percent)
{
    /// <summary>Contains a temperature only when the device supplies a current reading.</summary>
    public string TemperatureValue { get; init; } = string.Empty;
    /// <summary>Indicates whether a temperature line can be rendered without an unavailable placeholder.</summary>
    public bool HasTemperature => TemperatureValue.Length > 0;
    /// <summary>Contains the compact power, clock, transfer or battery information shown next to the primary reading.</summary>
    public string SecondaryValue { get; init; } = string.Empty;
    /// <summary>Contains free/total disk space or used/total memory capacity, with localized labels and units.</summary>
    public string CapacityText { get; init; } = string.Empty;
}

/// <summary>Selects representative readings without summing overlapping package, core or engine sensors.</summary>
public static class SensorMonitorProjection
{
    /// <summary>Projects each present device independently; an absent battery produces no placeholder.</summary>
    public static IReadOnlyList<SensorMonitorRow> Create(SystemSnapshot snapshot, CultureInfo culture, Func<string, string> translate)
    {
        SystemSnapshotValidator.Validate(snapshot);
        var missing = translate("Common.NotAvailable");
        var hasPhysicalMemory = snapshot.Devices.Any(device => device.Kind == "Memory" && !IsVirtualMemory(device));
        return snapshot.Devices.Where(device => Category(device.Kind) is not null)
            .Where(device => !hasPhysicalMemory || !IsVirtualMemory(device))
            .OrderBy(device => Order(Category(device.Kind)!)).ThenBy(device => device.Id, StringComparer.Ordinal)
            .Select(device =>
            {
                var category = Category(device.Kind)!;
                var usable = snapshot.Status is "ready" or "partial";
                var readings = device.Sensors.Where(sensor => usable && sensor.Value.HasValue).ToArray();
                var primary = category switch
                {
                    "Cpu" => Pick(readings, "Load", "CPU Total"),
                    "Gpu" => HardwareUsageProjection.SelectGpuUtilization(readings),
                    "Memory" => Pick(readings, "Load", "Memory"),
                    "Storage" => Pick(readings, "Load", "Total Activity"),
                    "Battery" => Pick(readings, "Level", "Charge Level"),
                    _ => null
                };
                // CPU totals and the driver's GPU core/busiest D3D engine drive utilization; engines are never added.
                // NVMe warning/critical values are thresholds, not device temperatures, even though upstream
                // classifies them as Temperature sensors. Never present those limits as live measurements.
                var temperatures = readings.Where(sensor => sensor.Kind == "Temperature"
                    && sensor.Name is not ("Warning Temperature" or "Critical Temperature")).ToArray();
                var temperature = Pick(temperatures, "Temperature", "CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "GPU Core", "Composite Temperature", "Composite", "Temperature")
                    ?? temperatures.OrderByDescending(sensor => sensor.Value).ThenBy(sensor => sensor.Id, StringComparer.Ordinal).FirstOrDefault();
                var power = Pick(readings, "Power", "CPU Package", "GPU Package", "GPU Power", "GPU Core", "Package", "Charge Rate", "Discharge Rate", "Charge/Discharge Rate");
                var clock = Pick(readings, "Clock", "GPU Core", "CPU Core #1", "Core #1");
                var capacity = Capacity(category, readings, culture, translate);
                var extra = new List<string>();
                if (power is not null) extra.Add($"{translate(power.Name == "Charge Rate" ? "Sensors.Charging" : power.Name == "Discharge Rate" ? "Sensors.Discharging" : "Sensors.Power")}: {Format(power, culture)}");
                if (clock is not null) extra.Add($"{translate("Sensors.Clock")}: {Format(clock, culture)}");
                var detailReadings = readings.Where(sensor => sensor.Kind is "Data" or "SmallData" or "Throughput" or "TimeSpan"
                    || category == "Storage" && sensor.Kind == "Load" && sensor.Name == "Used Space")
                    .OrderBy(sensor => sensor.Id, StringComparer.Ordinal).Take(6).ToArray();
                foreach (var sensor in detailReadings)
                    extra.Add($"{SensorLabel(sensor.Name, translate)}: {Format(sensor, culture)}");
                var source = string.Join(" · ", new[] { primary?.Name, temperature?.Name, power?.Name, clock?.Name }
                    .Where(name => name is not null).Concat(detailReadings.Select(sensor => sensor.Name)).Distinct(StringComparer.Ordinal));
                // The D3D series represents the busiest engine; changing that engine must not erase its history.
                var seriesId = category == "Gpu" && primary?.Name.StartsWith("D3D ", StringComparison.Ordinal) == true
                    ? device.Id + "/monitor/d3d-busiest" : primary?.Id;
                return new SensorMonitorRow(device.Id, category, device.Name,
                    primary is null ? missing : Format(primary, culture), string.Join(" · ", extra),
                    temperature is null ? string.Empty : $"{translate("Sensors.Temperature")}: {Format(temperature, culture)}",
                    source, device.SampledAt, seriesId,
                    primary is { Unit: "%", Value: >= 0 and <= 100 } ? primary.Value : null)
                {
                    TemperatureValue = temperature is null ? string.Empty : Format(temperature, culture),
                    SecondaryValue = Secondary(category, readings, power, clock, culture, translate),
                    CapacityText = capacity
                };
            }).ToArray();
    }

    private static HardwareSensorSnapshot? Pick(IEnumerable<HardwareSensorSnapshot> readings, string kind, params string[] names) =>
        names.Select(name => readings.FirstOrDefault(sensor => sensor.Kind == kind && sensor.Name == name)).FirstOrDefault(sensor => sensor is not null);

    private static bool IsVirtualMemory(HardwareDeviceSnapshot device) =>
        device.Kind == "Memory" && (device.Id == "/vram" || device.Name == "Virtual Memory");

    private static string Secondary(string category, HardwareSensorSnapshot[] readings, HardwareSensorSnapshot? power,
        HardwareSensorSnapshot? clock, CultureInfo culture, Func<string, string> translate)
    {
        var values = new List<string>();
        if (power is not null)
        {
            var state = power.Name switch { "Charge Rate" => "Sensors.Charging", "Discharge Rate" => "Sensors.Discharging", _ => null };
            values.Add(state is null ? Format(power, culture) : $"{translate(state)} {Format(power, culture)}");
        }
        if (clock is not null) values.Add(Format(clock, culture));
        if (category == "Battery" && Pick(readings, "TimeSpan", "Remaining Time (Estimated)") is { } remaining)
            values.Add(Format(remaining, culture));
        if (category == "Storage")
        {
            foreach (var name in new[] { "Read Rate", "Write Rate" })
                if (Pick(readings, "Throughput", name) is { } rate)
                    values.Add($"{SensorLabel(name, translate)} {Format(rate, culture)}");
        }
        return string.Join(category == "Storage" ? "\n" : " · ", values);
    }

    private static string Capacity(string category, HardwareSensorSnapshot[] readings,
        CultureInfo culture, Func<string, string> translate)
    {
        HardwareSensorSnapshot? used = null;
        HardwareSensorSnapshot? total = null;
        if (category == "Memory")
        {
            used = Pick(readings, "Data", "Memory Used");
            var available = Pick(readings, "Data", "Memory Available");
            if (used?.Unit == available?.Unit && used?.Value is >= 0 && available?.Value is >= 0)
                total = used with { Value = used.Value + available.Value };
        }
        else if (category == "Gpu")
        {
            // Shared memory is the relevant capacity for integrated GPUs; don't combine it with dedicated VRAM.
            foreach (var prefix in new[] { "GPU Memory", "D3D Dedicated Memory", "D3D Shared Memory" })
            {
                used = Pick(readings, "SmallData", prefix + " Used");
                total = Pick(readings, "SmallData", prefix + " Total");
                if (used?.Value is >= 0 && total?.Value is > 0) break;
            }
        }
        else if (category == "Storage")
        {
            total = Pick(readings, "Data", "Total Space");
            var free = Pick(readings, "Data", "Free Space");
            var missing = translate("Common.NotAvailable");
            var totalText = total?.Value is > 0 ? Format(total, culture) : missing;
            var freeText = free?.Value is >= 0 && (total is null || free.Unit == total.Unit && free.Value <= total.Value)
                ? Format(free, culture) : missing;
            // Disk space is a text label; missing capacity must not become zero or an activity percentage.
            return string.Format(culture, translate("Sensors.SpaceSummary"), freeText, totalText);
        }
        if (used?.Unit == total?.Unit && used?.Value is >= 0 && total?.Value is > 0 && used.Value <= total.Value)
        {
            var divisor = total.Unit == "MiB" && total.Value >= 1024 ? 1024 : 1;
            var unit = divisor == 1024 ? "GiB" : total.Unit;
            return $"{(used.Value.Value / divisor).ToString("0", culture)} / {(total.Value.Value / divisor).ToString("0", culture)} {unit}";
        }
        return string.Empty;
    }

    private static string Format(HardwareSensorSnapshot sensor, CultureInfo culture)
    {
        var value = sensor.Value!.Value;
        var (number, unit) = sensor.Unit switch
        {
            "B/s" when Math.Abs(value) >= 1048576 => (value / 1048576, "MiB/s"),
            "B/s" when Math.Abs(value) >= 1024 => (value / 1024, "KiB/s"),
            "MHz" when Math.Abs(value) >= 1000 => (value / 1000, "GHz"),
            "s" when value >= 60 => (value / 60, "min"),
            _ => (value, sensor.Unit)
        };
        return $"{number.ToString("0", culture)} {unit}";
    }

    private static string SensorLabel(string name, Func<string, string> translate) => name switch
    {
        "Memory Used" or "GPU Memory Used" or "D3D Shared Memory Used" or "D3D Dedicated Memory Used" => translate("Sensors.MemoryUsed"),
        "Memory Available" or "GPU Memory Free" or "D3D Shared Memory Free" or "D3D Dedicated Memory Free" => translate("Sensors.MemoryAvailable"),
        "GPU Memory Total" or "Total Space" or "D3D Shared Memory Total" or "D3D Dedicated Memory Total" => translate("Sensors.Total"),
        "Free Space" => translate("Sensors.FreeSpace"),
        "Used Space" => translate("Sensors.UsedSpace"),
        "Read Rate" => translate("Sensors.ReadRate"),
        "Write Rate" => translate("Sensors.WriteRate"),
        "Remaining Time (Estimated)" => translate("Sensors.RemainingTime"),
        _ => name // Driver-defined sensor names remain verbatim, including units and provenance in the tooltip.
    };

    private static string? Category(string kind) => kind switch
    {
        "Cpu" or "Memory" or "Storage" or "Battery" => kind,
        "GpuNvidia" or "GpuAmd" or "GpuIntel" => "Gpu",
        _ => null
    };

    private static int Order(string category) => category switch { "Cpu" => 0, "Memory" => 1, "Gpu" => 2, "Storage" => 3, _ => 4 };
}

/// <summary>Contains a timestamped graph sample; null values split traces across unavailable readings.</summary>
public sealed record SensorTracePoint(DateTimeOffset Timestamp, double? Value);

/// <summary>Retains a bounded view-only trace without polling hardware or fabricating cache refreshes.</summary>
public sealed class SensorTraceHistory
{
    private readonly Dictionary<string, (string? Sensor, List<SensorTracePoint> Points)> _traces = new(StringComparer.Ordinal);

    /// <summary>Appends only new device samples and evicts readings outside the visible two-minute window.</summary>
    /// <param name="row">Current device projection, including missing readings and its source sensor.</param>
    /// <param name="now">Current collection time used to bound the trace.</param>
    public void Record(SensorMonitorRow row, DateTimeOffset now)
    {
        if (!_traces.TryGetValue(row.Id, out var trace) || trace.Sensor != row.SensorId)
            trace = (row.SensorId, []);
        trace.Points.RemoveAll(point => point.Timestamp < now.AddMinutes(-2));
        if (row.SampledAt >= now.AddMinutes(-2) && (trace.Points.Count == 0 || row.SampledAt > trace.Points[^1].Timestamp))
            trace.Points.Add(new(row.SampledAt, row.Percent));
        if (trace.Points.Count > 241) trace.Points.RemoveRange(0, trace.Points.Count - 241);
        _traces[row.Id] = trace;
    }

    /// <summary>Copies a device trace only when the visible page needs to render it.</summary>
    /// <param name="deviceId">Stable identity of the device being rendered.</param>
    public IReadOnlyList<SensorTracePoint> GetPoints(string deviceId) =>
        _traces.TryGetValue(deviceId, out var trace) ? trace.Points.ToArray() : [];

    /// <summary>Discards traces for removed devices and failed or disabled collections.</summary>
    public void Retain(IReadOnlyList<SensorMonitorRow> rows)
    {
        var present = rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _traces.Keys.Where(id => !present.Contains(id)).ToArray()) _traces.Remove(id);
    }
}
