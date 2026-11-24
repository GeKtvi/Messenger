using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Model;

namespace Pingo.Edge;

// Sends each request to the first available region in the configured order ("us,eu,asia"). Listing the
// nearest region first gives nearest-region routing; listing the same region first on every edge
// makes that one the primary. Destination names are region names.
public sealed class RegionOrderPolicy(IConfiguration configuration) : ILoadBalancingPolicy
{
    private readonly string[] _order = (configuration["Edge:RegionOrder"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string Name => "RegionOrder";

    public DestinationState? PickDestination(
        HttpContext context, ClusterState cluster, IReadOnlyList<DestinationState> availableDestinations) =>
        availableDestinations.MinBy(destination => Position(destination.DestinationId));

    // A region missing from the order is only used when no listed one is available.
    private int Position(string region)
    {
        var index = Array.IndexOf(_order, region);
        return index < 0 ? int.MaxValue : index;
    }
}
