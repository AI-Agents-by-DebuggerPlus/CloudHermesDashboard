using CloudHermesDashboard.Models;

namespace CloudHermesDashboard.Services;

public interface IAgentDataService
{
    event EventHandler<IReadOnlyList<AgentModel>>? SnapshotReceived;

    event EventHandler<AgentEventModel>? EventAdded;

    event EventHandler<string>? UnrecognizedPayload;

    void Start();

    void Stop();
}
