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
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

/// <summary>Verifies setup outcomes without launching an installer or changing the machine.</summary>
public sealed class PawnIoInstallerTests
{
    /// <summary>Preserves a shared driver that already meets the bundled version.</summary>
    [Theory]
    [InlineData("2.2.0")]
    [InlineData("2.2.0.0")]
    [InlineData("3.0.0")]
    public async Task CurrentOrNewerSharedDriver_DoesNotLaunchSetup(string installedVersion)
    {
        var installer = Create(() => Version.Parse(installedVersion), _ => throw new InvalidOperationException("Setup must not run."));
        await installer.EnsureInstalledAsync(CancellationToken.None);
    }

    /// <summary>Installs only missing or older drivers and verifies the result before reuse.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("2.1.0")]
    public async Task MissingOrOlderDriver_InstallsOnceAndVerifiesTheInstalledVersion(string? previousVersion)
    {
        var installed = previousVersion is null ? null : Version.Parse(previousVersion);
        var runs = 0;
        var installer = Create(() => installed, _ =>
        {
            runs++;
            installed = new Version(2, 2, 0);
            return Task.FromResult(0);
        });

        await installer.EnsureInstalledAsync(CancellationToken.None);
        await installer.EnsureInstalledAsync(CancellationToken.None);

        Assert.Equal(1, runs);
    }

    /// <summary>Rejects a success exit code when no installation can be verified.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task ExitWithoutAnInstalledDriver_IsNotReportedAsSuccess(int exitCode)
    {
        var installer = Create(() => null, _ => Task.FromResult(exitCode));
        var failure = await Assert.ThrowsAsync<PawnIoSetupException>(() => installer.EnsureInstalledAsync(CancellationToken.None));
        Assert.Equal("HardwareAdvancedSetupFailed", failure.MessageKey);
    }

    /// <summary>Does not activate sensors or repeat setup while a restart is required.</summary>
    [Fact]
    public async Task RequiredRestart_RemainsVisibleOnRepeatedActivation()
    {
        Version? installed = null;
        var runs = 0;
        var installer = Create(() => installed, _ =>
        {
            runs++;
            installed = new Version(2, 2, 0);
            return Task.FromResult(3010);
        });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var failure = await Assert.ThrowsAsync<PawnIoSetupException>(() => installer.EnsureInstalledAsync(CancellationToken.None));
            Assert.Equal("HardwareAdvancedSetupRestartRequired", failure.MessageKey);
        }
        Assert.Equal(1, runs);
    }

    /// <summary>Explains cancelled consent whether returned by Windows or the installer.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeclinedConsent_HasAnActionableMessage(bool shellFailure)
    {
        var installer = Create(() => null, _ => shellFailure
            ? Task.FromException<int>(new Win32Exception(1223)) : Task.FromResult(1223));
        var failure = await Assert.ThrowsAsync<PawnIoSetupException>(() => installer.EnsureInstalledAsync(CancellationToken.None));
        Assert.Equal("HardwareAdvancedSetupCancelled", failure.MessageKey);
    }

    /// <summary>Allows a later activation to await the same ongoing installation.</summary>
    [Fact]
    public async Task ClosingTheCaller_DoesNotCancelOrDuplicateSystemSetup()
    {
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Version? installed = null;
        var runs = 0;
        var installer = Create(() => installed, token =>
        {
            Assert.False(token.CanBeCanceled);
            runs++;
            return pending.Task;
        });
        using var cancellation = new CancellationTokenSource();
        var first = installer.EnsureInstalledAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        var second = installer.EnsureInstalledAsync(CancellationToken.None);
        Assert.False(second.IsCompleted);
        installed = new Version(2, 2, 0);
        pending.SetResult(0);
        await second;
        Assert.Equal(1, runs);
    }

    /// <summary>Rejects bytes that differ from the audited official installer.</summary>
    [Fact]
    public async Task ModifiedInstaller_IsRejectedBeforeExecution()
    {
        using var bytes = new MemoryStream([1, 2, 3, 4]);
        await Assert.ThrowsAsync<InvalidDataException>(() => PawnIoInstaller.ValidateInstallerAsync(bytes, CancellationToken.None));
    }

    private static PawnIoInstaller Create(Func<Version?> readVersion, Func<CancellationToken, Task<int>> run) =>
        new(Path.Combine(Path.GetTempPath(), "PawnIO.UnitTests.exe"), readVersion, run);
}
