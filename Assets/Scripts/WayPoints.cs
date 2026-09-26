using System.Collections.Generic;
using UnityEngine;

public class WayPoints : MonoBehaviour
{
    // Distinguishes car lanes from pedestrian paths so spawners never mix them.
    public enum RouteKind
    {
        Vehicle,
        Pedestrian
    }

    [Header("Route")]
    public RouteKind kind = RouteKind.Vehicle;

    [Header("Speed limits")]
    // Applies to every segment unless overridden below. 0 means "no limit".
    public float defaultSpeedLimit = 0f;
    // Optional per-segment limit: entry i applies while driving toward waypoint i.
    // Leave empty (or 0) to fall back to defaultSpeedLimit.
    public float[] segmentSpeedLimits;

    // Routes that registered themselves while the game is running.
    private static readonly List<WayPoints> routes = new List<WayPoints>();

    // Backwards-compatible access to the primary route (the first registered one).
    // New code should assign a route per car instead of relying on this.
    public static Transform[] points
    {
        get
        {
            WayPoints primary = GetPrimary();
            return primary != null ? primary.Points : System.Array.Empty<Transform>();
        }
    }

    // All routes currently known, so several lanes can exist side by side.
    public static IReadOnlyList<WayPoints> Routes
    {
        get { return routes; }
    }

    // Waypoints owned by this component. Filled in Awake.
    public Transform[] Points { get; private set; }

    private void Awake()
    {
        BuildPoints();

        if (!routes.Contains(this))
        {
            routes.Add(this);
        }
    }

    private void OnDestroy()
    {
        routes.Remove(this);
    }

    private void BuildPoints()
    {
        Points = new Transform[transform.childCount];
        for (int i = 0; i < Points.Length; i++)
        {
            Points[i] = transform.GetChild(i);
        }
    }

    // Speed limit for the segment ending at the given waypoint index.
    // Returns 0 when no limit applies, meaning the car keeps its own speed.
    public float GetSpeedLimit(int waypointIndex)
    {
        if (segmentSpeedLimits != null
            && waypointIndex >= 0
            && waypointIndex < segmentSpeedLimits.Length
            && segmentSpeedLimits[waypointIndex] > 0f)
        {
            return segmentSpeedLimits[waypointIndex];
        }

        return defaultSpeedLimit;
    }

    // Picks the route whose nearest waypoint is closest to the given world position.
    public static WayPoints GetClosest(Vector3 position)
    {
        return FindClosest(position, false, RouteKind.Vehicle);
    }

    // Same as above but only considers routes of the given kind.
    public static WayPoints GetClosest(Vector3 position, RouteKind kindFilter)
    {
        return FindClosest(position, true, kindFilter);
    }

    // Index of the waypoint nearest to the given world position, or -1 when empty.
    public int GetClosestPointIndex(Vector3 position)
    {
        if (Points == null || Points.Length == 0)
        {
            return -1;
        }

        int closestIndex = -1;
        float closestSqrDistance = float.PositiveInfinity;

        for (int i = 0; i < Points.Length; i++)
        {
            if (Points[i] == null)
            {
                continue;
            }

            float sqrDistance = (Points[i].position - position).sqrMagnitude;
            if (sqrDistance < closestSqrDistance)
            {
                closestSqrDistance = sqrDistance;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private static WayPoints FindClosest(Vector3 position, bool filterByKind, RouteKind kindFilter)
    {
        WayPoints closestRoute = null;
        float closestSqrDistance = float.PositiveInfinity;

        for (int i = 0; i < routes.Count; i++)
        {
            WayPoints route = routes[i];
            if (route == null || route.Points == null || route.Points.Length == 0)
            {
                continue;
            }

            if (filterByKind && route.kind != kindFilter)
            {
                continue;
            }

            float sqrDistance = route.GetClosestSqrDistance(position);
            if (sqrDistance < closestSqrDistance)
            {
                closestSqrDistance = sqrDistance;
                closestRoute = route;
            }
        }

        return closestRoute;
    }

    private static WayPoints GetPrimary()
    {
        for (int i = 0; i < routes.Count; i++)
        {
            WayPoints route = routes[i];
            if (route != null && route.Points != null && route.Points.Length > 0)
            {
                return route;
            }
        }

        return routes.Count > 0 ? routes[0] : null;
    }

    private float GetClosestSqrDistance(Vector3 position)
    {
        float closestSqrDistance = float.PositiveInfinity;

        for (int i = 0; i < Points.Length; i++)
        {
            if (Points[i] == null)
            {
                continue;
            }

            float sqrDistance = (Points[i].position - position).sqrMagnitude;
            if (sqrDistance < closestSqrDistance)
            {
                closestSqrDistance = sqrDistance;
            }
        }

        return closestSqrDistance;
    }
}
