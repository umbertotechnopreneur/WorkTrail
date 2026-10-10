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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WorkTrail.Services;
using Xunit;

namespace WorkTrail.Core.Tests;

public sealed class StartupServiceTests
{
    [Fact]
    public void MissingProcessPath_UsesBaseDirectoryExecutable()
    {
        var baseDirectory = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Apps", "WorkTrail");

        var command = StartupCommandResolver.Resolve(
            processPath: null,
            baseDirectory: baseDirectory);

        var expectedExecutable = Path.Combine(baseDirectory, "WorkTrail.exe");
        Assert.NotNull(command);
        Assert.Equal(expectedExecutable, command.ExecutablePath);
        Assert.Equal($"\"{expectedExecutable}\" --start-with-windows", command.CommandLine);
    }

    [Fact]
    public void UnpackagedCommand_UsesCurrentExecutable()
    {
        var executable = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Apps", "WorkTrail", "WorkTrail.exe");

        var command = StartupCommandResolver.Resolve(
            processPath: executable,
            baseDirectory: Path.GetDirectoryName(executable));

        Assert.NotNull(command);
        Assert.Equal(executable, command.ExecutablePath);
        Assert.Equal($"\"{executable}\" --start-with-windows", command.CommandLine);
    }

    [Fact]
    public async Task InvalidOrMissingExecutable_IsNotReportedAsEnabled()
    {
        var command = Command(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Missing", "WorkTrail.exe"));
        var store = new InMemoryStartupRegistrationStore { Value = command.CommandLine };
        var service = new StartupService(store, command, _ => false);

        Assert.False(await service.IsEnabledAsync(CancellationToken.None));
        Assert.False(await service.SetEnabledAsync(true, CancellationToken.None));
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task StaleRegistration_IsRejectedAndEnableRepairsIt()
    {
        var command = Command(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Program Files", "WorkTrail", "WorkTrail.exe"));
        var store = new InMemoryStartupRegistrationStore
        {
            Value = "\"C:\\obsolete\\Debug-Unpackaged\\WorkTrail.exe\" --start-with-windows"
        };
        var service = new StartupService(store, command, path => path == command.ExecutablePath);

        Assert.False(await service.IsEnabledAsync(CancellationToken.None));
        Assert.True(await service.SetEnabledAsync(true, CancellationToken.None));
        Assert.Equal(command.CommandLine, store.Value);
        Assert.Equal(1, store.WriteCount);
        Assert.True(await service.IsEnabledAsync(CancellationToken.None));
    }

    [Fact]
    public void UnchangedEnabledSetting_RequiresRepairWhenRegistrationIsStale()
    {
        Assert.True(StartupRegistrationPolicy.RequiresUpdate(
            savedEnabled: true,
            requestedEnabled: true,
            registrationIsValid: false));

        Assert.False(StartupRegistrationPolicy.RequiresUpdate(
            savedEnabled: true,
            requestedEnabled: true,
            registrationIsValid: true));
    }

    [Fact]
    public async Task Disable_RemovesRegistrationWithoutRequiringAnExecutable()
    {
        var store = new InMemoryStartupRegistrationStore { Value = "stale" };
        var service = new StartupService(store, command: null, _ => false);

        Assert.True(await service.SetEnabledAsync(false, CancellationToken.None));
        Assert.Null(store.Value);
        Assert.Equal(1, store.DeleteCount);
    }

    [Fact]
    public async Task PackagedEnable_RequestsTheWindowsStartupTaskBroker()
    {
        var store = new InMemoryPackagedStartupTaskStore(
            PackagedStartupTaskState.Disabled,
            enableResult: PackagedStartupTaskState.Enabled);
        var service = new StartupService(store);

        Assert.True(await service.SetEnabledAsync(true, CancellationToken.None));
        Assert.True(await service.IsEnabledAsync(CancellationToken.None));
        Assert.Equal(1, store.EnableCount);
        Assert.Equal(0, store.DisableCount);
    }

    [Fact]
    public async Task PackagedDisable_UsesTheWindowsStartupTaskBroker()
    {
        var store = new InMemoryPackagedStartupTaskStore(
            PackagedStartupTaskState.Enabled,
            disableResult: PackagedStartupTaskState.Disabled);
        var service = new StartupService(store);

        Assert.True(await service.SetEnabledAsync(false, CancellationToken.None));
        Assert.False(await service.IsEnabledAsync(CancellationToken.None));
        Assert.Equal(0, store.EnableCount);
        Assert.Equal(1, store.DisableCount);
    }

    [Theory]
    [InlineData((int)PackagedStartupTaskState.DisabledByUser)]
    [InlineData((int)PackagedStartupTaskState.DisabledByPolicy)]
    public async Task PackagedEnable_FailsWhenWindowsBlocksTheStartupTask(int stateValue)
    {
        var state = (PackagedStartupTaskState)stateValue;
        var store = new InMemoryPackagedStartupTaskStore(state);
        var service = new StartupService(store);

        Assert.False(await service.SetEnabledAsync(true, CancellationToken.None));
        Assert.False(await service.IsEnabledAsync(CancellationToken.None));
        Assert.Equal(0, store.EnableCount);
        Assert.Equal(0, store.DisableCount);
    }

    [Fact]
    public async Task PackagedEnabledByPolicy_IsReportedAsEnabledWithoutPrompting()
    {
        var store = new InMemoryPackagedStartupTaskStore(PackagedStartupTaskState.EnabledByPolicy);
        var service = new StartupService(store);

        Assert.True(await service.IsEnabledAsync(CancellationToken.None));
        Assert.True(await service.SetEnabledAsync(true, CancellationToken.None));
        Assert.Equal(0, store.EnableCount);
        Assert.Equal(0, store.DisableCount);
    }

    [Fact]
    public async Task BackendReadFailure_IsNotReportedAsDisabled()
    {
        var command = Command(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Apps", "WorkTrail.exe"));
        var service = new StartupService(new ThrowingStartupRegistrationStore(), command, _ => true);

        await Assert.ThrowsAsync<IOException>(() => service.IsEnabledAsync(CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => service.SetEnabledAsync(true, CancellationToken.None));
    }

    private static StartupCommand Command(string executablePath) =>
        new(executablePath, $"\"{executablePath}\" --start-with-windows");

    private sealed class InMemoryStartupRegistrationStore : IStartupRegistrationStore
    {
        internal string? Value { get; set; }

        internal int WriteCount { get; private set; }

        internal int DeleteCount { get; private set; }

        public string? Read() => Value;

        public void Write(string commandLine)
        {
            Value = commandLine;
            WriteCount++;
        }

        public void Delete()
        {
            Value = null;
            DeleteCount++;
        }
    }

    private sealed class ThrowingStartupRegistrationStore : IStartupRegistrationStore
    {
        public string? Read() => throw new IOException("Startup registration is unavailable.");

        public void Write(string commandLine) => throw new IOException("Startup registration is unavailable.");

        public void Delete() => throw new IOException("Startup registration is unavailable.");
    }

    private sealed class InMemoryPackagedStartupTaskStore : IPackagedStartupTaskStore
    {
        private readonly PackagedStartupTaskState _enableResult;
        private readonly PackagedStartupTaskState _disableResult;

        internal InMemoryPackagedStartupTaskStore(
            PackagedStartupTaskState state,
            PackagedStartupTaskState? enableResult = null,
            PackagedStartupTaskState? disableResult = null)
        {
            State = state;
            _enableResult = enableResult ?? state;
            _disableResult = disableResult ?? state;
        }

        internal PackagedStartupTaskState State { get; private set; }

        internal int EnableCount { get; private set; }

        internal int DisableCount { get; private set; }

        public Task<PackagedStartupTaskState> GetStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(State);
        }

        public Task<PackagedStartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnableCount++;
            State = _enableResult;
            return Task.FromResult(State);
        }

        public Task<PackagedStartupTaskState> DisableAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisableCount++;
            State = _disableResult;
            return Task.FromResult(State);
        }
    }
}
