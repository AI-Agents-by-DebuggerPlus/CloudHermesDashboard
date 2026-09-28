using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using CloudHermesDashboard.Models;
using CloudHermesDashboard.ViewModels;

namespace CloudHermesDashboard.Views;

public partial class StatusIndicator : UserControl
{
    private AgentViewModel? _agent;
    private bool _pulsing;

    public StatusIndicator()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookAgent();
        Unloaded += (_, _) => UnhookAgent();
    }

    private void HookAgent()
    {
        if (_agent != null)
            _agent.PropertyChanged -= OnAgentPropertyChanged;

        _agent = DataContext as AgentViewModel;

        if (_agent != null)
            _agent.PropertyChanged += OnAgentPropertyChanged;

        UpdatePulse();
    }

    private void UnhookAgent()
    {
        if (_agent != null)
            _agent.PropertyChanged -= OnAgentPropertyChanged;

        _agent = null;
        StopPulse();
    }

    private void OnAgentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AgentViewModel.Status) or nameof(AgentViewModel.StatusBrush))
            UpdatePulse();
    }

    private void UpdatePulse()
    {
        if (Dot == null)
            return;

        if (_agent?.Status == AgentStatus.Thinking)
            StartPulse();
        else
            StopPulse();
    }

    private void StartPulse()
    {
        if (_pulsing)
            return;

        var animation = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(800))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        Dot.BeginAnimation(OpacityProperty, animation);
        _pulsing = true;
    }

    private void StopPulse()
    {
        if (!_pulsing || Dot == null)
            return;

        Dot.BeginAnimation(OpacityProperty, null);
        Dot.Opacity = 1;
        _pulsing = false;
    }
}
