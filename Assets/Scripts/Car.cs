using System.Collections.Generic;
using UnityEngine;

public class Car : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 10f;
    // Maximum steering rate in degrees per second.
    public float turnSpeed = 180f;
    // Corrects models whose forward axis is not +Z.
    public float yawOffset = 0f;

    [Header("Longitudinal")]
    // Forward acceleration in units per second squared.
    public float acceleration = 4f;
    // How sharply the car brakes for a given speed error (per second).
    public float brakeResponse = 4f;
    // Hard braking limit in units per second squared.
    public float maxBraking = 12f;

    [Header("Steering")]
    // How far ahead along the route the car aims. Larger values round corners more.
    public float lookAheadDistance = 4f;
    // Distance at which a waypoint counts as reached.
    public float arrivalRadius = 0.5f;
    // Slowest speed multiplier while taking the sharpest turn.
    [Range(0.1f, 1f)] public float cornerSpeedFactor = 0.4f;
    // Turn angle (degrees) at which cornerSpeedFactor is reached.
    public float cornerAngle = 90f;

    [Header("Route")]
    // Route this car follows. When empty the nearest vehicle route is picked automatically.
    public WayPoints route;
    // Join the route at the waypoint closest to the spawn position. Off for scene cars.
    public bool startAtClosestPoint = false;

    [Header("Collision avoidance")]
    public bool avoidanceEnabled = true;
    // How far ahead the car looks for other cars in its lane.
    public float sensorDistance = 8f;
    // Minimum bumper-to-bumper distance the car keeps.
    public float safeGap = 2.5f;
    // Cars further sideways than this are treated as being in another lane.
    public float laneWidth = 3f;

    [Header("Traffic signals")]
    // Stop at red and yellow traffic lights.
    public bool obeyTrafficLights = true;

    [Header("Lane changing")]
    // Try to move to a parallel lane when the current one is congested.
    public bool laneChangingEnabled = true;
    // Congestion threshold: change lanes when the gap ahead drops below this.
    public float laneChangeLookAhead = 8f;
    // Seconds to wait after a lane change before trying again.
    public float laneChangeCooldown = 3f;
    // How far sideways a parallel lane may be.
    public float laneChangeSearchRadius = 6f;
    // Required clearance in front and behind in the target lane.
    public float laneChangeClearance = 8f;

    private static readonly List<Car> registered = new List<Car>();
    // Lightweight XZ spatial hash so a car only inspects nearby cars, not every car.
    private const float SpatialCellSize = 10f;
    private static readonly SpatialHash<Car> spatialIndex = new SpatialHash<Car>(SpatialCellSize);
    private static readonly List<Car> queryBuffer = new List<Car>(32);
    private static int spatialIndexFrame = -1;

    private Transform[] waypoints;
    private int waypointIndex = 0;
    private Transform target;
    // Current travel direction. Smoothed by the steering rate instead of snapping to waypoints.
    private Vector3 heading = Vector3.forward;
    // Speed with momentum: eased toward the desired speed instead of snapping to it.
    private float currentSpeed = 0f;
    // Gap to the nearest car ahead in the current lane, refreshed once per frame.
    private float nearestGapAhead = float.PositiveInfinity;
    private float laneChangeTimer = 0f;
    private CarPool pool;

    // Active cars, used for collision avoidance and density checks.
    public static IReadOnlyList<Car> Registered { get { return registered; } }

    public Vector3 Heading { get { return heading; } }

    // Speed the car is actually doing right now, after momentum.
    public float CurrentSpeed { get { return currentSpeed; } }

    // Number of lane changes made during the current run. Useful for debugging.
    public int LaneChanges { get; private set; }

    // How hard the car is currently steering, in degrees.
    public float CurrentTurnAngle
    {
        get
        {
            if (waypoints == null || target == null)
            {
                return 0f;
            }

            return Vector3.Angle(heading, GetDesiredHeading());
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        registered.Clear();
        spatialIndex.Clear();
        spatialIndexFrame = -1;
    }

    // Rebuilds the spatial index at most once per frame, on first use.
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
            Car car = registered[i];
            if (car != null)
            {
                spatialIndex.Insert(car);
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

    private void OnValidate()
    {
        speed = Mathf.Max(0f, speed);
        turnSpeed = Mathf.Max(1f, turnSpeed);
        lookAheadDistance = Mathf.Max(0.1f, lookAheadDistance);
        arrivalRadius = Mathf.Max(0.05f, arrivalRadius);
        cornerAngle = Mathf.Max(1f, cornerAngle);
        acceleration = Mathf.Max(0.01f, acceleration);
        brakeResponse = Mathf.Max(0.01f, brakeResponse);
        maxBraking = Mathf.Max(0.01f, maxBraking);
        laneChangeLookAhead = Mathf.Max(0f, laneChangeLookAhead);
        laneChangeCooldown = Mathf.Max(0f, laneChangeCooldown);
        laneChangeSearchRadius = Mathf.Max(0.1f, laneChangeSearchRadius);
        laneChangeClearance = Mathf.Max(0f, laneChangeClearance);
    }

    public void Init(CarPool owner)
    {
        pool = owner;
    }

    void Start()
    {
        // Managed by a pool: Spawn() initialises the car on every reuse.
        if (pool != null)
        {
            return;
        }

        // Standalone car placed directly in the scene.
        if (!BeginRoute(route))
        {
            enabled = false;
        }
    }

    // Called by the traffic system every time this car is (re)used.
    public void Spawn(WayPoints assignedRoute, bool joinAtClosestPoint)
    {
        enabled = true;
        startAtClosestPoint = joinAtClosestPoint;

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

        if (laneChangeTimer > 0f)
        {
            laneChangeTimer -= Time.deltaTime;
        }

        nearestGapAhead = GetNearestGapAhead();
        UpdateLaneChange();

        Vector3 desiredHeading = GetDesiredHeading();
        heading = RotateTowardsFlat(heading, desiredHeading, turnSpeed * Time.deltaTime);

        float desiredSpeed = GetAllowedSpeed() * GetCornerSpeedFactor(desiredHeading);
        UpdateSpeed(desiredSpeed);

        transform.position += heading * currentSpeed * Time.deltaTime;

        ApplyRotation();

        AdvanceIfPassed();
    }

    // Eases the car toward the desired speed instead of snapping to it.
    void UpdateSpeed(float desiredSpeed)
    {
        if (desiredSpeed >= currentSpeed)
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, acceleration * Time.deltaTime);
            return;
        }

        // Brake proportionally to how far over the target we are, up to a hard limit.
        float deceleration = Mathf.Clamp(brakeResponse * (currentSpeed - desiredSpeed), 0f, maxBraking);
        currentSpeed = Mathf.Max(desiredSpeed, currentSpeed - deceleration * Time.deltaTime);
    }

    // Direction the car is actually travelling, independent of any model yaw offset.
    Vector3 TravelDirection()
    {
        return heading.sqrMagnitude > 1e-6f ? heading.normalized : transform.forward;
    }

    // Aims at a point further along the route so corners are entered gradually.
    Vector3 GetDesiredHeading()
    {
        Vector3 lookAhead = GetLookAheadPoint();
        Vector3 dir = lookAhead - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 1e-6f)
        {
            return TravelDirection();
        }

        return dir.normalized;
    }

    Vector3 GetLookAheadPoint()
    {
        Vector3 current = transform.position;
        float remaining = Mathf.Max(0.1f, lookAheadDistance);

        for (int i = waypointIndex; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null)
            {
                continue;
            }

            Vector3 segmentEnd = waypoints[i].position;
            Vector3 toEnd = segmentEnd - current;
            toEnd.y = 0f;
            float length = toEnd.magnitude;

            if (length >= remaining)
            {
                return length > 1e-6f ? current + toEnd / length * remaining : segmentEnd;
            }

            remaining -= length;
            current = segmentEnd;
        }

        for (int i = waypoints.Length - 1; i >= 0; i--)
        {
            if (waypoints[i] != null)
            {
                return waypoints[i].position;
            }
        }

        return current;
    }

    // Slows the car down as its steering angle grows.
    float GetCornerSpeedFactor(Vector3 desiredHeading)
    {
        if (cornerSpeedFactor >= 1f)
        {
            return 1f;
        }

        float turn = Vector3.Angle(heading, desiredHeading);
        float amount = Mathf.Clamp01(turn / Mathf.Max(1f, cornerAngle));
        return Mathf.Lerp(1f, cornerSpeedFactor, amount);
    }

    void ApplyRotation()
    {
        if (heading.sqrMagnitude < 1e-6f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(heading, Vector3.up) * Quaternion.Euler(0f, yawOffset, 0f);
    }

    static Vector3 RotateTowardsFlat(Vector3 current, Vector3 desired, float maxDegrees)
    {
        current.y = 0f;
        desired.y = 0f;

        if (current.sqrMagnitude < 1e-6f)
        {
            return desired.sqrMagnitude > 1e-6f ? desired.normalized : Vector3.forward;
        }

        if (desired.sqrMagnitude < 1e-6f)
        {
            return current.normalized;
        }

        Vector3 rotated = Vector3.RotateTowards(current.normalized, desired.normalized, maxDegrees * Mathf.Deg2Rad, 0f);
        return rotated.sqrMagnitude > 1e-6f ? rotated.normalized : desired.normalized;
    }

    // Combines the car's own speed with the segment limit, traffic lights and the gap ahead.
    float GetAllowedSpeed()
    {
        float limit = route != null ? route.GetSpeedLimit(waypointIndex) : 0f;
        float allowed = limit > 0f ? Mathf.Min(speed, limit) : speed;
        allowed = Mathf.Min(allowed, GetTrafficLightSpeed(allowed));
        return Mathf.Min(allowed, GetFollowingSpeed(allowed));
    }

    // Slows the car so it stops at a red or yellow light ahead.
    float GetTrafficLightSpeed(float desiredSpeed)
    {
        if (!obeyTrafficLights)
        {
            return desiredSpeed;
        }

        Vector3 forward = TravelDirection();
        Vector3 position = transform.position;
        float allowed = desiredSpeed;

        IReadOnlyList<TrafficLight> trafficLights = TrafficLight.Lights;
        for (int i = 0; i < trafficLights.Count; i++)
        {
            TrafficLight light = trafficLights[i];
            if (light != null)
            {
                allowed = Mathf.Min(allowed, light.LimitSpeed(position, forward, allowed));
            }
        }

        return allowed;
    }

    // Gap to the nearest car ahead in the same lane, or infinity when the lane is clear.
    float GetNearestGapAhead()
    {
        if (!avoidanceEnabled || route == null)
        {
            return float.PositiveInfinity;
        }

        EnsureSpatialIndex();

        Vector3 forward = TravelDirection();
        Vector3 position = transform.position;
        float nearestGap = float.PositiveInfinity;

        queryBuffer.Clear();
        spatialIndex.Query(position, sensorDistance, queryBuffer);

        for (int i = 0; i < queryBuffer.Count; i++)
        {
            Car other = queryBuffer[i];
            if (other == null || other == this || other.route != route)
            {
                continue;
            }

            Vector3 offset = other.transform.position - position;
            float ahead = Vector3.Dot(offset, forward);
            if (ahead <= 0f || ahead > sensorDistance)
            {
                continue;
            }

            float lateral = Vector3.ProjectOnPlane(offset, forward).magnitude;
            if (lateral > laneWidth)
            {
                continue;
            }

            if (ahead < nearestGap)
            {
                nearestGap = ahead;
            }
        }

        return nearestGap;
    }

    // Speed that still lets the car stop before the gap ahead closes.
    // Safe braking distance gives v = sqrt(2 * braking * freeGap), so the car
    // eases to a stop at safeGap instead of touching the car in front.
    float GetFollowingSpeed(float desiredSpeed)
    {
        if (float.IsPositiveInfinity(nearestGapAhead))
        {
            return desiredSpeed;
        }

        float freeGap = Mathf.Max(0f, nearestGapAhead - safeGap);
        float safeSpeed = Mathf.Sqrt(2f * maxBraking * freeGap);
        return Mathf.Min(desiredSpeed, safeSpeed);
    }

    // Moves to a parallel lane when the current lane is congested and the target lane is clear.
    void UpdateLaneChange()
    {
        if (!laneChangingEnabled || !avoidanceEnabled || route == null || laneChangeTimer > 0f)
        {
            return;
        }

        if (nearestGapAhead > laneChangeLookAhead)
        {
            // Current lane is flowing; nothing to do.
            return;
        }

        WayPoints newRoute;
        int newIndex;
        if (!TryFindLaneChange(out newRoute, out newIndex))
        {
            return;
        }

        if (!IsLaneChangeSafe(newRoute))
        {
            return;
        }

        route = newRoute;
        waypoints = newRoute.Points;
        waypointIndex = Mathf.Clamp(newIndex, 0, waypoints.Length - 1);
        target = waypoints[waypointIndex];
        laneChangeTimer = laneChangeCooldown;
        LaneChanges++;

        // The gap we just measured belonged to the old lane.
        nearestGapAhead = GetNearestGapAhead();
    }

    // Finds the nearest parallel vehicle lane running the same way.
    bool TryFindLaneChange(out WayPoints newRoute, out int newIndex)
    {
        newRoute = null;
        newIndex = 0;

        IReadOnlyList<WayPoints> routes = WayPoints.Routes;
        Vector3 position = transform.position;
        Vector3 forward = TravelDirection();
        float bestLateral = float.PositiveInfinity;

        for (int i = 0; i < routes.Count; i++)
        {
            WayPoints candidate = routes[i];
            if (candidate == null || candidate == route)
            {
                continue;
            }

            if (candidate.kind != WayPoints.RouteKind.Vehicle)
            {
                continue;
            }

            if (candidate.Points == null || candidate.Points.Length < 2)
            {
                continue;
            }

            int index = candidate.GetClosestPointIndex(position);
            if (index < 0 || candidate.Points.Length - index < 2)
            {
                continue;
            }

            Vector3 direction = RouteDirection(candidate, index);
            if (direction.sqrMagnitude < 1e-6f || Vector3.Dot(direction.normalized, forward) < 0.85f)
            {
                // Oncoming or crossing lane: never merge into it.
                continue;
            }

            Vector3 offset = candidate.Points[index].position - position;
            offset.y = 0f;
            float lateral = Vector3.ProjectOnPlane(offset, forward).magnitude;
            if (lateral < 0.5f || lateral > laneChangeSearchRadius || lateral >= bestLateral)
            {
                continue;
            }

            if (Vector3.Dot(offset, forward) < -laneChangeClearance)
            {
                continue;
            }

            bestLateral = lateral;
            newRoute = candidate;
            newIndex = NextIndexAhead(candidate, index, position, forward);
        }

        return newRoute != null;
    }

    // True when nobody already occupies the gap we would merge into.
    bool IsLaneChangeSafe(WayPoints newRoute)
    {
        EnsureSpatialIndex();

        Vector3 forward = TravelDirection();
        Vector3 position = transform.position;

        queryBuffer.Clear();
        spatialIndex.Query(position, laneChangeClearance, queryBuffer);

        for (int i = 0; i < queryBuffer.Count; i++)
        {
            Car other = queryBuffer[i];
            if (other == null || other == this || other.route != newRoute)
            {
                continue;
            }

            Vector3 offset = other.transform.position - position;
            if (Mathf.Abs(Vector3.Dot(offset, forward)) >= laneChangeClearance)
            {
                continue;
            }

            float lateral = Vector3.ProjectOnPlane(offset, forward).magnitude;
            if (lateral <= laneWidth + 0.5f)
            {
                // Someone is already in that spot: abort this change.
                return false;
            }
        }

        return true;
    }

    // Direction of a route around a waypoint index, flattened.
    static Vector3 RouteDirection(WayPoints source, int index)
    {
        Transform[] points = source.Points;
        Vector3 direction;

        if (index + 1 < points.Length && points[index] != null && points[index + 1] != null)
        {
            direction = points[index + 1].position - points[index].position;
        }
        else if (index > 0 && points[index] != null && points[index - 1] != null)
        {
            direction = points[index].position - points[index - 1].position;
        }
        else
        {
            return Vector3.zero;
        }

        direction.y = 0f;
        return direction;
    }

    // First waypoint index ahead of the car along its forward direction.
    static int NextIndexAhead(WayPoints source, int startIndex, Vector3 position, Vector3 forward)
    {
        Transform[] points = source.Points;

        for (int i = startIndex; i < points.Length; i++)
        {
            if (points[i] == null)
            {
                continue;
            }

            Vector3 offset = points[i].position - position;
            offset.y = 0f;
            if (Vector3.Dot(offset, forward) > 0f)
            {
                return i;
            }
        }

        return points.Length - 1;
    }

    // Number of active cars currently following a route.
    public static int CountOnRoute(WayPoints routeFilter)
    {
        int count = 0;
        for (int i = 0; i < registered.Count; i++)
        {
            Car car = registered[i];
            if (car != null && car.route == routeFilter)
            {
                count++;
            }
        }

        return count;
    }

    // True when no active car sits within radius of position (optionally limited to one route).
    public static bool IsAreaClear(WayPoints routeFilter, Vector3 position, float radius)
    {
        EnsureSpatialIndex();

        queryBuffer.Clear();
        spatialIndex.Query(position, radius, queryBuffer);

        for (int i = 0; i < queryBuffer.Count; i++)
        {
            Car car = queryBuffer[i];
            if (car == null)
            {
                continue;
            }

            // Query already filtered by radius; only the lane filter remains.
            if (routeFilter != null && car.route != routeFilter)
            {
                continue;
            }

            return false;
        }

        return true;
    }

    // Prepares the car for a run. Returns false when there is no route to follow.
    bool BeginRoute(WayPoints assignedRoute)
    {
        route = assignedRoute != null
            ? assignedRoute
            : WayPoints.GetClosest(transform.position, WayPoints.RouteKind.Vehicle);

        Transform[] resolved = null;
        if (route != null && route.Points != null && route.Points.Length > 0)
        {
            resolved = route.Points;
        }
        else
        {
            resolved = WayPoints.points;
        }

        if (resolved == null || resolved.Length == 0)
        {
            waypoints = null;
            target = null;
            route = null;
            return false;
        }

        waypoints = resolved;
        waypointIndex = 0;

        if (startAtClosestPoint && route != null)
        {
            waypointIndex = Mathf.Clamp(route.GetClosestPointIndex(transform.position), 0, waypoints.Length - 1);
        }

        target = waypoints[waypointIndex];
        laneChangeTimer = laneChangeCooldown;
        LaneChanges = 0;
        currentSpeed = 0f;
        InitialiseHeading();
        return true;
    }

    // Points the car at its first target (or keeps the current facing when already there).
    void InitialiseHeading()
    {
        Vector3 initial = waypoints[waypointIndex].position - transform.position;
        initial.y = 0f;

        if (initial.sqrMagnitude > 1e-6f)
        {
            heading = initial.normalized;
        }
        else
        {
            heading = transform.forward;
            heading.y = 0f;
            heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.forward;
        }

        ApplyRotation();
    }

    // Advances past every waypoint the car has reached or driven beyond.
    // Passing is measured against the incoming segment direction so a sharp turn
    // does not make the car skip the waypoint it is about to steer toward.
    void AdvanceIfPassed()
    {
        while (waypointIndex < waypoints.Length)
        {
            Vector3 waypoint = waypoints[waypointIndex].position;
            Vector3 toWaypoint = waypoint - transform.position;
            toWaypoint.y = 0f;

            if (toWaypoint.magnitude > arrivalRadius)
            {
                Vector3 passed = transform.position - waypoint;
                passed.y = 0f;

                if (Vector3.Dot(passed, IncomingDirection(waypointIndex)) <= 0f)
                {
                    break;
                }
            }

            waypointIndex++;
        }

        if (waypointIndex >= waypoints.Length)
        {
            ReleaseSelf();
            return;
        }

        target = waypoints[waypointIndex];
    }

    // Direction of the segment leading into a waypoint, falling back to current travel.
    Vector3 IncomingDirection(int index)
    {
        if (index > 0)
        {
            Vector3 dir = waypoints[index].position - waypoints[index - 1].position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f)
            {
                return dir.normalized;
            }
        }

        return TravelDirection();
    }

    void ReleaseSelf()
    {
        target = null;

        if (pool != null)
        {
            pool.Release(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
