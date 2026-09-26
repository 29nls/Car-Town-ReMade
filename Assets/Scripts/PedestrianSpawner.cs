using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PedestrianSpawner : MonoBehaviour
{
    [Header("Template")]
    public Pedestrian prefab;
    public Transform spawnPoint;

    [Header("Pool")]
    public int prewarmCount = 6;
    public int maxActive = 20;

    [Header("Waves")]
    public float timeBetweenWaves = 6f;
    public float timeBetweenSpawnsInWave = 0.5f;
    public int pedestriansPerWave = 2;
    public int pedestriansAddedPerWave = 1;
    public int maxPerWave = 6;

    [Header("Crossing")]
    // Light assigned to spawned pedestrians. When empty, the nearest one is used.
    public TrafficLight crossingLight;

    private readonly Queue<Pedestrian> idle = new Queue<Pedestrian>();
    private readonly HashSet<Pedestrian> active = new HashSet<Pedestrian>();

    private float countdown = 2f;
    private int waveIndex = 0;
    private int routeCursor = 0;

    public int ActiveCount
    {
        get { return active.Count; }
    }

    private int TotalCount
    {
        get { return active.Count + idle.Count; }
    }

    private void Awake()
    {
        Prewarm();
    }

    private void Start()
    {
        countdown = 2f;
    }

    public void Release(Pedestrian pedestrian)
    {
        if (pedestrian == null)
        {
            return;
        }

        if (!active.Remove(pedestrian))
        {
            return;
        }

        pedestrian.gameObject.SetActive(false);
        pedestrian.transform.SetParent(transform, false);
        idle.Enqueue(pedestrian);
    }

    private void Prewarm()
    {
        if (prefab == null)
        {
            return;
        }

        while (TotalCount < prewarmCount)
        {
            Pedestrian pedestrian = Create();
            if (pedestrian == null)
            {
                return;
            }

            idle.Enqueue(pedestrian);
        }
    }

    private Pedestrian Create()
    {
        if (prefab == null)
        {
            return null;
        }

        Pedestrian pedestrian = Instantiate(prefab, transform);
        pedestrian.name = prefab.name;
        pedestrian.Init(this);
        pedestrian.gameObject.SetActive(false);
        return pedestrian;
    }

    private Pedestrian Get()
    {
        Pedestrian pedestrian = null;
        while (pedestrian == null && idle.Count > 0)
        {
            pedestrian = idle.Dequeue();
        }

        if (pedestrian == null)
        {
            pedestrian = Create();
        }

        if (pedestrian == null)
        {
            return null;
        }

        pedestrian.gameObject.SetActive(true);
        active.Add(pedestrian);
        return pedestrian;
    }

    private void Update()
    {
        countdown -= Time.deltaTime;
        if (countdown > 0f)
        {
            return;
        }

        countdown = timeBetweenWaves;

        if (prefab == null || ActiveCount >= maxActive)
        {
            return;
        }

        StartCoroutine(SpawnWave());
    }

    private IEnumerator SpawnWave()
    {
        int cap = Mathf.Max(1, maxPerWave);
        int pedestriansThisWave = Mathf.Clamp(pedestriansPerWave + pedestriansAddedPerWave * waveIndex, 1, cap);
        waveIndex++;

        for (int i = 0; i < pedestriansThisWave; i++)
        {
            if (prefab == null || ActiveCount >= maxActive)
            {
                yield break;
            }

            SpawnOne();
            yield return new WaitForSeconds(timeBetweenSpawnsInWave);
        }
    }

    private void SpawnOne()
    {
        Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position;
        WayPoints route = ResolveRoute(position);
        if (route == null)
        {
            return;
        }

        Pedestrian pedestrian = Get();
        if (pedestrian == null)
        {
            return;
        }

        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;
        pedestrian.transform.SetPositionAndRotation(position, rotation);
        pedestrian.Spawn(route, ResolveLight(position));
    }

    private WayPoints ResolveRoute(Vector3 origin)
    {
        IReadOnlyList<WayPoints> routes = WayPoints.Routes;
        if (routes == null || routes.Count == 0)
        {
            return null;
        }

        for (int i = 0; i < routes.Count; i++)
        {
            WayPoints candidate = routes[routeCursor % routes.Count];
            routeCursor++;

            if (candidate != null
                && candidate.kind == WayPoints.RouteKind.Pedestrian
                && candidate.Points != null
                && candidate.Points.Length > 0)
            {
                return candidate;
            }
        }

        return null;
    }

    private TrafficLight ResolveLight(Vector3 origin)
    {
        if (crossingLight != null)
        {
            return crossingLight;
        }

        return TrafficLight.GetClosest(origin);
    }
}
