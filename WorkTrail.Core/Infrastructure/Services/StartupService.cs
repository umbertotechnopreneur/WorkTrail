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

    #region Fields
    internal const string PackagedStartupTaskId = "WorkTrailStartup";

    private readonly IStartupRegistrationBackend _backend;

    #endregion

    #region Initialization

    /// <summary>Creates a service for the current packaged or unpackaged application process.</summary>
    public StartupService()
        : this(CreateBackend())
    {
    }

    /// <summary>Connects startup control to the supplied registration backend.</summary>
    internal StartupService(
        IStartupRegistrationStore registrationStore,
        StartupCommand? command,
        Func<string, bool> fileExists)
        : this(new RegistryStartupRegistrationBackend(registrationStore, command, fileExists))
    {
    }

    /// <summary>Connects startup control to the supplied registration backend.</summary>
    internal StartupService(IPackagedStartupTaskStore startupTaskStore)
        : this(new PackagedStartupRegistrationBackend(startupTaskStore))
    {
    }

    /// <summary>Connects startup control to the supplied registration backend.</summary>
    private StartupService(IStartupRegistrationBackend backend) =>
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    #endregion

    #region Methods and properties

    /// <summary>Enables, repairs, or disables the startup integration used by this installation.</summary>
    public Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
        _backend.SetEnabledAsync(enabled, cancellationToken);

    /// <summary>Checks whether Windows currently considers this installation enabled at sign-in.</summary>
    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        _backend.IsEnabledAsync(cancellationToken);

    #endregion

    #region Implementation

    /// <summary>Chooses packaged startup tasks or current-user registry registration.</summary>
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

    #endregion

}

/// <summary>Defines startup state reads and writes independently of deployment type.</summary>
internal interface IStartupRegistrationBackend
{

    #region Methods and properties
    /// <summary>Applies the requested startup state through the selected deployment mechanism.</summary>
    Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken);

    /// <summary>Reports whether the selected startup registration is currently enabled.</summary>
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);

    #endregion

}

/// <summary>Maintains the current-user Run entry for an unpackaged executable.</summary>
internal sealed class RegistryStartupRegistrationBackend : IStartupRegistrationBackend
{

    #region Fields
    private readonly IStartupRegistrationStore _registrationStore;
    private readonly StartupCommand? _command;
    private readonly Func<string, bool> _fileExists;

    #endregion

    #region Initialization

    /// <summary>Connects registry registration to the resolved command and executable check.</summary>
    internal RegistryStartupRegistrationBackend(
        IStartupRegistrationStore registrationStore,
        StartupCommand? command,
        Func<string, bool> fileExists)
    {
        _registrationStore = registrationStore ?? throw new ArgumentNullException(nameof(registrationStore));
        _command = command;
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
    }

    #endregion

    #region Methods and properties

    /// <summary>
    /// Sets the requested startup state through the active startup mechanism.
    /// </summary>
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

    /// <summary>
    /// Returns whether the active startup mechanism is currently enabled.
    /// </summary>
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

    #endregion

}

/// <summary>Represents user and policy restrictions returned by Windows startup tasks.</summary>
internal enum PackagedStartupTaskState
{
    Disabled,
    DisabledByUser,
    DisabledByPolicy,
    Enabled,
    EnabledByPolicy
}

/// <summary>Abstracts the Windows packaged startup task broker.</summary>
internal interface IPackagedStartupTaskStore
{

    #region Methods and properties
    /// <summary>Reads the task's current user or policy-controlled state.</summary>
    Task<PackagedStartupTaskState> GetStateAsync(CancellationToken cancellationToken);

    /// <summary>Requests permission to enable the packaged startup task.</summary>
    Task<PackagedStartupTaskState> RequestEnableAsync(CancellationToken cancellationToken);

    /// <summary>Disables the packaged task and returns its resulting state.</summary>
    Task<PackagedStartupTaskState> DisableAsync(CancellationToken cancellationToken);

    #endregion

}

/// <summary>Applies startup preferences while respecting Windows user and policy restrictions.</summary>
internal sealed class PackagedStartupRegistrationBackend : IStartupRegistrationBackend
{

    #region Fields
    private readonly IPackagedStartupTaskStore _startupTaskStore;

    #endregion

    #region Initialization

    /// <summary>Connects startup registration to the packaged task store.</summary>
    internal PackagedStartupRegistrationBackend(IPackagedStartupTaskStore startupTaskStore) =>
        _startupTaskStore = startupTaskStore ?? throw new ArgumentNullException(nameof(startupTaskStore));

    #endregion

    #region Methods and properties

    /// <summary>
    /// Sets packaged startup state through Windows app-startup APIs.
    /// </summary>
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

    /// <summary>
    /// Returns whether Windows currently permits the packaged startup task.
    /// </summary>
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken) =>
        IsEnabled(await _startupTaskStore.GetStateAsync(cancellationToken).ConfigureAwait(false));

    #endregion

    #region Implementation

    /// <summary>Recognizes both explicitly enabled and policy-enabled task states.</summary>
    private static bool IsEnabled(PackagedStartupTaskState state) =>
        state is PackagedStartupTaskState.Enabled or PackagedStartupTaskState.EnabledByPolicy;

    #endregion

}

/// <summary>Adapts Windows startup task APIs to the application registration contract.</summary>
internal sealed class WindowsPackagedStartupTaskStore : IPackagedStartupTaskStore
{

    #region Fields
    private readonly string _taskId;

    #endregion

    #region Initialization

    /// <summary>Captures the required packaged startup task identifier.</summary>
    internal WindowsPackagedStartupTaskStore(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new ArgumentException("A packaged startup task id is required.", nameof(taskId));
        }

        _taskId = taskId;
    }

    #endregion

    #region Methods and properties

    /// <summary>
    /// Retrieves the packaged startup task state from Windows.
    /// </summary>
    public async Task<PackagedStartupTaskState> GetStateAsync(CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(cancellationToken).ConfigureAwait(false);
        return Map(task.State);
    }

    /// <summary>
    /// Requests that Windows enable the packaged startup task.
    /// </summary>
    public async Task<PackagedStartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(cancellationToken).ConfigureAwait(false);
        var state = await task.RequestEnableAsync().AsTask(cancellationToken).ConfigureAwait(false);
        return Map(state);
    }

    /// <summary>
    /// Requests that Windows disable the packaged startup task.
    /// </summary>
    public async Task<PackagedStartupTaskState> DisableAsync(CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(cancellationToken).ConfigureAwait(false);
        task.Disable();
        return Map(task.State);
    }

    #endregion

    #region Implementation

    /// <summary>Retrieves the Windows startup task with cancellation checks.</summary>
    private async Task<StartupTask> GetTaskAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var task = await StartupTask.GetAsync(_taskId).AsTask(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return task;
    }

    /// <summary>Maps Windows task states without inventing a fallback for unknown states.</summary>
    private static PackagedStartupTaskState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => PackagedStartupTaskState.Disabled,
        StartupTaskState.DisabledByUser => PackagedStartupTaskState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => PackagedStartupTaskState.DisabledByPolicy,
        StartupTaskState.Enabled => PackagedStartupTaskState.Enabled,
        StartupTaskState.EnabledByPolicy => PackagedStartupTaskState.EnabledByPolicy,
        _ => throw new InvalidOperationException($"Unsupported Windows startup task state '{state}'.")
    };

    #endregion

}

/// <summary>Pairs a resolved executable path with its quoted startup command.</summary>
internal sealed record StartupCommand(string ExecutablePath, string CommandLine);

/// <summary>Resolves the executable and arguments used for unpackaged startup.</summary>
internal static class StartupCommandResolver
{

    #region Fields
    private const string StartupArgument = "--start-with-windows";

    #endregion

    #region Methods and properties

    /// <summary>Builds the startup command from the process path or application directory.</summary>
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

    #endregion

    #region Implementation

    /// <summary>Returns a valid absolute executable path or null for unsupported paths.</summary>
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

    #endregion

}

/// <summary>Determines whether saved startup preferences require a registration update.</summary>
internal static class StartupRegistrationPolicy
{

    #region Methods and properties
    /// <summary>Requests repair when preferences differ or an enabled registration is invalid.</summary>
    internal static bool RequiresUpdate(bool savedEnabled, bool requestedEnabled, bool registrationIsValid) =>
        savedEnabled != requestedEnabled || (requestedEnabled && !registrationIsValid);

    #endregion

}

/// <summary>Abstracts the persisted unpackaged startup command.</summary>
internal interface IStartupRegistrationStore
{

    #region Methods and properties
    /// <summary>Reads the registered startup command if present.</summary>
    string? Read();

    /// <summary>Persists the supplied startup command.</summary>
    void Write(string commandLine);

    /// <summary>Removes the persisted startup command.</summary>
    void Delete();

    #endregion

}

/// <summary>Reads and updates only WorkTrail's current-user Run registry value.</summary>
internal sealed class CurrentUserRunStartupRegistrationStore : IStartupRegistrationStore
{

    #region Fields
    private const string RunKeyName = "WorkTrail";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    #endregion

    #region Methods and properties

    /// <summary>
    /// Reads the current startup command line from the legacy registry location.
    /// </summary>
    public string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunKeyName) as string;
    }

    /// <summary>
    /// Writes the startup command line to the legacy registry location.
    /// </summary>
    public void Write(string commandLine)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("The current-user startup registry key is unavailable.");
        key.SetValue(RunKeyName, commandLine, RegistryValueKind.String);
    }

    /// <summary>
    /// Removes the legacy startup command line from the registry.
    /// </summary>
    public void Delete()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("The current-user startup registry key is unavailable.");
        key.DeleteValue(RunKeyName, false);
    }

    #endregion

}

/// <summary>Detects package identity through the Windows process API.</summary>
internal static class PackageIdentityProbe
{

    #region Fields
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;

    #endregion

    #region Methods and properties

    /// <summary>Probes package-name buffer size to detect a packaged process.</summary>
    internal static bool HasCurrentPackageIdentity()
    {
        uint packageFullNameLength = 0;

        // The size probe returns ERROR_INSUFFICIENT_BUFFER only when the process owns package identity.
        var result = GetCurrentPackageFullName(ref packageFullNameLength, IntPtr.Zero);
        return result is ErrorSuccess or ErrorInsufficientBuffer;
    }

    #endregion

    #region Implementation

    /// <summary>Queries the current process package identity through Windows.</summary>
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    #endregion

}
