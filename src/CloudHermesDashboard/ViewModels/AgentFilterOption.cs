namespace CloudHermesDashboard.ViewModels;

public sealed class AgentFilterOption
{
    public AgentFilterOption(string? agentId, string label)
    {
        AgentId = agentId;
        Label = label;
    }

    public string? AgentId { get; }

    public string Label { get; }

    public override string ToString() => Label;
}
