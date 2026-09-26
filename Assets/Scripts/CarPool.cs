using System.Collections.Generic;
using UnityEngine;

// Recycles car instances so the traffic system never pays for
// Instantiate/Destroy while it is running, and keeps the pool bounded.
public class CarPool : MonoBehaviour
{
    [Tooltip("Object new instances are cloned from. Assigned by CarSpawner.")]
    [SerializeField] private Car template;
    [Tooltip("Instances created up front so spawning never allocates during play.")]
    [SerializeField] private int prewarmCount = 4;
    [Tooltip("Hard cap on total instances. 0 or less means unlimited.")]
    [SerializeField] private int maxSize = 32;

    private readonly Queue<Car> idle = new Queue<Car>();
    private readonly HashSet<Car> active = new HashSet<Car>();

    public Car Template { get { return template; } }
    public int ActiveCount { get { return active.Count; } }
    public int IdleCount { get { return idle.Count; } }
    public int TotalCount { get { return active.Count + idle.Count; } }
    public int PrewarmCount { get { return prewarmCount; } }
    public int MaxSize { get { return maxSize; } }

    // True when no more instances may be created.
    public bool IsFull
    {
        get { return maxSize > 0 && TotalCount >= maxSize; }
    }

    private void Awake()
    {
        Prewarm();
    }

    private void OnValidate()
    {
        prewarmCount = Mathf.Max(0, prewarmCount);
        maxSize = Mathf.Max(0, maxSize);

        if (maxSize > 0 && prewarmCount > maxSize)
        {
            prewarmCount = maxSize;
        }
    }

    // Sets the template and, optionally, the pool sizing, then prepares the initial batch.
    // Pass a negative value to keep a setting unchanged.
    public void Configure(Car carTemplate, int prewarm = -1, int maxInstances = -1)
    {
        template = carTemplate;

        if (prewarm >= 0)
        {
            prewarmCount = prewarm;
        }

        if (maxInstances >= 0)
        {
            maxSize = maxInstances;
        }

        Prewarm();
    }

    public void Prewarm()
    {
        if (template == null)
        {
            return;
        }

        int target = prewarmCount;
        if (maxSize > 0 && target > maxSize)
        {
            target = maxSize;
        }

        while (TotalCount < target)
        {
            Car car = CreateInstance();
            if (car == null)
            {
                return;
            }

            idle.Enqueue(car);
        }
    }

    // Returns an active car, reusing an idle one when possible.
    // Returns null once the pool is full and nothing is idle.
    public Car Get()
    {
        Car car = null;
        while (car == null && idle.Count > 0)
        {
            car = idle.Dequeue();
        }

        if (car == null)
        {
            if (IsFull)
            {
                return null;
            }

            car = CreateInstance();
        }

        if (car == null)
        {
            return null;
        }

        car.gameObject.SetActive(true);
        active.Add(car);
        return car;
    }

    // Puts a car back so it can be reused later.
    public void Release(Car car)
    {
        if (car == null)
        {
            return;
        }

        if (!active.Remove(car))
        {
            return;
        }

        car.gameObject.SetActive(false);
        car.transform.SetParent(transform, false);
        idle.Enqueue(car);
    }

    private Car CreateInstance()
    {
        if (template == null)
        {
            return null;
        }

        Car car = Instantiate(template, transform);
        car.name = template.name;
        car.Init(this);
        car.gameObject.SetActive(false);
        return car;
    }
}
