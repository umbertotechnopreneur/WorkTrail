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

using System.Text.Json;
using Microsoft.Extensions.Logging;
using WorkTrail.Application;

using WorkTrail.Services;

namespace WorkTrail.Runtime;

/// <summary>Validates and dispatches versioned runtime request envelopes to the application facade.</summary>
internal sealed class RuntimeRequestDispatcher
{

    #region Fields
    private readonly IWorkTrailApplication _application;
    private readonly ILogger _logger;

    #endregion

    #region Initialization

    /// <summary>Connects validated runtime requests to the application facade and logger.</summary>
    internal RuntimeRequestDispatcher(IWorkTrailApplication application, ILogger logger)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #endregion

    #region Methods and properties

    /// <summary>Dispatches one validated request without exposing application exceptions over IPC.</summary>
    internal async Task<RuntimeResponseEnvelope> DispatchAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        if (request.ProtocolVersion != RuntimeProtocol.ProtocolVersion)
        {
            return Failure(request, "ipc.protocol.unsupported", "IpcProtocolUnsupported");
        }

        if (!RuntimeOperationCatalog.TryResolve(request.Operation, out var operation))
        {
            return Failure(request, "command.invalid", "CommandInvalid");
        }

        try
        {
            return operation switch
            {
                RuntimeOperation.PlannerGetV1 => ToResponse(request, await _application.GetAvailabilityPlannerAsync(
                    Read<AvailabilityPlannerRequest>(request.Payload) ?? throw new InvalidDataException("A planner request is required."), cancellationToken)),
                RuntimeOperation.PlannerScheduleSaveV1 => ToResponse(request, await _application.SaveAvailabilityScheduleAsync(
                    Read<AvailabilitySchedule>(request.Payload) ?? throw new InvalidDataException("A schedule is required."), cancellationToken)),
                RuntimeOperation.RuntimeHealth => ToResponse(request, await _application.GetRuntimeHealthAsync(cancellationToken)),
                RuntimeOperation.WorldClocksGetV3 => ToResponse(request, await _application.GetWorldClocksAsync(cancellationToken)),
                RuntimeOperation.CelestialReferenceV1 => ToResponse(request, await _application.GetCelestialReferenceAsync(cancellationToken)),
                RuntimeOperation.CelestialGetV1 => ToResponse(request, await _application.GetCelestialAsync(
                                    Read<CelestialRequest>(request.Payload) ?? throw new InvalidDataException("A celestial request is required."), cancellationToken)),
                RuntimeOperation.CelestialMapV1 => ToResponse(request, await _application.GetCelestialMapAsync(
                                    Read<CelestialMapRequest>(request.Payload) ?? throw new InvalidDataException("An Earth projection request is required."), cancellationToken)),
                RuntimeOperation.WorldClocksConvertV2 => ToResponse(request, await _application.ConvertWorldClocksAsync(
                                    Read<WorldClockConversionRequest>(request.Payload)
                                        ?? throw new InvalidDataException("A world-clock conversion request is required."),
                                    cancellationToken)),
                RuntimeOperation.WorldClocksCatalogV1 => ToResponse(request, await _application.GetWorldClockCityCatalogAsync(cancellationToken)),
                RuntimeOperation.WorldClocksAddV3 => ToResponse(request, await _application.AddWorldClockAsync(ReadString(request.Payload, "cityId"), cancellationToken)),
                RuntimeOperation.WorldClocksRemoveV3 => ToResponse(request, await _application.RemoveWorldClockAsync(ReadString(request.Payload, "cityId"), cancellationToken)),
                RuntimeOperation.WorldClocksMoveV1 => ToResponse(request, await DispatchWorldClockMoveAsync(request, cancellationToken)),
                RuntimeOperation.WorldClocksWeatherKeySetV2 => ToResponse(request, await _application.SetWorldClockWeatherKeyAsync(ReadString(request.Payload, "secret"), cancellationToken)),
                RuntimeOperation.DiagnosticsLogOpen => ToResponse(request, await _application.OpenApplicationLogAsync(cancellationToken)),
                RuntimeOperation.DiagnosticsLogOpenFolder => ToResponse(request, await _application.OpenApplicationLogFolderAsync(cancellationToken)),
                RuntimeOperation.DiagnosticsLogShare => ToResponse(request, await _application.ShareApplicationLogAsync(ReadInt64(request.Payload, "windowHandle"), cancellationToken)),
                RuntimeOperation.SettingsGet => ToResponse(request, await _application.GetSettingsAsync(cancellationToken)),
                RuntimeOperation.FeatureAccessGetV1 => ToResponse(request, await _application.GetFeatureAccessAsync(cancellationToken)),
                RuntimeOperation.SettingsPatch => ToResponse(request, await _application.PatchSettingsAsync(Read<SettingsPatch>(request.Payload) ?? new SettingsPatch(new Dictionary<string, string?>()), cancellationToken)),
                RuntimeOperation.WindowStateRestore => ToResponse(request, await _application.RestoreWindowStateAsync(ReadString(request.Payload, "windowKey"), ReadInt64(request.Payload, "windowHandle"), cancellationToken)),
                RuntimeOperation.WindowRevealOpenV1 => ToResponse(request, await _application.RevealOpenWindowsAsync(Read<WindowRevealRequest>(request.Payload) ?? throw new ArgumentException("A window reveal request is required."), cancellationToken)),
                RuntimeOperation.WindowStateSave => ToResponse(request, await _application.SaveWindowStateAsync(ReadString(request.Payload, "windowKey"), ReadInt64(request.Payload, "windowHandle"), cancellationToken)),
                RuntimeOperation.WindowOpenStateSet => ToResponse(request, await _application.SetWindowOpenStateAsync(ReadString(request.Payload, "windowKey"), ReadRequiredBool(request.Payload, "isOpen"), cancellationToken)),
                RuntimeOperation.StartupStatus => ToResponse(request, await _application.GetStartupStatusAsync(cancellationToken)),
                RuntimeOperation.StartupEnable => ToResponse(request, await _application.SetStartupEnabledAsync(true, cancellationToken)),
                RuntimeOperation.StartupDisable => ToResponse(request, await _application.SetStartupEnabledAsync(false, cancellationToken)),
                RuntimeOperation.ProductGet => ToResponse(request, await _application.GetProductInformationAsync(cancellationToken)),
                RuntimeOperation.ProductLinkOpen => ToResponse(request, await _application.OpenProductLinkAsync(ReadString(request.Payload, "linkKey"), cancellationToken)),
                _ => throw new InvalidOperationException($"Runtime operation '{operation}' has no host dispatch handler.")
            };
        }
        catch (OperationCanceledException)
        {
            return Failure(request, "operation.cancelled", "OperationCancelled");
        }
        catch (Exception exception)
        {
            // Never serialize exception messages: they can disclose local paths or external provider details.
            _logger.LogWarning("Runtime operation failed. Operation={Operation} ExceptionType={ExceptionType}", request.Operation, exception.GetType().Name);
            return Failure(request, "runtime.operation.failed", "RuntimeOperationFailed");
        }
    }

    #endregion

    #region Implementation

    /// <summary>Copies an application result into the versioned response envelope.</summary>
    private static RuntimeResponseEnvelope ToResponse<T>(
        RuntimeRequestEnvelope request,
        OperationResult<T> result) =>
        new(
            RuntimeProtocol.ProtocolVersion,
            request.RequestId,
            result.Succeeded,
            result.Code,
            result.MessageKey,
            result.Value,
            result.Issues);

    /// <summary>Deserializes the reorder request before invoking the facade.</summary>
    private async Task<OperationResult<WorldClockSelectionState>> DispatchWorldClockMoveAsync(
        RuntimeRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        var move = Read<WorldClockMoveRequest>(request.Payload)
            ?? throw new InvalidDataException("A world-clock move request is required.");
        return await _application.MoveWorldClockAsync(move.CityId, move.Direction, cancellationToken);
    }

    /// <summary>Creates a protocol failure tied to the original request identifier.</summary>
    private static RuntimeResponseEnvelope Failure(
        RuntimeRequestEnvelope request,
        string code,
        string messageKey) =>
        new(
            RuntimeProtocol.ProtocolVersion,
            request.RequestId,
            false,
            code,
            messageKey,
            null,
            Array.Empty<ValidationIssue>());

    /// <summary>Deserializes an optional typed payload with the shared protocol options.</summary>
    private static T? Read<T>(JsonElement value) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? default
            : value.Deserialize<T>(RuntimeProtocol.SerializerOptions);

    /// <summary>Reads a string property, using an empty value when it is absent.</summary>
    private static string ReadString(JsonElement value, string name) =>
        ReadStringOrNull(value, name) ?? string.Empty;

    /// <summary>Returns a string property only when its JSON shape matches.</summary>
    private static string? ReadStringOrNull(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    /// <summary>Reads a required integer property or rejects the request.</summary>
    private static long ReadInt64(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property)
            && property.TryGetInt64(out var number) ? number : throw new ArgumentException("An integer is required.");

    /// <summary>Reads a required Boolean property or rejects the request.</summary>
    private static bool ReadRequiredBool(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean() : throw new ArgumentException("A Boolean is required.");

    /// <summary>Returns true only for an explicitly true Boolean property.</summary>
    private static bool ReadBool(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
        && property.GetBoolean();

    #endregion

}
