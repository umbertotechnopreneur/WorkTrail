// SPDX-License-Identifier: MIT
using WorkTrail.Presentation;

namespace WorkTrail;

public sealed partial class ScreenshotWindow
{
    private bool _activityRailConnected;
    private int _activityRailGeneration;

    private async Task LoadActivityRailAsync(CancellationToken token)
    {
        var generation = ++_activityRailGeneration;
        try
        {
            var date = _selectedDate;
            var snapshot = await ActivitySpanLoader.LoadAsync(_application, date, date, token);
            token.ThrowIfCancellationRequested();
            if (generation != _activityRailGeneration || date != _selectedDate) return;
            ActivityRail.SetItems(snapshot.Spans, date, _strings, dayState: snapshot.Days.FirstOrDefault()?.State);
            TimelineSection.SetActivities(snapshot.Spans);
            ActivityRail.SelectTime(_items.Count > 0 ? _items[_selectedIndex].CapturedAt : null);
            if (_activityRailConnected) return;
            _activityRailConnected = true;
            ActivityRail.RefreshRequested += async () => await LoadActivityRailAsync(_lifetimeCancellation.Token);
            ActivityRail.SpanInvoked += span =>
            {
                // Select only a capture inside the interval; empty spans never jump to unrelated work.
                var index = Enumerable.Range(0, _items.Count)
                    .FirstOrDefault(i => _items[i].CapturedAt >= span.Start && _items[i].CapturedAt < span.End, -1);
                if (index >= 0) DayOverviewSection_SelectedIndexChanged(index);
            };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested && generation == _activityRailGeneration)
            {
                ActivityRail.ShowMessage(T("ActivityCalendar.Unavailable"));
                TimelineSection.SetActivities([]);
            }
        }
    }
}
