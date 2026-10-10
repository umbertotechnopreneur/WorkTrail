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


using System.Runtime.ExceptionServices;

namespace WorkTrail.Services;

/// <summary>Completes startup activation redirection without blocking COM dispatch on the launching thread.</summary>
public static class StartupThreadService
{
    /// <summary>Runs terminal activation redirection on an MTA worker while the caller waits with COM message pumping.</summary>
    /// <param name="redirectAsync">The activation operation that must finish before the launching process exits.</param>
    public static void CompleteActivationRedirection(Func<Task> redirectAsync)
    {
        ArgumentNullException.ThrowIfNull(redirectAsync);

        ExceptionDispatchInfo? failure = null;
        var redirectThread = new Thread(() =>
        {
            try
            {
                var operation = redirectAsync()
                    ?? throw new InvalidOperationException("Activation redirection did not return an operation.");
                operation.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                // Propagate the original failure after joining; a failed redirect must never continue into WinUI startup.
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        })
        {
            IsBackground = true,
            Name = "WorkTrail activation redirection"
        };

        // WinRT redirection runs on MTA; Thread.Join preserves standard COM and SendMessage pumping on the STA caller.
        redirectThread.SetApartmentState(ApartmentState.MTA);
        redirectThread.Start();
        redirectThread.Join();
        failure?.Throw();
    }
}
