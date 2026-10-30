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

using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTrail.Application;

using WorkTrail.Services;

namespace WorkTrail.Runtime;

/// <summary>Owns the single local runtime and serves versioned requests over a same-user named pipe.</summary>
public sealed class RuntimeHost : IAsyncDisposable
{

    #region Fields
    private IWorkTrailApplication _application;
    private readonly Func<IWorkTrailApplication>? _applicationFactory;
    private readonly bool _ownsApplication;
    private readonly RuntimeEndpoint _endpoint;
    private readonly ILogger<RuntimeHost> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private RuntimeMutexLease? _mutexLease;
    private RuntimePipeServer? _server;
    private Task? _serverTask;
    private bool _disposed;

    #endregion

    #region Initialization

    /// <summary>Initializes a runtime host for a single application installation.</summary>
    public RuntimeHost(IWorkTrailApplication application, string installationId, ILogger<RuntimeHost>? logger = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _endpoint = RuntimeProtocol.CreateEndpoint(installationId);
        _logger = logger ?? NullLogger<RuntimeHost>.Instance;
    }

    /// <summary>
    /// Initializes a host that creates its local application only after runtime ownership is acquired.
    /// </summary>
    public RuntimeHost(
        Func<IWorkTrailApplication> applicationFactory,
        string installationId,
        ILogger<RuntimeHost>? logger = null)
    {
        _application = null!;
        _applicationFactory = applicationFactory ?? throw new ArgumentNullException(nameof(applicationFactory));
        _ownsApplication = true;
        _endpoint = RuntimeProtocol.CreateEndpoint(installationId);
        _logger = logger ?? NullLogger<RuntimeHost>.Instance;
    }

    #endregion

    #region Methods and properties

    /// <summary>Gets the endpoint used by this host.</summary>
    public RuntimeEndpoint Endpoint => _endpoint;

    /// <summary>Gets the local application after this host has acquired runtime ownership.</summary>
    public IWorkTrailApplication Application =>
        _application ?? throw new InvalidOperationException("The runtime host does not own a local application.");

    /// <summary>Starts the host when this process successfully acquires runtime ownership.</summary>
    public bool TryStart()
    {
        ThrowIfDisposed();
        _mutexLease = new RuntimeMutexLease(_endpoint.MutexName);
        if (!_mutexLease.Acquired)
        {
            _logger.LogDebug("Runtime mutex is already owned by another process.");
            _mutexLease.Dispose();
            _mutexLease = null;
            return false;
        }

        try
        {
            _application ??= _applicationFactory?.Invoke()
                ?? throw new InvalidOperationException("The runtime application factory returned no application.");
        }
        catch
        {
            _mutexLease.Dispose();
            _mutexLease = null;
            throw;
        }

        var dispatcher = new RuntimeRequestDispatcher(_application, _logger);
        var server = new RuntimePipeServer(
            _endpoint,
            dispatcher,
            _logger);
        _server = server;

        // One background loop owns pipe acceptance; mutations themselves remain serialized in the facade.
        _serverTask = Task.Run(() => server.ServeAsync(_shutdown.Token));
        _logger.LogInformation("Runtime host started. Pipe={PipeName}", _endpoint.PipeName);
        return true;
    }

    /// <summary>
    /// Stops the named-pipe server, drains requests and disposes a factory-owned application before
    /// releasing the runtime mutex.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Exception? shutdownFailure = null;
        try
        {
            _shutdown.Cancel();
            if (_serverTask is not null)
            {
                try
                {
                    await _serverTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown; no client-visible error is produced.
                }
                catch (Exception exception)
                {
                    shutdownFailure = exception;
                }
            }

            if (_server is not null)
            {
                try
                {
                    await _server.DrainRequestsAsync();
                }
                catch (Exception exception)
                {
                    shutdownFailure ??= exception;
                }
            }

            if (_ownsApplication && _application is not null)
            {
                try
                {
                    await _application.DisposeAsync();
                }
                catch (Exception exception)
                {
                    shutdownFailure ??= exception;
                }
            }
        }
        finally
        {
            // Runtime ownership is the last resource released. A successor cannot start while any
            // factory-owned writer from this host is still shutting down.
            _mutexLease?.Dispose();
            _mutexLease = null;
            _shutdown.Dispose();
        }

        if (shutdownFailure is not null)
        {
            ExceptionDispatchInfo.Capture(shutdownFailure).Throw();
        }
    }

    #endregion

    #region Implementation

    /// <summary>Rejects operations after runtime host disposal has begun.</summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RuntimeHost));
        }
    }

    #endregion

}

/// <summary>Provides a typed application facade backed by the local runtime pipe.</summary>
public sealed class RuntimeClient : IWorkTrailApplication
{
    /// <inheritdoc />
    public Task<OperationResult<string?>> ExportAvailabilityPdfAsync(long ownerWindowHandle, AvailabilityPdfRequest request, CancellationToken cancellationToken) =>
        // The native picker belongs to the client window, just like local snapping registration.
        WorkTrail.Services.AvailabilityPdfExportService.ExportAsync(ownerWindowHandle, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AvailabilityPlannerSnapshot>> GetAvailabilityPlannerAsync(AvailabilityPlannerRequest request, CancellationToken cancellationToken) =>
        SendAsync<AvailabilityPlannerSnapshot>(RuntimeOperation.PlannerGetV1, request, cancellationToken, WorldClockQueryTimeout);

    /// <inheritdoc />
    public Task<OperationResult<AvailabilitySchedule>> SaveAvailabilityScheduleAsync(AvailabilitySchedule schedule, CancellationToken cancellationToken) =>
        SendAsync<AvailabilitySchedule>(RuntimeOperation.PlannerScheduleSaveV1, schedule, cancellationToken);

    #region Fields
    private readonly WindowSnappingService _windowSnapping = new();

    #endregion

    #region Methods and properties

    /// <inheritdoc />
    public IWindowSnappingRegistration RegisterWindowSnapping(long windowHandle, Action<Exception> reportFailure) =>
        _windowSnapping.Register(windowHandle, reportFailure);

    /// <inheritdoc />
    public void ConfigureWindowSnapping(bool enabled) => _windowSnapping.Configure(enabled);

    #endregion

    #region Fields
    private static readonly TimeSpan StartupMutationTimeout = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan WorldClockQueryTimeout = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan WorldClockWeatherKeyTimeout = TimeSpan.FromSeconds(15);
    private readonly RuntimeEndpoint _endpoint;
    private readonly TimeSpan _timeout;
    private readonly ILogger<RuntimeClient> _logger;

    #endregion

    #region Initialization

    /// <summary>Initializes a client for the supplied installation identifier.</summary>
    public RuntimeClient(string installationId, TimeSpan timeout, ILogger<RuntimeClient>? logger = null)
    {
        _endpoint = RuntimeProtocol.CreateEndpoint(installationId);
        _timeout = timeout;
        _logger = logger ?? NullLogger<RuntimeClient>.Instance;
    }

    #endregion

    #region Methods and properties

    /// <inheritdoc />
    public Task<OperationResult<RuntimeHealth>> GetRuntimeHealthAsync(CancellationToken cancellationToken) => SendAsync<RuntimeHealth>(RuntimeOperation.RuntimeHealth, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockSnapshot>> GetWorldClocksAsync(CancellationToken cancellationToken) =>
        SendAsync<WorldClockSnapshot>(RuntimeOperation.WorldClocksGetV3, null, cancellationToken, WorldClockQueryTimeout);

    /// <inheritdoc />
    public Task<OperationResult<WorldClockSnapshot>> GetCelestialReferenceAsync(CancellationToken cancellationToken) =>
        SendAsync<WorldClockSnapshot>(RuntimeOperation.CelestialReferenceV1, null, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<CelestialSnapshot>> GetCelestialAsync(CelestialRequest request, CancellationToken cancellationToken) =>
        SendAsync<CelestialSnapshot>(RuntimeOperation.CelestialGetV1, request, cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<CelestialMapImage>> GetCelestialMapAsync(CelestialMapRequest request, CancellationToken cancellationToken) =>
        SendAsync<CelestialMapImage>(RuntimeOperation.CelestialMapV1, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockSnapshot>> ConvertWorldClocksAsync(WorldClockConversionRequest request, CancellationToken cancellationToken) =>
        SendAsync<WorldClockSnapshot>(RuntimeOperation.WorldClocksConvertV2, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockCityCatalog>> GetWorldClockCityCatalogAsync(CancellationToken cancellationToken) => SendAsync<WorldClockCityCatalog>(RuntimeOperation.WorldClocksCatalogV1, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> AddWorldClockAsync(string cityId, CancellationToken cancellationToken) =>
        SendAsync<WorldClockSelectionState>(RuntimeOperation.WorldClocksAddV3, new { cityId }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> RemoveWorldClockAsync(string cityId, CancellationToken cancellationToken) =>
        SendAsync<WorldClockSelectionState>(RuntimeOperation.WorldClocksRemoveV3, new { cityId }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WorldClockSelectionState>> MoveWorldClockAsync(
        string cityId,
        WorldClockMoveDirection direction,
        CancellationToken cancellationToken) =>
        SendAsync<WorldClockSelectionState>(
            RuntimeOperation.WorldClocksMoveV1,
            new WorldClockMoveRequest(cityId, direction),
            cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<string>> SetWorldClockWeatherKeyAsync(string secret, CancellationToken cancellationToken) =>
        SendAsync<string>(RuntimeOperation.WorldClocksWeatherKeySetV2, new { secret }, cancellationToken, WorldClockWeatherKeyTimeout);
    /// <inheritdoc />
    public Task<OperationResult<bool>> OpenApplicationLogAsync(CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.DiagnosticsLogOpen, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> OpenApplicationLogFolderAsync(CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.DiagnosticsLogOpenFolder, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> ShareApplicationLogAsync(long windowHandle, CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.DiagnosticsLogShare, new { windowHandle }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AppSettings>> GetSettingsAsync(CancellationToken cancellationToken) => SendAsync<AppSettings>(RuntimeOperation.SettingsGet, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<FeatureAccessSnapshot>> GetFeatureAccessAsync(CancellationToken cancellationToken) =>
        SendAsync<FeatureAccessSnapshot>(RuntimeOperation.FeatureAccessGetV1, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<AppSettings>> PatchSettingsAsync(SettingsPatch patch, CancellationToken cancellationToken) =>
        SendAsync<AppSettings>(RuntimeOperation.SettingsPatch, patch, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WindowState?>> RestoreWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken) => SendAsync<WindowState?>(RuntimeOperation.WindowStateRestore, new { windowKey, windowHandle }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<int>> RevealOpenWindowsAsync(WindowRevealRequest request, CancellationToken cancellationToken) => SendAsync<int>(RuntimeOperation.WindowRevealOpenV1, request, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<WindowState>> SaveWindowStateAsync(string windowKey, long windowHandle, CancellationToken cancellationToken) => SendAsync<WindowState>(RuntimeOperation.WindowStateSave, new { windowKey, windowHandle }, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> SetWindowOpenStateAsync(string windowKey, bool isOpen, CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.WindowOpenStateSet, new { windowKey, isOpen }, cancellationToken);
    /// <inheritdoc />
    /// <inheritdoc />
    public Task<OperationResult<bool>> GetStartupStatusAsync(CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.StartupStatus, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> SetStartupEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
        SendAsync<bool>(enabled ? RuntimeOperation.StartupEnable : RuntimeOperation.StartupDisable, null, cancellationToken, StartupMutationTimeout);
    /// <inheritdoc />
    public Task<OperationResult<ProductInformation>> GetProductInformationAsync(CancellationToken cancellationToken) => SendAsync<ProductInformation>(RuntimeOperation.ProductGet, null, cancellationToken);
    /// <inheritdoc />
    public Task<OperationResult<bool>> OpenProductLinkAsync(string linkKey, CancellationToken cancellationToken) => SendAsync<bool>(RuntimeOperation.ProductLinkOpen, new { linkKey }, cancellationToken);
    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    #endregion

    #region Implementation

    /// <summary>Exchanges one bounded request with the same-user runtime and maps unavailable transport results.</summary>
    private async Task<OperationResult<T>> SendAsync<T>(
        RuntimeOperation operation,
        object? payload,
        CancellationToken cancellationToken,
        TimeSpan? requestTimeout = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout ?? _timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", _endpoint.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            var wireName = RuntimeOperationCatalog.GetWireName(operation);
            var request = new RuntimeRequestEnvelope(RuntimeProtocol.ProtocolVersion, Guid.NewGuid(), wireName, JsonSerializer.SerializeToElement(payload, RuntimeProtocol.SerializerOptions), null, null);
            await RuntimeProtocol.WriteAsync(pipe, request, timeout.Token).ConfigureAwait(false);
            var response = await RuntimeProtocol.ReadAsync<RuntimeResponseEnvelope>(pipe, timeout.Token).ConfigureAwait(false);
            if (response.ProtocolVersion != RuntimeProtocol.ProtocolVersion)
            {
                return OperationResult<T>.Failure("ipc.protocol.unsupported", "IpcProtocolUnsupported");
            }

            var value = response.Payload is null ? default : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(response.Payload, RuntimeProtocol.SerializerOptions), RuntimeProtocol.SerializerOptions);
            return new OperationResult<T>(response.Succeeded, response.Code, response.MessageKey, value, response.Issues);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<T>.Failure(cancellationToken.IsCancellationRequested ? "operation.cancelled" : "runtime.unavailable", cancellationToken.IsCancellationRequested ? "OperationCancelled" : "RuntimeUnavailable");
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Runtime pipe request was unavailable. Operation={Operation}",
                RuntimeOperationCatalog.GetWireName(operation));
            return OperationResult<T>.Failure("runtime.unavailable", "RuntimeUnavailable");
        }
    }

    #endregion

}
