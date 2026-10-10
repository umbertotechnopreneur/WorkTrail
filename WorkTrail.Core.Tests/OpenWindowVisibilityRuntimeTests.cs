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


using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Application;
using WorkTrail.Runtime;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class OpenWindowVisibilityRuntimeTests
{
    /// <summary>Preserves full-width handles and peer order through the typed client, pipe and dispatcher.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevealOpenWindows_RoundTripsThroughRuntimeFacade(bool fail)
    {
        var application = DispatchProxy.Create<IWorkTrailApplication, WindowRuntimeProxy>();
        var proxy = (WindowRuntimeProxy)application;
        proxy.Fail = fail;
        var installation = $"window-reveal-test-{Guid.NewGuid():N}";
        await using var host = new RuntimeHost(application, installation);
        Assert.True(host.TryStart());
        await using var client = new RuntimeClient(installation, TimeSpan.FromSeconds(5));
        var request = new WindowRevealRequest(0x1_0000_0001, [0x1_0000_0003, 0x1_0000_0002]);

        var result = await client.RevealOpenWindowsAsync(request, CancellationToken.None);

        Assert.NotNull(proxy.Request);
        Assert.Equal(request.MainWindowHandle, proxy.Request.MainWindowHandle);
        Assert.Equal(request.OpenWindowHandles, proxy.Request.OpenWindowHandles);
        Assert.Equal(!fail, result.Succeeded);
        Assert.Equal(fail ? "window.reveal.failed" : "window.reveal.completed", result.Code);
        if (!fail) Assert.Equal(2, result.Value);
        Assert.Equal("window.reveal_open.v1", RuntimeOperationCatalog.GetWireName(RuntimeOperation.WindowRevealOpenV1));
    }

    public class WindowRuntimeProxy : DispatchProxy
    {
        internal bool Fail { get; set; }
        internal WindowRevealRequest? Request { get; private set; }

        /// <summary>Records the request and returns an inert result without querying or changing native windows.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IWorkTrailApplication.RevealOpenWindowsAsync))
            {
                Request = (WindowRevealRequest)args![0]!;
                return Task.FromResult(Fail
                    ? OperationResult<int>.Failure("window.reveal.failed", "Window.RevealFailed")
                    : OperationResult<int>.Success("window.reveal.completed", "WindowRevealCompleted", Request.OpenWindowHandles.Count));
            }
            if (targetMethod?.Name == nameof(IAsyncDisposable.DisposeAsync)) return ValueTask.CompletedTask;
            if (targetMethod?.Name is "add_RuntimeStateChanged" or "remove_RuntimeStateChanged") return null;
            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
