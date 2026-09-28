using System.Globalization;
using System.IO;
using CloudHermesDashboard.Models;
using YamlDotNet.RepresentationModel;

namespace CloudHermesDashboard.Services;

public sealed class DelegationConfigRead
{
    public Dictionary<AgentRole, IReadOnlyList<string>> RoleToolsets { get; init; } = [];

    public int? MaxSpawnDepth { get; init; }

    public int? MaxConcurrentChildren { get; init; }

    public bool? OrchestratorEnabled { get; init; }
}

public sealed class ServerSettingsDraft
{
    public int? MaxSpawnDepth { get; init; }

    public int? MaxConcurrentChildren { get; init; }

    public bool? OrchestratorEnabled { get; init; }
}

internal static class YamlConfigEditor
{
    private static readonly Dictionary<string, AgentRole> Roles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["developer"] = AgentRole.Developer,
        ["researcher"] = AgentRole.Researcher,
        ["reviewer"] = AgentRole.Reviewer,
        ["planner"] = AgentRole.Planner,
        ["universal"] = AgentRole.Universal
    };

    public static DelegationConfigRead Read(string yaml)
    {
        var root = LoadRoot(yaml);
        if (!root.Children.Any())
            return new DelegationConfigRead();

        var delegation = FindMap(root, "delegation");
        if (delegation == null)
            return new DelegationConfigRead();

        var roles = new Dictionary<AgentRole, IReadOnlyList<string>>();
        var models = FindMap(delegation, "models");
        if (models != null)
        {
            foreach (var child in models.Children)
            {
                if (child.Key is not YamlScalarNode key || key.Value == null)
                    continue;
                if (!Roles.TryGetValue(key.Value, out var role))
                    continue;
                if (child.Value is not YamlMappingNode roleMap)
                    continue;
                roles[role] = ReadSequence(roleMap, "toolsets");
            }
        }

        return new DelegationConfigRead
        {
            RoleToolsets = roles,
            MaxSpawnDepth = ReadInt(delegation, "max_spawn_depth"),
            MaxConcurrentChildren = ReadInt(delegation, "max_concurrent_children"),
            OrchestratorEnabled = ReadBool(delegation, "orchestrator_enabled")
        };
    }

    public static string WriteRole(string yaml, AgentRole role, IReadOnlyList<string> toolsets)
    {
        var (stream, root) = LoadOrCreate(yaml);
        var delegation = EnsureMap(root, "delegation");
        var models = EnsureMap(delegation, "models");
        var roleMap = EnsureMap(models, RoleKey(role));
        Set(roleMap, "toolsets", new YamlSequenceNode(toolsets.Select(item => new YamlScalarNode(item))));
        return Save(stream);
    }

    public static string WriteServer(string yaml, ServerSettingsDraft draft)
    {
        var (stream, root) = LoadOrCreate(yaml);
        var delegation = EnsureMap(root, "delegation");
        if (draft.MaxSpawnDepth is int depth)
            Set(delegation, "max_spawn_depth", new YamlScalarNode(depth.ToString(CultureInfo.InvariantCulture)));
        if (draft.MaxConcurrentChildren is int children)
            Set(delegation, "max_concurrent_children", new YamlScalarNode(children.ToString(CultureInfo.InvariantCulture)));
        if (draft.OrchestratorEnabled is bool enabled)
            Set(delegation, "orchestrator_enabled", new YamlScalarNode(enabled ? "true" : "false"));
        return Save(stream);
    }

    private static YamlMappingNode LoadRoot(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return new YamlMappingNode();

        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Корень config.yaml не является YAML-объектом.");
        return root;
    }

    private static (YamlStream Stream, YamlMappingNode Root) LoadOrCreate(string yaml)
    {
        var stream = new YamlStream();
        if (string.IsNullOrWhiteSpace(yaml))
        {
            var created = new YamlMappingNode();
            stream.Documents.Add(new YamlDocument(created));
            return (stream, created);
        }

        stream.Load(new StringReader(yaml));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Корень config.yaml не является YAML-объектом.");
        return (stream, root);
    }

    private static string Save(YamlStream stream)
    {
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static YamlMappingNode? FindMap(YamlMappingNode parent, string key)
    {
        foreach (var child in parent.Children)
        {
            if (child.Key is YamlScalarNode scalar && scalar.Value == key && child.Value is YamlMappingNode map)
                return map;
        }

        return null;
    }

    private static YamlMappingNode EnsureMap(YamlMappingNode parent, string key)
    {
        var existing = FindMap(parent, key);
        if (existing != null)
            return existing;

        var created = new YamlMappingNode();
        parent.Children.Add(new YamlScalarNode(key), created);
        return created;
    }

    private static void Set(YamlMappingNode parent, string key, YamlNode value)
    {
        YamlScalarNode? found = null;
        foreach (var child in parent.Children)
        {
            if (child.Key is YamlScalarNode scalar && scalar.Value == key)
            {
                found = scalar;
                break;
            }
        }

        if (found != null)
            parent.Children.Remove(found);
        parent.Children.Add(new YamlScalarNode(key), value);
    }

    private static IReadOnlyList<string> ReadSequence(YamlMappingNode parent, string key)
    {
        foreach (var child in parent.Children)
        {
            if (child.Key is not YamlScalarNode scalar || scalar.Value != key || child.Value is not YamlSequenceNode sequence)
                continue;

            return sequence.Children
                .OfType<YamlScalarNode>()
                .Select(node => node.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();
        }

        return [];
    }

    private static int? ReadInt(YamlMappingNode parent, string key)
    {
        var text = ReadScalar(parent, key);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static bool? ReadBool(YamlMappingNode parent, string key)
    {
        var text = ReadScalar(parent, key);
        if (bool.TryParse(text, out var value))
            return value;
        return null;
    }

    private static string? ReadScalar(YamlMappingNode parent, string key)
    {
        foreach (var child in parent.Children)
        {
            if (child.Key is YamlScalarNode scalar && scalar.Value == key && child.Value is YamlScalarNode value)
                return value.Value;
        }

        return null;
    }

    private static string RoleKey(AgentRole role) => role.ToString().ToLowerInvariant();
}
