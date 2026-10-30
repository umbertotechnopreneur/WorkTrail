// SPDX-License-Identifier: MIT
using WorkTrail.Services;
namespace WorkTrail.Application;

public sealed partial class WorkTrailApplication
{
    /// <inheritdoc />
    public Task<OperationResult<string?>> ExportAvailabilityPdfAsync(long ownerWindowHandle, AvailabilityPdfRequest request, CancellationToken cancellationToken) =>
        AvailabilityPdfExportService.ExportAsync(ownerWindowHandle, request, cancellationToken);
    /// <inheritdoc />
    public async Task<OperationResult<AvailabilityPlannerSnapshot>> GetAvailabilityPlannerAsync(
        AvailabilityPlannerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StepMinutes is not (15 or 30 or 60)
            || request.ReferenceLocalTime is { Year: < 1901 or > 2098 })
            return OperationResult<AvailabilityPlannerSnapshot>.Failure("planner.invalid", "Planner.InvalidRequest");
        var settings = _settingsSnapshot.Value;
        var current = (await _worldClockOperations.GetCurrentAsync(cancellationToken).ConfigureAwait(false)).Value!;
        if (current.Clocks.Count == 0)
            return OperationResult<AvailabilityPlannerSnapshot>.Failure("planner.empty", "Planner.NoCities");
        var referenceId = request.ReferenceCityId ?? current.Clocks.FirstOrDefault(c => c.CityId == settings.HomeCityId)?.CityId
            ?? current.Clocks[0].CityId;
        if (!current.Clocks.Any(c => c.CityId == referenceId))
            return OperationResult<AvailabilityPlannerSnapshot>.Failure("planner.reference", "Planner.InvalidRequest");
        var projection = current;
        if (request.ReferenceLocalTime is { } local)
        {
            var converted = await Task.Run(() => _worldClockOperations.Convert(new(referenceId, local)), cancellationToken).ConfigureAwait(false);
            if (!converted.Succeeded || converted.Value is null)
                return OperationResult<AvailabilityPlannerSnapshot>.Failure(converted.Code, converted.MessageKey, converted.Issues.ToArray());
            projection = converted.Value;
        }
        var snapshot = await Task.Run(() => AvailabilityPlannerService.Build(projection, current,
            referenceId, request.StepMinutes, settings, cancellationToken), cancellationToken).ConfigureAwait(false);
        return OperationResult<AvailabilityPlannerSnapshot>.Success("planner.loaded", "WorldClocksLoaded", snapshot);
    }

    /// <inheritdoc />
    public Task<OperationResult<AvailabilitySchedule>> SaveAvailabilityScheduleAsync(
        AvailabilitySchedule schedule, CancellationToken cancellationToken) => MutateAsync(() =>
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var settings = _settingsSnapshot.Value;
        if (!AvailabilityPlannerService.IsValid(schedule)
            || !WorldClockSelection.NormalizePersisted(settings.WorldClockCityIds).Contains(schedule.CityId))
            return Task.FromResult(OperationResult<AvailabilitySchedule>.Failure("planner.schedule.invalid", "Planner.InvalidSchedule"));
        var schedules = new Dictionary<string, AvailabilitySchedule>(settings.AvailabilitySchedules
            ?? new Dictionary<string, AvailabilitySchedule>(), StringComparer.Ordinal);
        schedules[schedule.CityId] = schedule with
        { RestDays = schedule.RestDays.Distinct().ToArray(), Exceptions = schedule.Exceptions?.ToArray() };
        PersistSettings(settings with { AvailabilitySchedules = schedules });
        return Task.FromResult(OperationResult<AvailabilitySchedule>.Success("planner.schedule.saved", "SettingsSaved", schedules[schedule.CityId]));
    }, cancellationToken);
}
