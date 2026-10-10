// SPDX-License-Identifier: MIT

using System.Globalization;
using WorkTrail.Services;

namespace WorkTrail.Presentation;

/// <summary>Contains an already-formatted hardware category summary.</summary>
public sealed record HardwareSummaryRow(string Label, string Value);

/// <summary>Groups all captured readings under their original device identity and sampling time.</summary>
public sealed record HardwareDeviceDetails(string Id, string Name, string Category, string UpdatedAt,
    IReadOnlyList<string> Readings);

/// <summary>Contains inert hardware text rendered by capture and diagnostics surfaces.</summary>
public sealed record HardwareSnapshotViewState(
    string Status,
    string CollectedAt,
    string DriverStatus,
    IReadOnlyList<HardwareSummaryRow> Summary,
    IReadOnlyList<HardwareDeviceDetails> Details,
    bool HasData = false);

/// <summary>Formats captured sensor DTOs without querying hardware or estimating missing measurements.</summary>
public static class HardwareSnapshotProjection
{
    /// <summary>Projects a historical or current collection using supplied localized labels.</summary>
    public static HardwareSnapshotViewState Create(
        SystemSnapshot? snapshot,
        CultureInfo culture,
        Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(translate);
        var rows = new List<HardwareSummaryRow>();
        foreach (var category in new[] { "Cpu", "Gpu", "Memory", "Storage", "Battery", "Network" })
        {
            var devices = snapshot?.Devices.Where(device => IsCategory(device.Kind, category))
                .Where(device => category != "Memory" || device.Id != "/vram" && device.Name != "Virtual Memory")
                .ToArray() ?? [];
            if (!devices.Any(device => device.Sensors.Any(sensor => sensor.Value.HasValue)))
            {
                continue;
            }

            var values = devices.Select(device => $"{device.Name.TrimEnd(':')}: {Summarize(device, culture, translate)}");
            rows.Add(new HardwareSummaryRow(
                translate("Hardware.Category." + category),
                string.Join("\n", values)));
        }

        if (snapshot is null)
        {
            // Historical captures without telemetry never receive a live read or a placeholder hardware section.
            return new HardwareSnapshotViewState(string.Empty, string.Empty, string.Empty, rows, []);
        }

        var details = new List<HardwareDeviceDetails>();
        foreach (var device in snapshot.Devices)
        {
            if (device.Sensors.Count == 0)
            {
                continue;
            }

            var category = device.Kind is "GpuNvidia" or "GpuAmd" or "GpuIntel" ? "Gpu" : device.Kind;
            var readings = device.Sensors.Select(sensor =>
                $"{sensor.Name} · {sensor.Kind}: {FormatSensor(sensor, culture, translate("Common.NotAvailable"))}").ToArray();
            details.Add(new HardwareDeviceDetails(device.Id, device.Name, translate("Hardware.Category." + category),
                string.Format(culture, translate("Hardware.DeviceUpdated"), device.SampledAt.ToLocalTime().ToString("G", culture)), readings));
        }

        var status = TranslateStatus(snapshot.Status, translate);
        if (snapshot.ErrorCode is { Length: > 0 } code)
        {
            status += $" ({code})";
        }

        return new HardwareSnapshotViewState(
            status,
            string.Format(culture, translate("Hardware.CollectedAt"), snapshot.Timestamp.ToLocalTime().ToString("G", culture)),
            $"{translate("Hardware.Advanced.Label")}: {TranslateStatus(snapshot.DriverStatus, translate)}",
            rows,
            details,
            details.Count > 0);
    }

    private static bool IsCategory(string kind, string category) => category == "Gpu"
        ? kind is "GpuNvidia" or "GpuAmd" or "GpuIntel"
        : string.Equals(kind, category, StringComparison.Ordinal);

    // device supplies the original captured measurements for one component.
    // culture formats the compact display values.
    // translate supplies existing localized capacity and transfer labels.
    private static string Summarize(HardwareDeviceSnapshot device, CultureInfo culture, Func<string, string> translate)
    {
        var missing = translate("Common.NotAvailable");
        var readings = device.Sensors.Where(sensor => sensor.Value.HasValue).ToArray();
        var values = new List<string>();
        switch (device.Kind)
        {
            case "Cpu":
                // Per-core peaks and package power are not substitutes for total CPU utilization.
                return Pick(readings, "Load", "CPU Total") is { } cpu ? FormatSummary(cpu, culture) : missing;
            case "GpuNvidia" or "GpuAmd" or "GpuIntel":
                if (HardwareUsageProjection.SelectGpuUtilization(readings) is { } gpu)
                    values.Add(FormatSummary(gpu, culture));
                var temperature = Pick(readings, "Temperature", "GPU Core")
                    ?? readings.Where(sensor => sensor.Kind == "Temperature"
                        && sensor.Name is not ("Warning Temperature" or "Critical Temperature"))
                        .OrderByDescending(sensor => sensor.Value).ThenBy(sensor => sensor.Id, StringComparer.Ordinal).FirstOrDefault();
                if (temperature is not null) values.Add(FormatSummary(temperature, culture));
                break;
            case "Memory":
                var used = Pick(readings, "Data", "Memory Used");
                var available = Pick(readings, "Data", "Memory Available");
                if (used is { Value: >= 0 } && available is { Value: >= 0 } && used.Unit == available.Unit)
                {
                    // Physical used plus available memory gives the captured total; virtual memory stays in details.
                    var total = used with { Value = used.Value + available.Value };
                    values.Add($"{used.Value.Value.ToString("0.#", culture)} / {FormatSummary(total, culture)}");
                }
                else if (used is not null) values.Add($"{translate("Sensors.MemoryUsed")} {FormatSummary(used, culture)}");
                break;
            case "Storage":
                var space = Pick(readings, "Data", "Total Space");
                var free = Pick(readings, "Data", "Free Space");
                var totalText = space is { Value: > 0 } ? FormatSummary(space, culture) : missing;
                var freeText = free is { Value: >= 0 } && (space is null || free.Unit == space.Unit && free.Value <= space.Value)
                    ? FormatSummary(free, culture) : missing;
                values.Add(string.Format(culture, translate("Sensors.SpaceSummary"), freeText, totalText));
                if (Pick(readings, "Throughput", "Read Rate") is { } read)
                    values.Add($"{translate("Sensors.ReadRate")} {FormatSummary(read, culture)}");
                if (Pick(readings, "Throughput", "Write Rate") is { } write)
                    values.Add($"{translate("Sensors.WriteRate")} {FormatSummary(write, culture)}");
                break;
            case "Network":
                if (Pick(readings, "Throughput", "Download Speed") is { } download)
                    values.Add($"↓ {FormatSummary(download, culture)}");
                if (Pick(readings, "Throughput", "Upload Speed") is { } upload)
                    values.Add($"↑ {FormatSummary(upload, culture)}");
                break;
            default:
                return SummarizeBattery(device, culture, missing);
        }
        return values.Count == 0 ? missing : string.Join(" · ", values);
    }

    // readings contains available captured values, including valid zero readings.
    // kind identifies the measurement type.
    // name identifies the representative driver sensor.
    private static HardwareSensorSnapshot? Pick(IEnumerable<HardwareSensorSnapshot> readings, string kind, string name) =>
        readings.FirstOrDefault(sensor => sensor.Kind == kind && sensor.Name == name);

    // sensor supplies an available value without modifying the stored measurement.
    // culture supplies decimal and grouping conventions.
    private static string FormatSummary(HardwareSensorSnapshot sensor, CultureInfo culture)
    {
        var value = sensor.Value!.Value;
        var (number, unit) = sensor.Unit switch
        {
            "B/s" when Math.Abs(value) >= 1073741824 => (value / 1073741824, "GiB/s"),
            "B/s" when Math.Abs(value) >= 1048576 => (value / 1048576, "MiB/s"),
            "B/s" when Math.Abs(value) >= 1024 => (value / 1024, "KiB/s"),
            _ => (value, sensor.Unit)
        };
        var format = unit is "%" or "°C" or "B/s" ? "0" : "0.#";
        return $"{number.ToString(format, culture)} {unit}".TrimEnd();
    }

    // device supplies battery measurements whose power and energy units remain distinct.
    // culture formats the existing battery display.
    // missing labels absent values without estimating them.
    private static string SummarizeBattery(HardwareDeviceSnapshot device, CultureInfo culture, string missing)
    {
        string[] preferredKinds = ["Level", "Power", "Energy", "TimeSpan", "Temperature"];
        var sensors = preferredKinds.SelectMany(kind => device.Sensors
            .Where(sensor => sensor.Kind == kind && sensor.Value.HasValue)
            .OrderByDescending(sensor => sensor.Name is "CPU Total" or "GPU Core" or "Remaining Capacity" ? 3
                : sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("Full Charged", StringComparison.OrdinalIgnoreCase) ? 2
                : sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(sensor => sensor.Id, StringComparer.Ordinal)
            .Take(kind is "Data" or "Throughput" or "Energy" ? 2 : 1)).ToArray();
        if (sensors.Length == 0)
        {
            sensors = device.Sensors
                .Where(sensor => sensor.Value.HasValue)
                .OrderBy(sensor => sensor.Id, StringComparer.Ordinal)
                .Take(2)
                .ToArray();
        }
        return sensors.Length == 0
            ? missing
            : string.Join(" · ", sensors.Select(sensor => $"{sensor.Name} {FormatSensor(sensor, culture, missing)}"));
    }

    private static string FormatSensor(HardwareSensorSnapshot sensor, CultureInfo culture, string missing) =>
        sensor.Value is { } value
            ? $"{value.ToString("0.##", culture)} {sensor.Unit}".TrimEnd()
            : missing;

    private static string TranslateStatus(string status, Func<string, string> translate) => status switch
    {
        "ready" => translate("Hardware.Status.Available"),
        "disabled" => translate("Hardware.Status.Disabled"),
        "partial" => translate("Hardware.Status.Partial"),
        "unavailable" => translate("Common.NotAvailable"),
        "error" => translate("Hardware.Status.Error"),
        "starting" => translate("Hardware.Status.Starting"),
        "stale" => translate("Hardware.Status.Stale"),
        "not-installed" => translate("Hardware.Status.NotInstalled"),
        "available" => translate("Hardware.Status.Installed"),
        "active" => translate("Hardware.Status.Active"),
        "access-denied" => translate("Hardware.Status.AccessDenied"),
        "blocked" => translate("Hardware.Status.Blocked"),
        "unsupported" or "unsupported-architecture" => translate("Hardware.Status.Unsupported"),
        _ => status
    };
}
