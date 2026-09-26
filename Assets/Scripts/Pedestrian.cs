using System.Collections.Generic;
using UnityEngine;

public class Pedestrian : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 1.6f;
    public float turnSpeed = 360f;

    [Header("Route")]
    // Pedestrian path to follow. Should be a route of kind Pedestrian.
    public WayPoints route;
    public bool startAtClosestPoint = true;

    [Header("Crossing")]
    // Light this pedestrian waits for before stepping onto the road.
    public TrafficLight crossingLight;

    [Header("Avoidance")]
    // Personal space kept from other pedestrians.
    public float personalSpace = 1.2f;

    private const float SpatialCellSize = 5f;
    private static readonly List<Pedestrian> registered = new List<Pedestrian>();
    private static readonly SpatialHash<Pedestrian> spatialIndex = new SpatialHash<Pedestrian>(SpatialCellSize);
    private static readonly List<Pedestrian> queryBuffer = new List<Pedestrian>(16);
    private static int spatialIndexFrame = -1;

    private Transform[] waypoints;
    private int waypointIndex = 0;
    private Transform target;
    private PedestrianSpawner spawner;

    public static IReadOnlyList<Pedestrian> Registered { get { return registered; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        registered.Clear();
        spatialIndex.Clear();
        spatialIndexFrame = -1;
    }

    private static void EnsureSpatialIndex()
    {
        if (spatialIndexFrame == Time.frameCount)
        {
            return;
        }

        spatialIndexFrame = Time.frameCount;
        spatialIndex.Clear();

        for (int i = 0; i < registered.Count; i++)
        {
            Pedestrian pedestrian = registered[i];
            if (pedestrian != null)
            {
                spatialIndex.Insert(pedestrian);
            }
        }
    }

    private void OnEnable()
    {
        if (!registered.Contains(this))
        {
            registered.Add(this);
        }
    }

    private void OnDisable()
    {
        registered.Remove(this);
    }

    public void Init(PedestrianSpawner owner)
    {
        spawner = owner;
    }

    void Start()
    {
        // Managed by a spawner: Spawn() initialises the pedestrian on every reuse.
        if (spawner != null)
        {
            return;
        }

        if (!BeginRoute(route))
        {
            enabled = false;
        }
    }

    // Called by the pedestrian system every time this NPC is (re)used.
    public void Spawn(WayPoints assignedRoute, TrafficLight light)
    {
        enabled = true;
        crossingLight = light;

        if (!BeginRoute(assignedRoute))
        {
            ReleaseSelf();
        }
    }

    void Update()
    {
        if (target == null)
        {
            return;
        }

        Vector3 dir = target.position - transform.position;
        Vector3 flatDir = new Vector3(dir.x, 0f, dir.z);

        if (flatDir.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        float allowedSpeed = speed;
        allowedSpeed = Mathf.Min(allowedSpeed, GetCrossingSpeed(allowedSpeed));
        allowedSpeed = Mathf.Min(allowedSpeed, GetSeparationSpeed(allowedSpeed));

        transform.Translate(dir.normalized * allowedSpeed * Time.deltaTime, Space.World);

        if (Vector3.Distance(transform.position, target.position) <= 0.2f)
        {
            GetNextWaypoint();
        }
    }

    // Waits before the crossing while traffic (not pedestrians) has the right of way.
    float GetCrossingSpeed(float desiredSpeed)
    {
        if (crossingLight == null || crossingLight.PedestriansMayCross)
        {
            return desiredSpeed;
        }

        Vector3 forward = transform.forward;
        Vector3 position = transform.position;
        Vector3 toLine = crossingLight.StopPosition - position;
        float ahead = Vector3.Dot(toLine, forward);
        if (ahead <= 0f || ahead > crossingLight.detectionDistance)
        {
            return desiredSpeed;
        }

        float lateral = Vector3.ProjectOnPlane(toLine, forward).magnitude;
        if (lateral > crossingLight.laneHalfWidth)
        {
            return desiredSpeed;
        }

        float gap = ahead - crossingLight.stopOffset;
        if (gap <= 0f)
        {
            return 0f;
        }

        float range = Mathf.Max(0.01f, crossingLight.detectionDistance - crossingLight.stopOffset);
        return Mathf.Min(desiredSpeed, desiredSpeed * Mathf.Clamp01(gap / range));
    }

    // Keeps a little distance from other pedestrians so they never stack up.
    float GetSeparationSpeed(float desiredSpeed)
    {
        EnsureSpatialIndex();

        Vector3 forward = transform.forward;
        Vector3 position = transform.position;

        queryBuffer.Clear();
        spatialIndex.Query(position, personalSpace * 2f, queryBuffer);

        for (int i = 0; i < queryBuffer.Count; i++)
        {
            Pedestrian other = queryBuffer[i];
            if (other == null || other == this)
            {
                continue;
            }

            Vector3 offset = other.transform.position - position;
            float ahead = Vector3.Dot(offset, forward);
            if (ahead <= 0f)
            {
                continue;
            }

            float lateral = Vector3.ProjectOnPlane(offset, forward).magnitude;
            if (lateral > personalSpace)
            {
                continue;
            }

            if (ahead <= personalSpace)
            {
                return 0f;
            }
        }

        return desiredSpeed;
    }

    // Prepares the pedestrian for a walk. Returns false when there is no route to follow.
    bool BeginRoute(WayPoints assignedRoute)
    {
        route = assignedRoute != null
            ? assignedRoute
            : WayPoints.GetClosest(transform.position, WayPoints.RouteKind.Pedestrian);

        if (route == null || route.Points == null || route.Points.Length == 0)
        {
            waypoints = null;
            target = null;
            route = null;
            return false;
        }

        waypoints = route.Points;
        waypointIndex = 0;

        if (startAtClosestPoint)
        {
            waypointIndex = Mathf.Clamp(route.GetClosestPointIndex(transform.position), 0, waypoints.Length - 1);
        }

        target = waypoints[waypointIndex];
        return true;
    }

    void GetNextWaypoint()
    {
        waypointIndex++;

        if (waypointIndex >= waypoints.Length)
        {
            ReleaseSelf();
            return;
        }

        target = waypoints[waypointIndex];
    }

    void ReleaseSelf()
    {
        target = null;

        if (spawner != null)
        {
            spawner.Release(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
