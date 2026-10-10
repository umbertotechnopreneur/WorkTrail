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


using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WorkTrail;

/// <summary>Renders thin desktop Acrylic that remains visible when another window receives focus.</summary>
/// <remarks>Create one instance per window. Windows still controls accessibility and material availability.</remarks>
public sealed class GlassBackdrop : SystemBackdrop
{
    private ICompositionSupportsSystemBackdrop? _connectedTarget;
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _configuration;

    /// <summary>Connects the glass material and the framework's theme policy to one window.</summary>
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(connectedTarget);
        ArgumentNullException.ThrowIfNull(xamlRoot);
        if (_connectedTarget is not null)
        {
            throw new InvalidOperationException("Each window requires its own GlassBackdrop instance.");
        }

        if (!DesktopAcrylicController.IsSupported())
        {
            // An unsupported rendering platform is an error; supported platforms retain the controller's system fallbacks.
            throw new PlatformNotSupportedException("Desktop Acrylic is unavailable on this operating system.");
        }

        _connectedTarget = connectedTarget;
        _configuration = new SystemBackdropConfiguration { IsInputActive = true };
        var frameworkConnected = false;
        try
        {
            base.OnTargetConnected(connectedTarget, xamlRoot);
            frameworkConnected = true;
            CopyFrameworkConfiguration(connectedTarget, xamlRoot);
            _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
            _controller.SetSystemBackdropConfiguration(_configuration);
            if (!_controller.AddSystemBackdropTarget(connectedTarget))
            {
                throw new InvalidOperationException("The desktop Acrylic controller could not attach to its window.");
            }
        }
        catch
        {
            // Failed attachment releases native rendering resources and propagates the original failure.
            try
            {
                _controller?.Dispose();
            }
            finally
            {
                _controller = null;
                _configuration = null;
                _connectedTarget = null;
                if (frameworkConnected)
                {
                    base.OnTargetDisconnected(connectedTarget);
                }
            }

            throw;
        }
    }

    /// <summary>Preserves framework theme and high-contrast changes while keeping background windows translucent.</summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        if (_configuration is null)
        {
            // The framework can publish its final configuration while the target is being disconnected.
            return;
        }

        RequireConnectedTarget(target);
        CopyFrameworkConfiguration(target, xamlRoot);
    }

    /// <summary>Disconnects and disposes the native material before the window is released.</summary>
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        RequireConnectedTarget(disconnectedTarget);
        var controller = _controller
            ?? throw new InvalidOperationException("The desktop Acrylic controller was not connected.");
        _controller = null;
        _configuration = null;
        _connectedTarget = null;
        try
        {
            // Disposal releases the sole target, even if its window has already closed during dispatcher shutdown.
            if (!controller.IsClosed)
            {
                controller.Dispose();
            }
        }
        finally
        {
            base.OnTargetDisconnected(disconnectedTarget);
        }
    }

    private void CopyFrameworkConfiguration(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        var configuration = _configuration
            ?? throw new InvalidOperationException("The glass material has no connected configuration.");
        var frameworkConfiguration = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        configuration.Theme = frameworkConfiguration.Theme;
        configuration.HighContrastBackgroundColor = frameworkConfiguration.HighContrastBackgroundColor;
        configuration.IsHighContrast = frameworkConfiguration.IsHighContrast;
        // Override focus policy only. The controller still respects transparency, power, and high-contrast policy.
        configuration.IsInputActive = true;
    }

    private void RequireConnectedTarget(ICompositionSupportsSystemBackdrop target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (_connectedTarget is null || !_connectedTarget.Equals(target))
        {
            throw new InvalidOperationException("The glass material received an event for an unconnected window.");
        }
    }
}
