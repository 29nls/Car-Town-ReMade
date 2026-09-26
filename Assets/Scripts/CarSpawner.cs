using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CarSpawner : MonoBehaviour
{
    // How the spawner chooses which lane a new car joins.
    public enum RouteSelection
    {
        // Closest lane to the spawn point (previous behaviour).
        Nearest,
        // Cycle through every lane in turn.
        RoundRobin,
        // Prefer the lane with the fewest active cars.
        LeastCongested
    }

    [Header("Template")]
    public Transform CarPrefab;
    public Transform spawnPoint;

    [Header("Waves")]
    public float timeBetweenCars = 10f;
    public float timeBetweenCarsInWave = 1f;
    public int carsPerWave = 2;
    public int carsAddedPerWave = 1;
    public int maxCarsPerWave = 8;

    [Header("Density")]
    public int maxActiveCars = 24;
    public bool slowDownWhenCongested = true;
    public float congestedIntervalMultiplier = 2f;

    [Header("Routing")]
    public RouteSelection routeSelection = RouteSelection.Nearest;
    public bool joinRouteAtClosestPoint = true;
    // Skip a spawn when another car is this close to the chosen lane entrance.
    public float minSpawnClearance = 5f;

    [Header("Pool")]
    public CarPool pool;
    [Tooltip("Car instances created up front so spawning never allocates during play.")]
    public int prewarmCars = 4;
    [Tooltip("Hard cap on total pooled instances to keep memory bounded. 0 means unlimited.")]
    public int maxPoolSize = 32;

    private float countdown = 3f;
    private int waveIndex = 0;
    private int routeCursor = 0;

    private void Awake()
    {
        SetUpPool();
    }

    private void Start()
    {
        countdown = 3f;
    }

    private void OnValidate()
    {
        prewarmCars = Mathf.Max(0, prewarmCars);
        maxPoolSize = Mathf.Max(0, maxPoolSize);
        if (maxPoolSize > 0 && prewarmCars > maxPoolSize)
        {
            prewarmCars = maxPoolSize;
        }

        maxActiveCars = Mathf.Max(1, maxActiveCars);
    }

    private void SetUpPool()
    {
        if (pool == null)
        {
            pool = GetComponent<CarPool>();
        }

        if (pool == null)
        {
            pool = gameObject.AddComponent<CarPool>();
        }

        if (CarPrefab == null)
        {
            return;
        }

        Car template = CarPrefab.GetComponent<Car>();
        if (template == null)
        {
            template = CarPrefab.GetComponentInChildren<Car>();
        }

        pool.Configure(template, prewarmCars, maxPoolSize);

        // The scene object used as a template must stay parked so it never drives off.
        if (template != null && template.gameObject.scene.IsValid())
        {
            template.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        countdown -= Time.deltaTime;
        if (countdown > 0f)
        {
            return;
        }

        countdown = GetWaveInterval();

        if (pool == null || pool.ActiveCount >= maxActiveCars)
        {
            return;
        }

        StartCoroutine(SpawnWave());
    }

    private float GetWaveInterval()
    {
        if (!slowDownWhenCongested || maxActiveCars <= 0 || pool == null)
        {
            return timeBetweenCars;
        }

        float occupancy = Mathf.Clamp01((float)pool.ActiveCount / maxActiveCars);
        return Mathf.Lerp(timeBetweenCars, timeBetweenCars * congestedIntervalMultiplier, occupancy);
    }

    private IEnumerator SpawnWave()
    {
        int cap = Mathf.Max(1, maxCarsPerWave);
        int carsThisWave = Mathf.Clamp(carsPerWave + carsAddedPerWave * waveIndex, 1, cap);
        waveIndex++;

        for (int i = 0; i < carsThisWave; i++)
        {
            if (pool == null || pool.ActiveCount >= maxActiveCars)
            {
                yield break;
            }

            SpawnCar();
            yield return new WaitForSeconds(timeBetweenCarsInWave);
        }
    }

    private void SpawnCar()
    {
        if (pool == null)
        {
            return;
        }

        Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position;
        WayPoints route = ResolveRoute(position);
        if (route == null)
        {
            return;
        }

        if (minSpawnClearance > 0f && !Car.IsAreaClear(route, position, minSpawnClearance))
        {
            // The lane entrance is still busy; try again on the next wave tick.
            return;
        }

        Car car = pool.Get();
        if (car == null)
        {
            return;
        }

        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;
        car.transform.SetPositionAndRotation(position, rotation);
        car.Spawn(route, joinRouteAtClosestPoint);
    }

    private WayPoints ResolveRoute(Vector3 origin)
    {
        IReadOnlyList<WayPoints> routes = WayPoints.Routes;
        if (routes == null || routes.Count == 0)
        {
            return null;
        }

        switch (routeSelection)
        {
            case RouteSelection.RoundRobin:
                return PickRoundRobin(routes);
            case RouteSelection.LeastCongested:
                return PickLeastCongested(routes, origin);
            default:
                return WayPoints.GetClosest(origin, WayPoints.RouteKind.Vehicle);
        }
    }

    private static bool IsVehicleRoute(WayPoints route)
    {
        return route != null
            && route.kind == WayPoints.RouteKind.Vehicle
            && route.Points != null
            && route.Points.Length > 0;
    }

    private WayPoints PickRoundRobin(IReadOnlyList<WayPoints> routes)
    {
        for (int i = 0; i < routes.Count; i++)
        {
            WayPoints candidate = routes[routeCursor % routes.Count];
            routeCursor++;

            if (IsVehicleRoute(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private WayPoints PickLeastCongested(IReadOnlyList<WayPoints> routes, Vector3 origin)
    {
        WayPoints best = null;
        int bestCount = int.MaxValue;
        float bestSqrDistance = float.PositiveInfinity;

        for (int i = 0; i < routes.Count; i++)
        {
            WayPoints route = routes[i];
            if (!IsVehicleRoute(route))
            {
                continue;
            }

            int count = Car.CountOnRoute(route);
            float sqrDistance = route.Points[0] != null
                ? (route.Points[0].position - origin).sqrMagnitude
                : 0f;

            if (count < bestCount || (count == bestCount && sqrDistance < bestSqrDistance))
            {
                bestCount = count;
                bestSqrDistance = sqrDistance;
                best = route;
            }
        }

        return best;
    }
}
