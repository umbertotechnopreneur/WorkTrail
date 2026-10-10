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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;
using WorkTrail.Services;

namespace WorkTrail.Runtime;

/// <summary>Connects frontends to the single local runtime and starts its background host when absent.</summary>
public static class RuntimeConnector
{
    /// <summary>Returns a pipe-backed application facade after waiting for the shared runtime to become reachable.</summary>
    /// <param name="executablePath">Trusted WorkTrail executable path supplied by the composition root.</param>
    /// <param name="timeoutSeconds">Per-request timeout in seconds.</param>
    /// <param name="cancellationToken">Cancellation for the connection attempt.</param>
    /// <param name="logger">Optional infrastructure logger.</param>
    public static async Task<IWorkTrailApplication?> ConnectAsync(
        string executablePath,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        ILogger<RuntimeClient>? logger = null)
    {
        var store = new LocalStore();
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300));
        var runtimeLogger = logger ?? NullLogger<RuntimeClient>.Instance;
        var installationId = store.TryLoadInstallationId();
        if (!string.IsNullOrWhiteSpace(installationId))
        {
            var existingClient = new RuntimeClient(installationId, timeout, runtimeLogger);
            var health = await existingClient.GetRuntimeHealthAsync(cancellationToken).ConfigureAwait(false);
            if (health.Succeeded)
            {
                return existingClient;
            }
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--background");
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            runtimeLogger.LogWarning("Background runtime launch failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return null;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Min(Math.Clamp(timeoutSeconds, 1, 300), 5));
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            installationId = store.TryLoadInstallationId();
            if (!string.IsNullOrWhiteSpace(installationId))
            {
                var client = new RuntimeClient(installationId, timeout, runtimeLogger);
                var health = await client.GetRuntimeHealthAsync(cancellationToken).ConfigureAwait(false);
                if (health.Succeeded)
                {
                    return client;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        return null;
    }
}
