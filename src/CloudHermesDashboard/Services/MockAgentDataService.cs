using System.Windows.Threading;
using CloudHermesDashboard.Models;

namespace CloudHermesDashboard.Services;

public sealed class MockAgentDataService : IAgentDataService
{
    private readonly List<AgentModel> _agents;
    private readonly List<AgentEventModel> _events = [];
    private readonly Dictionary<string, AgentScript> _scripts;
    private readonly Random _random = new();
    private readonly DateTime _startedAt = DateTime.Now;
    private DispatcherTimer? _timer;

    public event EventHandler<IReadOnlyList<AgentModel>>? SnapshotReceived;

    public event EventHandler<AgentEventModel>? EventAdded;

#pragma warning disable CS0067
    public event EventHandler<string>? UnrecognizedPayload;
#pragma warning restore CS0067

    public MockAgentDataService()
    {
        _agents = CreateAgents();
        _scripts = CreateScripts();
        SeedHistory();
    }

    public IReadOnlyList<AgentModel> GetAgents() => _agents;

    public IReadOnlyList<AgentEventModel> GetEvents() => _events.ToList();

    public void Start()
    {
        if (_timer != null)
            return;

        _timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        Publish();
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void Publish() => SnapshotReceived?.Invoke(this, _agents.ToList());

    public void Stop()
    {
        if (_timer == null)
            return;

        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_timer == null)
            return;

        var agent = _agents[_random.Next(_agents.Count)];
        agent.Status = PickNextStatus(agent.Status);

        if (_random.NextDouble() < 0.3 && _scripts.TryGetValue(agent.Id, out var script))
            agent.CurrentTask = script.Tasks[_random.Next(script.Tasks.Length)];

        var type = PickEventType(agent.Status);
        var evt = new AgentEventModel
        {
            Timestamp = DateTime.Now,
            AgentId = agent.Id,
            EventType = type,
            Text = PickText(agent.Id, type)
        };

        _events.Add(evt);
        EventAdded?.Invoke(this, evt);
        Publish();

        _timer.Interval = TimeSpan.FromMilliseconds(_random.Next(2000, 3001));
    }

    private AgentStatus PickNextStatus(AgentStatus current)
    {
        AgentStatus[] bag =
        [
            AgentStatus.Working, AgentStatus.Working, AgentStatus.Working,
            AgentStatus.Thinking, AgentStatus.Thinking,
            AgentStatus.Idle, AgentStatus.Idle,
            AgentStatus.WaitingApproval,
            AgentStatus.Error
        ];

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var next = bag[_random.Next(bag.Length)];
            if (next != current)
                return next;
        }

        return AgentStatus.Thinking;
    }

    private AgentEventType PickEventType(AgentStatus status)
    {
        return status switch
        {
            AgentStatus.Error => AgentEventType.Error,
            AgentStatus.Working => _random.Next(3) == 0 ? AgentEventType.Message : AgentEventType.ToolCall,
            AgentStatus.WaitingApproval => AgentEventType.Message,
            _ => _random.Next(5) == 0 ? AgentEventType.ToolCall : AgentEventType.Message
        };
    }

    private string PickText(string agentId, AgentEventType type)
    {
        if (!_scripts.TryGetValue(agentId, out var script))
            return "Status changed.";

        var source = type switch
        {
            AgentEventType.ToolCall => script.ToolCalls,
            AgentEventType.Error => script.Errors,
            _ => script.Messages
        };

        return source[_random.Next(source.Length)];
    }

    private void SeedHistory()
    {
        Add("agt-atlas", 92, AgentEventType.ToolCall, "read_file src/Session/CredentialStore.cs");
        Add("agt-atlas", 86, AgentEventType.Message, "The dashboard process still resolves the provider key during startup.");
        Add("agt-atlas", 74, AgentEventType.ToolCall, "grep ICredentialBroker");
        Add("agt-atlas", 61, AgentEventType.Message, "Moving token lookup behind ICredentialBroker so the UI never sees the secret.");
        Add("agt-atlas", 48, AgentEventType.Error, "Session tests failed: ICredentialBroker is not registered in the host.");
        Add("agt-atlas", 33, AgentEventType.ToolCall, "apply_patch src/Host/CompositionRoot.cs");
        Add("agt-atlas", 21, AgentEventType.Message, "Registered the broker and re-ran the session store tests.");
        Add("agt-atlas", 6, AgentEventType.ToolCall, "run_tests SessionStore");

        Add("agt-nova", 88, AgentEventType.ToolCall, "search_docs long-context retrieval");
        Add("agt-nova", 70, AgentEventType.Message, "Chunking the repo loses cross-file call chains. Looking at graph-based retrieval.");
        Add("agt-nova", 55, AgentEventType.ToolCall, "read_file docs/runtime/retrieval.md");
        Add("agt-nova", 40, AgentEventType.Message, "Hybrid search plus a symbol graph covers both prose and references.");
        Add("agt-nova", 27, AgentEventType.Message, "Drafting a recommendation for the agent runtime, with a note on index freshness.");
        Add("agt-nova", 14, AgentEventType.ToolCall, "write_file notes/retrieval-recommendation.md");
        Add("agt-nova", 8, AgentEventType.Message, "Checking how often the index would have to rebuild for a monorepo this size.");
        Add("agt-nova", 2, AgentEventType.Message, "Still weighing rebuild cost against stale-symbol risk.");

        Add("agt-quill", 80, AgentEventType.ToolCall, "read_file src/Tools/ArgumentValidator.cs");
        Add("agt-quill", 66, AgentEventType.Message, "The validator drops unknown fields silently. That hides caller bugs.");
        Add("agt-quill", 50, AgentEventType.ToolCall, "apply_patch src/Tools/ArgumentValidator.cs");
        Add("agt-quill", 38, AgentEventType.Message, "Missing required arguments now fail before the call reaches the host.");
        Add("agt-quill", 24, AgentEventType.Error, "One fixture still expects the old silent-drop behavior.");
        Add("agt-quill", 15, AgentEventType.ToolCall, "apply_patch tests/Tools/ArgumentValidatorTests.cs");
        Add("agt-quill", 7, AgentEventType.Message, "Updated the fixture. The patch is ready for approval.");
        Add("agt-quill", 3, AgentEventType.Message, "Waiting for approval before this validation change is merged.");

        Add("agt-harbor", 100, AgentEventType.Message, "Onboarding is one long prompt. Splitting it into ordered tasks.");
        Add("agt-harbor", 84, AgentEventType.ToolCall, "read_file docs/onboarding.md");
        Add("agt-harbor", 69, AgentEventType.Message, "Atlas takes the session store, Nova the retrieval note, Quill the validator review.");
        Add("agt-harbor", 52, AgentEventType.Message, "Iris writes the release notes after those three land.");
        Add("agt-harbor", 30, AgentEventType.Message, "Plan is queued. Nothing else is blocked on me.");
        Add("agt-harbor", 18, AgentEventType.ToolCall, "list_tasks");
        Add("agt-harbor", 11, AgentEventType.Message, "All assigned tasks are already in progress.");
        Add("agt-harbor", 9, AgentEventType.Message, "Waiting for the next assignment.");

        Add("agt-iris", 76, AgentEventType.ToolCall, "list_merges since=12");
        Add("agt-iris", 58, AgentEventType.Message, "Twelve merges. Four of them change behavior a user would notice.");
        Add("agt-iris", 44, AgentEventType.ToolCall, "read_file CHANGELOG.md");
        Add("agt-iris", 29, AgentEventType.Message, "Grouping notes by session store, tool validation, and docs.");
        Add("agt-iris", 16, AgentEventType.ToolCall, "write_file docs/release-notes.md");
        Add("agt-iris", 10, AgentEventType.Message, "Flagged the credential-broker change as user-facing.");
        Add("agt-iris", 5, AgentEventType.Message, "Leaving the retrieval work out until Nova's recommendation is in.");
        Add("agt-iris", 1, AgentEventType.ToolCall, "read_file docs/release-notes.md");

        Add("agt-sable", 95, AgentEventType.ToolCall, "run_build dashboard-host");
        Add("agt-sable", 81, AgentEventType.Error, "Build failed: PackageReference could not be resolved.");
        Add("agt-sable", 67, AgentEventType.Message, "The host project is on an outdated lock file.");
        Add("agt-sable", 54, AgentEventType.ToolCall, "read_file src/Host/Host.csproj");
        Add("agt-sable", 41, AgentEventType.Message, "The package version in the project does not match the feed.");
        Add("agt-sable", 22, AgentEventType.ToolCall, "apply_patch src/Host/Host.csproj");
        Add("agt-sable", 12, AgentEventType.Error, "Restore failed again: the private feed is unreachable from this environment.");
        Add("agt-sable", 4, AgentEventType.Message, "Build is still red. Need a reachable feed or a vendored package.");
    }

    private void Add(string agentId, int minutesAgo, AgentEventType type, string text)
    {
        _events.Add(new AgentEventModel
        {
            AgentId = agentId,
            Timestamp = _startedAt.AddMinutes(-minutesAgo),
            EventType = type,
            Text = text
        });
    }

    private static List<AgentModel> CreateAgents() =>
    [
        new()
        {
            Id = "agt-atlas",
            Name = "Atlas",
            Role = AgentRole.Developer,
            Status = AgentStatus.Working,
            Model = "Hermes-4-70B via Nous Portal",
            CurrentTask = "Isolate provider credentials from the dashboard process and route them through the session store."
        },
        new()
        {
            Id = "agt-nova",
            Name = "Nova",
            Role = AgentRole.Researcher,
            Status = AgentStatus.Thinking,
            Model = "Hermes-4-405B via Nous Portal",
            CurrentTask = "Compare retrieval strategies for long-context codebases and recommend one for the agent runtime."
        },
        new()
        {
            Id = "agt-quill",
            Name = "Quill",
            Role = AgentRole.Reviewer,
            Status = AgentStatus.WaitingApproval,
            Model = "Hermes-4-70B via Nous Portal",
            CurrentTask = "Review the patch that rejects tool calls with missing required arguments before they reach the host."
        },
        new()
        {
            Id = "agt-harbor",
            Name = "Harbor",
            Role = AgentRole.Planner,
            Status = AgentStatus.Idle,
            Model = "Hermes-3-8B via local runtime",
            CurrentTask = "Waiting for the next assignment."
        },
        new()
        {
            Id = "agt-iris",
            Name = "Iris",
            Role = AgentRole.Universal,
            Status = AgentStatus.Working,
            Model = "Hermes-4-70B via Nous Portal",
            CurrentTask = "Draft release notes from the last twelve merged changes and flag anything user-facing."
        },
        new()
        {
            Id = "agt-sable",
            Name = "Sable",
            Role = AgentRole.Developer,
            Status = AgentStatus.Error,
            Model = "Hermes-4-70B via Nous Portal",
            CurrentTask = "Restore the dashboard host build after a missing package reference failed CI."
        }
    ];

    private static Dictionary<string, AgentScript> CreateScripts() => new()
    {
        ["agt-atlas"] = new AgentScript
        {
            Tasks =
            [
                "Isolate provider credentials from the dashboard process and route them through the session store.",
                "Register ICredentialBroker in the host composition root and re-run the session tests.",
                "Confirm the dashboard process no longer reads the provider key at startup."
            ],
            Messages =
            [
                "The broker interface is in place. Wiring the UI off the raw token next.",
                "Checked that the API key no longer appears in the dashboard process.",
                "Session store tests are running.",
                "Waiting on the host registration before I touch the view layer."
            ],
            ToolCalls =
            [
                "read_file src/Session/CredentialBroker.cs",
                "grep ICredentialBroker",
                "run_tests SessionStore",
                "apply_patch src/Host/CompositionRoot.cs"
            ],
            Errors =
            [
                "Test run failed: broker returned an empty token in the stub host.",
                "Build failed: CredentialBroker is not registered in the host."
            ]
        },
        ["agt-nova"] = new AgentScript
        {
            Tasks =
            [
                "Compare retrieval strategies for long-context codebases and recommend one for the agent runtime.",
                "Estimate how often a symbol graph would have to rebuild for this monorepo.",
                "Write the retrieval recommendation and call out index freshness."
            ],
            Messages =
            [
                "Still weighing rebuild cost against stale-symbol risk.",
                "Hybrid search plus a symbol graph covers both prose and references.",
                "Chunking alone drops cross-file call chains.",
                "The recommendation is almost ready for Harbor to schedule."
            ],
            ToolCalls =
            [
                "search_docs long-context retrieval",
                "read_file docs/runtime/retrieval.md",
                "write_file notes/retrieval-recommendation.md",
                "grep SymbolGraph"
            ],
            Errors =
            [
                "The sample index failed to load: symbol graph file is truncated.",
                "Search timed out on the full monorepo sample."
            ]
        },
        ["agt-quill"] = new AgentScript
        {
            Tasks =
            [
                "Review the patch that rejects tool calls with missing required arguments before they reach the host.",
                "Update the validator fixture that still expects silent drops.",
                "Waiting for approval before the validation change is merged."
            ],
            Messages =
            [
                "Waiting for approval before this validation change is merged.",
                "Missing required arguments now fail before the call reaches the host.",
                "The silent-drop path is gone. Callers get an explicit error.",
                "Holding the merge until someone signs off on the new failure mode."
            ],
            ToolCalls =
            [
                "read_file src/Tools/ArgumentValidator.cs",
                "apply_patch tests/Tools/ArgumentValidatorTests.cs",
                "run_tests ArgumentValidator",
                "grep silent-drop"
            ],
            Errors =
            [
                "One fixture still expects the old silent-drop behavior.",
                "Review blocked: the patch touches a public tool schema."
            ]
        },
        ["agt-harbor"] = new AgentScript
        {
            Tasks =
            [
                "Waiting for the next assignment.",
                "Break the onboarding flow into ordered tasks for Atlas, Nova, and Quill.",
                "Check that Iris is unblocked once the three implementation tasks land."
            ],
            Messages =
            [
                "Waiting for the next assignment.",
                "All assigned tasks are already in progress.",
                "Nothing else is blocked on the plan.",
                "Iris stays queued until the credential and validator work lands."
            ],
            ToolCalls =
            [
                "list_tasks",
                "read_file docs/onboarding.md",
                "read_file notes/retrieval-recommendation.md"
            ],
            Errors =
            [
                "Could not refresh the task list: the plan file is locked.",
                "Assignment failed: Nova already has an active task."
            ]
        },
        ["agt-iris"] = new AgentScript
        {
            Tasks =
            [
                "Draft release notes from the last twelve merged changes and flag anything user-facing.",
                "Group the notes by session store, tool validation, and docs.",
                "Leave retrieval out of the notes until Nova's recommendation lands."
            ],
            Messages =
            [
                "Flagged the credential-broker change as user-facing.",
                "Leaving the retrieval work out until Nova's recommendation is in.",
                "Four of the twelve merges change behavior a user would notice.",
                "Release notes draft is updated."
            ],
            ToolCalls =
            [
                "list_merges since=12",
                "read_file CHANGELOG.md",
                "write_file docs/release-notes.md",
                "read_file docs/release-notes.md"
            ],
            Errors =
            [
                "Could not read one merge: the changelog entry is malformed.",
                "Release notes write failed: the docs folder is read-only."
            ]
        },
        ["agt-sable"] = new AgentScript
        {
            Tasks =
            [
                "Restore the dashboard host build after a missing package reference failed CI.",
                "Point the host project at a feed it can actually reach.",
                "Re-run restore once the package version matches the lock file."
            ],
            Messages =
            [
                "Build is still red. Need a reachable feed or a vendored package.",
                "The package version in the project does not match the feed.",
                "The host project is on an outdated lock file.",
                "Retrying restore after the project file change."
            ],
            ToolCalls =
            [
                "run_build dashboard-host",
                "read_file src/Host/Host.csproj",
                "apply_patch src/Host/Host.csproj",
                "dotnet restore src/Host/Host.csproj"
            ],
            Errors =
            [
                "Build failed: PackageReference could not be resolved.",
                "Restore failed again: the private feed is unreachable from this environment."
            ]
        }
    };

    private sealed class AgentScript
    {
        public required string[] Tasks { get; init; }

        public required string[] Messages { get; init; }

        public required string[] ToolCalls { get; init; }

        public required string[] Errors { get; init; }
    }
}
