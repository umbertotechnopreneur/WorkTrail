// SPDX-License-Identifier: MIT
using System.Text.Json;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Validates the entire daily identity graph before estimated intervals can be saved.</summary>
internal static class TimesheetInferenceParser
{
    internal static JsonElement Schema { get; } = JsonDocument.Parse("""
        {
          "type":"object","additionalProperties":false,"required":["tasks","assignments","transitions"],
          "properties":{
            "tasks":{"type":"array","items":{"type":"object","additionalProperties":false,
              "required":["id","title","project","summary","evidenceObservationIds"],"properties":{
                "id":{"type":"string"},"title":{"type":"string"},"project":{"type":"string"},"summary":{"type":"string"},
                "evidenceObservationIds":{"type":"array","items":{"type":"string"}}
            }}},
            "assignments":{"type":"array","items":{"type":"object","additionalProperties":false,
              "required":["observationId","taskId"],"properties":{"observationId":{"type":"string"},"taskId":{"type":["string","null"]}}
            }},
            "transitions":{"type":"array","items":{"type":"object","additionalProperties":false,
              "required":["fromObservationId","toObservationId","relation"],"properties":{
                "fromObservationId":{"type":"string"},"toObservationId":{"type":"string"},
                "relation":{"type":"string","enum":["continuous","switch","uncertain"]}
            }}}
          }
        }
        """).RootElement.Clone();

    internal static TimesheetInference Parse(string json, IReadOnlyList<TimesheetObservation> observations, string scope)
    {
        if (json.Length is 0 or > 2_000_000) throw Invalid("size");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
        var root = document.RootElement;
        Object(root, "tasks", "assignments", "transitions");
        var ids = observations.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != observations.Count) throw Invalid("source_ids");
        var tasks = new List<TimesheetTask>();
        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in Array(root, "tasks", observations.Count))
        {
            Object(task, "id", "title", "project", "summary", "evidenceObservationIds");
            var id = Text(task, "id", 80, false);
            if (!taskIds.Add(id)) throw Invalid("duplicate_task");
            var evidence = Array(task, "evidenceObservationIds", observations.Count)
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : throw Invalid("evidence_type")).ToArray();
            if (evidence.Length == 0 || evidence.Distinct(StringComparer.Ordinal).Count() != evidence.Length || evidence.Any(e => !ids.Contains(e)))
                throw Invalid("task_evidence");
            tasks.Add(new(scope + id, Text(task, "title", 160, false), Text(task, "project", 120, true), Text(task, "summary", 600, true), evidence));
        }
        var assignments = new List<TimesheetAssignment>();
        var assigned = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var assignment in Array(root, "assignments", observations.Count))
        {
            Object(assignment, "observationId", "taskId");
            var id = Text(assignment, "observationId", 160, false);
            var task = assignment.GetProperty("taskId").ValueKind == JsonValueKind.Null ? null : Text(assignment, "taskId", 80, false);
            if (!ids.Contains(id) || (task is not null && !taskIds.Contains(task)) || !assigned.TryAdd(id, task)) throw Invalid("assignment");
            assignments.Add(new(id, task is null ? null : scope + task));
        }
        if (assigned.Count != observations.Count) throw Invalid("missing_assignments");
        foreach (var task in tasks)
            if (!assignments.Where(a => a.TaskId == task.Id).Select(a => a.ObservationId).ToHashSet(StringComparer.Ordinal).SetEquals(task.EvidenceObservationIds))
                throw Invalid("assignment_evidence");
        var pairs = observations.Zip(observations.Skip(1)).ToDictionary(p => (p.First.Id, p.Second.Id));
        var seen = new HashSet<(string, string)>();
        var transitions = new List<TimesheetTransition>();
        foreach (var transition in Array(root, "transitions", Math.Max(0, observations.Count - 1)))
        {
            Object(transition, "fromObservationId", "toObservationId", "relation");
            var left = Text(transition, "fromObservationId", 160, false);
            var right = Text(transition, "toObservationId", 160, false);
            var relation = Text(transition, "relation", 16, false);
            if (!pairs.TryGetValue((left, right), out var pair) || !seen.Add((left, right))) throw Invalid("transition_pair");
            var a = assigned[left]; var b = assigned[right];
            if (relation is not ("continuous" or "switch" or "uncertain")
                || ((a is null || b is null || pair.First.InstallationId != pair.Second.InstallationId) && relation != "uncertain")
                || (relation == "continuous" && a != b) || (relation == "switch" && a == b)) throw Invalid("transition_relation");
            transitions.Add(new(left, right, relation));
        }
        if (seen.Count != pairs.Count) throw Invalid("missing_transitions");
        return new(tasks, assignments, transitions);
    }

    private static void Object(JsonElement element, params string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid("object");
        var actual = element.EnumerateObject().Select(p => p.Name).ToArray();
        if (actual.Length != keys.Length || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(keys)) throw Invalid("properties");
    }

    private static JsonElement.ArrayEnumerator Array(JsonElement element, string key, int maximum)
    {
        var value = element.GetProperty(key);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum) throw Invalid("array");
        return value.EnumerateArray();
    }

    private static string Text(JsonElement element, string key, int maximum, bool empty)
    {
        var value = element.GetProperty(key);
        if (value.ValueKind != JsonValueKind.String) throw Invalid("text_type");
        var text = value.GetString()!;
        if (text.Length > maximum || (!empty && string.IsNullOrWhiteSpace(text)) || text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw Invalid("text_length");
        return text;
    }

    private static InvalidDataException Invalid(string code) => new("timesheet.parse." + code);
}
