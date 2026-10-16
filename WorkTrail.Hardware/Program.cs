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


using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using WorkTrail.Services;

namespace WorkTrail.Hardware;

/// <summary>
/// Hosts the isolated hardware collector process and its authenticated named-pipe protocol.
/// </summary>
internal static class Program
{
    private static long _operationStarted;

    /// <summary>
    /// Records the start of a native hardware operation so the watchdog can enforce its deadline.
    /// </summary>
    internal static void BeginNativeOperation() => Interlocked.Exchange(ref _operationStarted, Stopwatch.GetTimestamp());

    /// <summary>
    /// Connects the collector to its parent process and serves isolated hardware sampling requests.
    /// </summary>
    private static async Task<int> Main(string[] arguments)
    {
        var hasExpectedArgumentCount = arguments.Length is 4 or 5;
        var pipeArgument = hasExpectedArgumentCount ? arguments[1] : string.Empty;
        var parentArgument = hasExpectedArgumentCount ? arguments[3] : string.Empty;
        var hasExpectedOptionLabels = hasExpectedArgumentCount &&
                                      arguments[0] == "--pipe" &&
                                      arguments[2] == "--parent";
        var hasValidPipeName = hasExpectedOptionLabels &&
                               pipeArgument.StartsWith("WorkTrail.Hardware.", StringComparison.Ordinal) &&
                               Guid.TryParseExact(pipeArgument["WorkTrail.Hardware.".Length..], "N", out _);
        var hasValidParentId = int.TryParse(parentArgument, NumberStyles.None, CultureInfo.InvariantCulture, out var parentId) &&
                               parentId > 0;
        var hasValidAdvancedFlag = arguments.Length != 5 || arguments[4] == "--advanced";

        if (!hasValidPipeName || !hasValidParentId || !hasValidAdvancedFlag)
        {
            return 2;
        }

        var advanced = arguments.Length == 5;
        using var identity = WindowsIdentity.GetCurrent();
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        if (advanced && !elevated)
        {
            return 3;
        }

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            return 4;
        }

        try
        {
            using var parent = Process.GetProcessById(parentId);
            await using var pipe = new NamedPipeClientStream(
                ".",
                arguments[1],
                PipeDirection.InOut,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(10_000).ConfigureAwait(false);
            HardwareTelemetryProtocol.VerifyServerProcess(pipe, parentId);
            var hello = new HardwareCollectorHello(HardwareTelemetryProtocol.Version, elevated);
            await HardwareTelemetryProtocol.WriteAsync(pipe, hello, CancellationToken.None).ConfigureAwait(false);

            // A blocked vendor/driver read may not honor cancellation. Only this dedicated
            // helper terminates; the application keeps its screenshots and explicit failure state.
            using var watchdog = new Timer(_ =>
            {
                try
                {
                    var started = Interlocked.Read(ref _operationStarted);
                    if (parent.HasExited || started != 0 && Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(8))
                        Environment.Exit(20);
                }
                catch { Environment.Exit(21); }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            using var collector = new LibreHardwareTelemetryCollector(advanced);
            while (true)
            {
                var request = await HardwareTelemetryProtocol.ReadAsync<HardwareCollectorRequest>(pipe, CancellationToken.None).ConfigureAwait(false);
                if (request.Version != HardwareTelemetryProtocol.Version)
                {
                    return 5;
                }

                var profile = HardwareSamplingProfiles.Get(request.SamplingProfile);
                if (request.Command == "stop")
                {
                    return 0;
                }

                if (request.Command != "sample")
                {
                    return 6;
                }

                Interlocked.Exchange(ref _operationStarted, Stopwatch.GetTimestamp());
                SystemSnapshot snapshot;
                var fatalError = false;
                try
                {
                    snapshot = await collector.SampleAsync(profile).ConfigureAwait(false);
                }
                catch
                {
                    // Never send exception messages, paths, device serial numbers or diagnostics over IPC.
                    snapshot = new SystemSnapshot(DateTimeOffset.UtcNow, "error", [], collector.DriverStatus, "sensor-read-failed");
                    fatalError = true;
                }
                await HardwareTelemetryProtocol.WriteAsync(pipe, snapshot, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Exchange(ref _operationStarted, 0);
                // Device-local errors are already isolated by the collector. A global failure may
                // leave LHM only partly initialized, so this process must never reuse that state.
                if (fatalError)
                {
                    return 8;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or TimeoutException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {
            // A parent disconnect or invalid frame ends this sensor-only process; no background runtime survives.
            return 7;
        }
    }
}
