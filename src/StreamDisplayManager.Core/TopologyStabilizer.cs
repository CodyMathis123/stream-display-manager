namespace StreamDisplayManager.Core;

/// <summary>
/// Tracks confirmed *display topologies*, not Steam streaming sessions.
/// Requires stable, consistent observations before publishing each change.
/// No monitor or window mutations are performed here.
/// </summary>
public sealed class TopologyStabilizer
{
    private readonly TimeSpan _minimumStableDuration;
    private string? _candidateSignature;
    private DateTimeOffset _candidateSince;
    private bool _candidateAccepted;
    private string? _publishedSignature;
    private long _sequence;

    public TopologyStabilizer(TimeSpan minimumStableDuration)
    {
        if (minimumStableDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumStableDuration));

        _minimumStableDuration = minimumStableDuration;
    }

    public StableTopologyTransition? Observe(
        TopologyAssessment topology,
        DateTimeOffset observedAtUtc)
    {
        if (!topology.IsConsistent)
        {
            // Windows can disagree between EnumDisplayDevices and Screen.AllScreens
            // while attaching or detaching SudoVDA. A later matching observation
            // must pass the debounce window again.
            _candidateSignature = null;
            _candidateAccepted = false;
            return null;
        }

        if (!string.Equals(_candidateSignature, topology.Signature, StringComparison.Ordinal))
        {
            _candidateSignature = topology.Signature;
            _candidateSince = observedAtUtc;
            _candidateAccepted = false;
        }

        if (_candidateAccepted || observedAtUtc - _candidateSince < _minimumStableDuration)
            return null;

        _candidateAccepted = true;

        // A brief transient may resolve back to the previously published topology.
        // It still needs stability, but doesn't need a duplicate transition.
        if (string.Equals(_publishedSignature, topology.Signature, StringComparison.Ordinal))
            return null;

        var transition = new StableTopologyTransition(
            ++_sequence, _publishedSignature, topology);
        _publishedSignature = topology.Signature;
        return transition;
    }

    public bool IsStable(TopologyAssessment topology) =>
        topology.IsConsistent &&
        _candidateAccepted &&
        string.Equals(_candidateSignature, topology.Signature, StringComparison.Ordinal);
}

/// <summary>A new stable desktop topology; not proof of a live Remote Play session.</summary>
public sealed record StableTopologyTransition(
    long Sequence, string? PreviousSignature, TopologyAssessment Topology);
