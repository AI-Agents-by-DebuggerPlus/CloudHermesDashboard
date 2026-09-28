using System.Globalization;
using System.Text.Json;
using CloudHermesDashboard.Models;

namespace CloudHermesDashboard.Services;

public sealed class RosterParse
{
    public bool Recognized { get; init; }

    public IReadOnlyList<AgentModel> Agents { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class TaskParse
{
    public bool Recognized { get; init; }

    public IReadOnlyList<TaskItemModel> Tasks { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public static class ReplyParsers
{
    private const string ModelLabel = "Hermes Agent via Nous Portal";

    private static readonly Dictionary<string, AgentRole> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["developer"] = AgentRole.Developer,
        ["researcher"] = AgentRole.Researcher,
        ["reviewer"] = AgentRole.Reviewer,
        ["planner"] = AgentRole.Planner,
        ["universal"] = AgentRole.Universal,
        ["orchestrator"] = AgentRole.Orchestrator
    };

    private static readonly Dictionary<string, AgentStatus> Statuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = AgentStatus.Idle,
        ["thinking"] = AgentStatus.Thinking,
        ["waitingapproval"] = AgentStatus.WaitingApproval,
        ["working"] = AgentStatus.Working,
        ["error"] = AgentStatus.Error
    };

    public static RosterParse ParseRoster(string text)
    {
        var warnings = new List<string>();
        foreach (var slice in JsonObjects(text))
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(slice);
            }
            catch (JsonException)
            {
                continue;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("agents", out var agents)
                    || agents.ValueKind != JsonValueKind.Array)
                    continue;

                var list = new List<AgentModel>();
                foreach (var item in agents.EnumerateArray())
                    Walk(item, null, list, warnings, 0);

                var unique = new List<AgentModel>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var agent in list)
                {
                    if (!seen.Add(agent.Id))
                    {
                        warnings.Add($"Повтор id {agent.Id}, оставлена последняя запись.");
                        unique.RemoveAll(existing => existing.Id == agent.Id);
                    }

                    unique.Add(agent);
                }

                return new RosterParse { Recognized = true, Agents = unique, Warnings = warnings };
            }
        }

        return new RosterParse { Recognized = false };
    }

    public static TaskParse ParseTasks(string text)
    {
        var warnings = new List<string>();
        foreach (var slice in JsonObjects(text))
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(slice);
            }
            catch (JsonException)
            {
                continue;
            }

            using (doc)
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("tasks", out var tasks)
                    || tasks.ValueKind != JsonValueKind.Array)
                    continue;

                var list = new List<TaskItemModel>();
                foreach (var item in tasks.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        warnings.Add("Элемент tasks не объект, пропущен.");
                        continue;
                    }

                    var id = StringProp(item, "id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        warnings.Add("Задача без id пропущена.");
                        continue;
                    }

                    var title = StringProp(item, "title");
                    var status = StringProp(item, "status");
                    if (status is not ("Current" or "Queued" or "Done"))
                        warnings.Add($"Задача {id}: статус \"{status}\" не из Current|Queued|Done, показан как есть.");

                    list.Add(new TaskItemModel
                    {
                        Id = id,
                        Title = string.IsNullOrWhiteSpace(title) ? "(без названия)" : title,
                        Status = string.IsNullOrWhiteSpace(status) ? "" : status,
                        Priority = ScalarProp(item, "priority"),
                        CreatedUtc = DateProp(item, "createdUtc")
                    });
                }

                return new TaskParse { Recognized = true, Tasks = list, Warnings = warnings };
            }
        }

        return new TaskParse { Recognized = false };
    }

    private static void Walk(JsonElement item, string? parentId, List<AgentModel> into, List<string> warnings, int depth)
    {
        if (depth > 6)
        {
            warnings.Add("Вложенность children глубже 6, хвост отброшен.");
            return;
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            warnings.Add("Элемент agents не объект, пропущен.");
            return;
        }

        var id = StringProp(item, "id");
        if (string.IsNullOrWhiteSpace(id))
            warnings.Add("Агент без id пропущен.");
        else
            into.Add(ToAgent(item, id, parentId, warnings));

        if (item.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                Walk(child, string.IsNullOrWhiteSpace(id) ? parentId : id, into, warnings, depth + 1);
        }
    }

    private static AgentModel ToAgent(JsonElement item, string id, string? parentId, List<string> warnings)
    {
        var rawRole = StringProp(item, "role");
        var role = Roles.TryGetValue(rawRole, out var parsedRole) ? parsedRole : AgentRole.Unknown;
        if (role == AgentRole.Unknown && rawRole.Length > 0)
            warnings.Add($"Агент {id}: неизвестная роль \"{rawRole}\".");

        var rawStatus = StringProp(item, "status");
        var statusKnown = Statuses.TryGetValue(rawStatus.Replace(" ", "", StringComparison.Ordinal), out var status)
                          || Statuses.TryGetValue(rawStatus, out status);
        if (!statusKnown && rawStatus.Length > 0)
        {
            warnings.Add($"Агент {id}: неизвестный статус \"{rawStatus}\".");
            status = AgentStatus.Idle;
        }

        var toolsets = new List<string>();
        if (item.TryGetProperty("toolsets", out var tools) && tools.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in tools.EnumerateArray())
            {
                if (tool.ValueKind == JsonValueKind.String && tool.GetString() is { Length: > 0 } name)
                    toolsets.Add(name);
            }
        }

        var task = StringProp(item, "task");
        return new AgentModel
        {
            Id = id,
            Name = NameOr(item, id),
            Role = role,
            RawRole = rawRole,
            Model = ModelLabel,
            Status = status,
            RawStatus = rawStatus,
            StatusKnown = statusKnown || rawStatus.Length == 0,
            CurrentTask = task.Length == 0 ? "Нет описания задачи." : task,
            Toolsets = toolsets,
            ParentId = parentId,
            LastActivityUtc = DateProp(item, "lastActivityUtc")
        };
    }

    private static string NameOr(JsonElement item, string id)
    {
        var name = StringProp(item, "name");
        return name.Length == 0 ? id : name;
    }

    private static string StringProp(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return "";
        return value.GetString()?.Trim() ?? "";
    }

    private static string ScalarProp(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
            return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
    }

    private static DateTime? DateProp(JsonElement item, string name)
    {
        var text = StringProp(item, name);
        if (text.Length == 0)
            return null;
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static IEnumerable<string> JsonObjects(string text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '{')
                continue;
            if (TrySlice(text, i, out var slice))
                yield return slice;
        }
    }

    private static bool TrySlice(string text, int start, out string slice)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escape)
                    escape = false;
                else if (ch == '\\')
                    escape = true;
                else if (ch == '"')
                    inString = false;
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
                depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    slice = text[start..(i + 1)];
                    return true;
                }
            }
        }

        slice = "";
        return false;
    }
}
