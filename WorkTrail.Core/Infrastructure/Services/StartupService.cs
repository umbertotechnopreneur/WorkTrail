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


using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace WorkTrail.Services;

/// <summary>
/// Controls Windows startup through the package startup-task broker or the current-user Run key.
/// </summary>
public sealed class StartupService
{
    internal const string PackagedStartupTaskId = "WorkTrailStartup";

    private readonly IStartupRegistrationBackend _backend;

    /// <summary>Creates a service for the current packaged or unpackaged application process.</summary>
    public StartupService()
        : this(CreateBackend())
    {
    }

    internal StartupService(
        IStartupRegistrationStore registrationStore,
        StartupCommand? command,
        Func<string, bool> fileExists)
        : this(new RegistryStartupRegistrationBackend(registrationStore, command, fileExists))
    {
    }

    internal StartupService(IPackagedStartupTaskStore startupTaskStore)
        : this(new PackagedStartupRegistrationBackend(startupTaskStore))
    {
    }

    private StartupService(IStartupRegistrationBackend backend) =>
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    /// <summary>Enables, repairs, or disables the startup integration used by this installation.</summary>
    public Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
        _backend.SetEnabledAsync(enabled, cancellationToken);

    /// <summary>Checks whether Windows currently considers this installation enabled at sign-in.</summary>
    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        _backend.IsEnabledAsync(cancellationToken);

    private static IStartupRegistrationBackend CreateBackend()
    {
        if (PackageIdentityProbe.HasCurrentPackageIdentity())
        {
            return new PackagedStartupRegistrationBackend(
                new WindowsPackagedStartupTaskStore(PackagedStartupTaskId));
        }

        var command = StartupCommandResolver.Resolve(
            Environment.ProcessPath,
            AppContext.BaseDirectory);
        return new RegistryStartupRegistrationBackend(
            new CurrentUserRunStartupRegistrationStore(),
            command,
            File.Exists);
    }
}

internal interface IStartupRegistrationBackend
{
    Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken);

    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);
}

internal sealed class RegistryStartupRegistrationBackend : IStartupRegistrationBackend
{
    private readonly IStartupRegistrationStore _registrationStore;
    private readonly StartupCommand? _command;
    private readonly Func<string, bool> _fileExists;

    internal RegistryStartupRegistrationBackend(
        IStartupRegistrationStore registrationStore,
        StartupCommand? command,
        Func<string, bool> fileExists)
    {
        _registrationStore = registrationStore ?? throw new ArgumentNullException(nameof(registrationStore));
        _command = command;
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
    }

    public Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enabled)
        {
            _registrationStore.Delete();
            return Task.FromResult(true);
        }

        // Never persist an autorun command that cannot resolve to the executable right now.
        if (_command is null || !_fileExists(_command.ExecutablePath))
        {
            return Task.FromResult(false);
        }

        _registrationStore.Write(_command.CommandLine);
        return Task.FromResult(true);
    }

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_command is null || !_fileExists(_command.ExecutablePath))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(string.Equals(
            _registrationStore.Read(),
            _command.CommandLine,
            StringComparison.OrdinalIgnoreCase));
    }
}

internal enum PackagedStartupTaskState
{
    Disabled,
    DisabledByUser,
    DisabledByPolicy,
    Enabled,
    EnabledByPolicy
}

internal interface IPackagedStartupTaskStore
{
    Task<PackagedStartupTaskState> GetStateAsync(CancellationToken cancellationToken);

    Task<PackagedStartupTaskState> RequestEnableAsync(CancellationToken cancellationToken);

    Task<PackagedStartupTaskState> DisableAsync(CancellationToken cancellationToken);
}

internal sealed class PackagedStartupRegistrationBackend : IStartupRegistrationBackend
{
    private readonly IPackagedStartupTaskStore _startupTaskStore;

    internal PackagedStartupRegistrationBackend(IPackagedStartupTaskStore startupTaskStore) =>
        _startupTaskStore = startupTaskStore ?? throw new ArgumentNullException(nameof(startupTaskStore));

    public async Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        var current = await _startupTaskStore.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (enabled)
        {
            if (IsEnabled(current))
            {
                return true;
            }

            if (current is PackagedStartupTaskState.DisabledByUser or PackagedStartupTaskState.DisabledByPolicy)
            {
                return false;
            }

            var requested = await _startupTaskStore.RequestEnableAsync(cancellationToken).ConfigureAwait(false);
            return IsEnabled(requested);
        }

        if (!IsEnabled(current))
        {
            return true;
        }

        var disabled = await _startupTaskStore.DisableAsync(cancellationToken).ConfigureAwait(false);
        return !IsEnabled(disabled);
    }

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        IsEnabled(await _startupTaskStore.GetStateAsync(cancellationToken).ConfigureAwait(false));

    private static bool IsEnabled(PackagedStartupTaskState state) =>
        state is PackagedStartupTaskState.Enabled or PackagedStartupTaskState.EnabledByPolicy;
}

internal sealed class WindowsPackagedStartupTaskStore : IPackagedStartupTaskStore
{
    private readonly string _taskId;

    internal WindowsPackagedStartupTaskStore(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new ArgumentException("A packaged startup task id is required.", nameof(taskId));
        }

        _taskId = taskId;
    }

    public async Task<PackagedStartupTaskState> GetStateAsync(CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(cancellationToken).ConfigureAwait(false);
        return Map(task.State);
    }

    public async Task<PackagedStartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(cancellationToken).ConfigureAwait(false);
        var state = await task.RequestEnableAsync().AsTask(cancellationToken).ConfigureAwait(false);
        return Map(state);
    }

    public async Task<PackagedStartupTaskState> DisableAsync(CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(cancellationToken).ConfigureAwait(false);
        task.Disable();
        return Map(task.State);
    }

    private async Task<StartupTask> GetTaskAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = await StartupTask.GetAsync(_taskId).AsTask(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return task;
    }

    private static PackagedStartupTaskState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => PackagedStartupTaskState.Disabled,
        StartupTaskState.DisabledByUser => PackagedStartupTaskState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => PackagedStartupTaskState.DisabledByPolicy,
        StartupTaskState.Enabled => PackagedStartupTaskState.Enabled,
        StartupTaskState.EnabledByPolicy => PackagedStartupTaskState.EnabledByPolicy,
        _ => throw new InvalidOperationException($"Unsupported Windows startup task state '{state}'.")
    };
}

internal sealed record StartupCommand(string ExecutablePath, string CommandLine);

internal static class StartupCommandResolver
{
    private const string StartupArgument = "--start-with-windows";

    internal static StartupCommand? Resolve(string? processPath, string? baseDirectory)
    {
        var candidate = string.IsNullOrWhiteSpace(processPath)
            ? string.IsNullOrWhiteSpace(baseDirectory)
                ? null
                : Path.Combine(baseDirectory, "WorkTrail.exe")
            : processPath;
        var executablePath = NormalizeAbsolutePath(candidate);
        return executablePath is null
            ? null
            : new StartupCommand(executablePath, $"\"{executablePath}\" {StartupArgument}");
    }

    private static string? NormalizeAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            return Path.IsPathFullyQualified(fullPath) && string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

internal static class StartupRegistrationPolicy
{
    internal static bool RequiresUpdate(bool savedEnabled, bool requestedEnabled, bool registrationIsValid) =>
        savedEnabled != requestedEnabled || (requestedEnabled && !registrationIsValid);
}

internal interface IStartupRegistrationStore
{
    string? Read();

    void Write(string commandLine);

    void Delete();
}

internal sealed class CurrentUserRunStartupRegistrationStore : IStartupRegistrationStore
{
    private const string RunKeyName = "WorkTrail";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunKeyName) as string;
    }

    public void Write(string commandLine)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("The current-user startup registry key is unavailable.");
        key.SetValue(RunKeyName, commandLine, RegistryValueKind.String);
    }

    public void Delete()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("The current-user startup registry key is unavailable.");
        key.DeleteValue(RunKeyName, false);
    }
}

internal static class PackageIdentityProbe
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    internal static bool HasCurrentPackageIdentity()
    {
        uint packageFullNameLength = 0;

        // The size probe returns ERROR_INSUFFICIENT_BUFFER only when the process owns package identity.
        var result = GetCurrentPackageFullName(ref packageFullNameLength, IntPtr.Zero);
        return result is ErrorSuccess or ErrorInsufficientBuffer;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
