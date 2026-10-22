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

/// <summary>Checks startup registration repair and packaged startup broker outcomes.</summary>
public sealed class StartupServiceTests
{
    #region Implementation

    /// <summary>Resolves the startup executable from the base directory when the process path is unavailable.</summary>
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

    /// <summary>Builds the unpackaged startup command from the current executable path.</summary>
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

    /// <summary>Rejects a missing executable without reporting startup as enabled or writing a registration.</summary>
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

    /// <summary>Treats an obsolete executable registration as disabled and replaces it when startup is enabled.</summary>
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

    /// <summary>Repairs a stale registration even when the requested enabled preference has not changed.</summary>
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

    /// <summary>Allows startup registration removal even when no executable can be resolved.</summary>
    [Fact]
    public async Task Disable_RemovesRegistrationWithoutRequiringAnExecutable()
    {
        var store = new InMemoryStartupRegistrationStore { Value = "stale" };
        var service = new StartupService(store, command: null, _ => false);

        Assert.True(await service.SetEnabledAsync(false, CancellationToken.None));
        Assert.Null(store.Value);
        Assert.Equal(1, store.DeleteCount);
    }

    /// <summary>Enables packaged startup through the Windows broker instead of the registry backend.</summary>
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

    /// <summary>Disables packaged startup through the Windows broker instead of the registry backend.</summary>
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

    /// <summary>Reports an unsuccessful enable request when Windows policy or the user blocks the startup task.</summary>
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

    /// <summary>Recognizes policy-enabled startup without issuing another enable request.</summary>
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

    /// <summary>Propagates registration backend failures instead of misreporting startup as disabled.</summary>
    [Fact]
    public async Task BackendReadFailure_IsNotReportedAsDisabled()
    {
        var command = Command(Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Apps", "WorkTrail.exe"));
        var service = new StartupService(new ThrowingStartupRegistrationStore(), command, _ => true);

        await Assert.ThrowsAsync<IOException>(() => service.IsEnabledAsync(CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => service.SetEnabledAsync(true, CancellationToken.None));
    }

    /// <summary>Creates the expected executable and quoted startup command for registration tests.</summary>
    private static StartupCommand Command(string executablePath) =>
        new(executablePath, $"\"{executablePath}\" --start-with-windows");

    #endregion

    #region Test doubles

    /// <summary>Records startup registration mutations without writing the Windows registry.</summary>
    private sealed class InMemoryStartupRegistrationStore : IStartupRegistrationStore
    {
        #region Properties

        internal string? Value { get; set; }

        internal int WriteCount { get; private set; }

        internal int DeleteCount { get; private set; }

        #endregion

        #region Implementation

        /// <summary>Returns the startup command retained by the in-memory store.</summary>
        public string? Read() => Value;

        /// <summary>Stores the startup command and counts the registration write.</summary>
        public void Write(string commandLine)
        {
            Value = commandLine;
            WriteCount++;
        }

        /// <summary>Clears the startup command and counts the registration removal.</summary>
        public void Delete()
        {
            Value = null;
            DeleteCount++;
        }

        #endregion
    }

    /// <summary>Simulates an inaccessible startup registration backend for every operation.</summary>
    private sealed class ThrowingStartupRegistrationStore : IStartupRegistrationStore
    {
        #region Implementation

        /// <summary>Raises the synthetic storage failure instead of reporting a successful registration operation.</summary>
        public string? Read() => throw new IOException("Startup registration is unavailable.");

        /// <summary>Raises the synthetic storage failure instead of reporting a successful registration operation.</summary>
        public void Write(string commandLine) => throw new IOException("Startup registration is unavailable.");

        /// <summary>Raises the synthetic storage failure instead of reporting a successful registration operation.</summary>
        public void Delete() => throw new IOException("Startup registration is unavailable.");

        #endregion
    }

    /// <summary>Simulates Windows startup task states and counts broker requests.</summary>
    private sealed class InMemoryPackagedStartupTaskStore : IPackagedStartupTaskStore
    {
        #region Fields

        private readonly PackagedStartupTaskState _enableResult;
        private readonly PackagedStartupTaskState _disableResult;

        #endregion

        #region Initialization

        /// <summary>Sets the starting broker state and the outcomes returned by enable and disable requests.</summary>
        internal InMemoryPackagedStartupTaskStore(
            PackagedStartupTaskState state,
            PackagedStartupTaskState? enableResult = null,
            PackagedStartupTaskState? disableResult = null)
        {
            State = state;
            _enableResult = enableResult ?? state;
            _disableResult = disableResult ?? state;
        }

        #endregion

        #region Properties

        internal PackagedStartupTaskState State { get; private set; }

        internal int EnableCount { get; private set; }

        internal int DisableCount { get; private set; }

        #endregion

        #region Implementation

        /// <summary>Returns the simulated startup broker state after honoring cancellation.</summary>
        public Task<PackagedStartupTaskState> GetStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(State);
        }

        /// <summary>Counts an enable request and returns the configured simulated broker outcome.</summary>
        public Task<PackagedStartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnableCount++;
            State = _enableResult;

            return Task.FromResult(State);
        }

        /// <summary>Counts a disable request and returns the configured simulated broker outcome.</summary>
        public Task<PackagedStartupTaskState> DisableAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DisableCount++;
            State = _disableResult;

            return Task.FromResult(State);
        }

        #endregion
    }

    #endregion
}
