using System.Windows.Media;
using CloudHermesDashboard.Models;

namespace CloudHermesDashboard.ViewModels;

public sealed class AgentEventViewModel
{
    private static readonly SolidColorBrush ToolCallBrush = Freeze("#6EA8FE");
    private static readonly SolidColorBrush MessageBrush = Freeze("#8B97AB");
    private static readonly SolidColorBrush ErrorBrush = Freeze("#F85149");

    public AgentEventViewModel(AgentEventModel model, string agentName, Brush accentBrush)
    {
        Timestamp = model.Timestamp;
        AgentId = model.AgentId;
        AgentName = agentName;
        EventType = model.EventType;
        Text = model.Text;
        AccentBrush = accentBrush;
        TypeBrush = model.EventType switch
        {
            AgentEventType.ToolCall => ToolCallBrush,
            AgentEventType.Error => ErrorBrush,
            _ => MessageBrush
        };
    }

    public DateTime Timestamp { get; }

    public string AgentId { get; }

    public string AgentName { get; }

    public AgentEventType EventType { get; }

    public string Text { get; }

    public Brush AccentBrush { get; }

    public Brush TypeBrush { get; }

    public string TimeLabel => Timestamp.ToString("HH:mm:ss");

    public string TypeLabel => EventType switch
    {
        AgentEventType.ToolCall => "tool_call",
        AgentEventType.Error => "error",
        _ => "message"
    };

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
