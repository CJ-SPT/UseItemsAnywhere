using System;

namespace UseItemsAnywhere.Integration;

public enum RuleSessionStatus { Standalone, Waiting, Active, Disabled }

// Independent of Unity and transport, including timeout and stale-reply handling.
public sealed class RuleSession
{
    public RuleSessionStatus Status { get; private set; }
    public GameplayRules? Rules { get; private set; }
    public string RequestId { get; private set; } = string.Empty;
    public string? Failure { get; private set; }
    private double? _connectedAt;

    public void Begin()
    {
        Reset();
        Status = RuleSessionStatus.Waiting;
        RequestId = Guid.NewGuid().ToString("N");
    }

    public void Connected(double now) => _connectedAt ??= now;

    public bool Apply(string requestId, GameplayRules rules)
    {
        if (Status != RuleSessionStatus.Waiting || requestId != RequestId) return false;
        Rules = rules;
        Status = RuleSessionStatus.Active;
        return true;
    }

    public void Tick(double now)
    {
        if (Status == RuleSessionStatus.Waiting && _connectedAt.HasValue && now - _connectedAt.Value >= 10)
            Disable("Host rules did not synchronize within 10 seconds. Install matching Use Items Anywhere core and Fika addon on the host and all clients.");
    }

    public void Disable(string reason)
    {
        if (Status == RuleSessionStatus.Disabled) return;
        Status = RuleSessionStatus.Disabled;
        Rules = null;
        Failure = reason;
    }

    public void Reset()
    {
        Status = RuleSessionStatus.Standalone;
        Rules = null;
        Failure = null;
        RequestId = string.Empty;
        _connectedAt = null;
    }
}
