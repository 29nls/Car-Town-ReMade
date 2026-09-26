using System.Collections.Generic;
using UnityEngine;

// Minimal XZ (ground plane) spatial hash for any Component with a Transform.
// Actors are bucketed into grid cells so neighbour lookups only touch nearby cells.
public class SpatialHash<T> where T : Component
{
    private const int InitialBucketCapacity = 4;

    private readonly float cellSize;
    private readonly Dictionary<long, List<T>> cells = new Dictionary<long, List<T>>();

    public SpatialHash(float cellSize)
    {
        this.cellSize = Mathf.Max(0.01f, cellSize);
    }

    // Number of grid cells currently tracked. Useful for debugging.
    public int CellCount
    {
        get { return cells.Count; }
    }

    // Empties every bucket but keeps the lists so no allocations happen per frame.
    public void Clear()
    {
        foreach (KeyValuePair<long, List<T>> pair in cells)
        {
            pair.Value.Clear();
        }
    }

    public void Insert(T item)
    {
        Vector3 position = item.transform.position;
        long key = Key(CellIndex(position.x), CellIndex(position.z));

        if (!cells.TryGetValue(key, out List<T> bucket))
        {
            bucket = new List<T>(InitialBucketCapacity);
            cells[key] = bucket;
        }

        bucket.Add(item);
    }

    // Adds every item within radius of center to results. Results are not cleared first.
    public void Query(Vector3 center, float radius, List<T> results)
    {
        float sqrRadius = radius * radius;
        int minX = CellIndex(center.x - radius);
        int maxX = CellIndex(center.x + radius);
        int minZ = CellIndex(center.z - radius);
        int maxZ = CellIndex(center.z + radius);

        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                if (!cells.TryGetValue(Key(x, z), out List<T> bucket))
                {
                    continue;
                }

                for (int i = 0; i < bucket.Count; i++)
                {
                    T item = bucket[i];
                    if (item == null)
                    {
                        continue;
                    }

                    if ((item.transform.position - center).sqrMagnitude <= sqrRadius)
                    {
                        results.Add(item);
                    }
                }
            }
        }
    }

    private int CellIndex(float coordinate)
    {
        return Mathf.FloorToInt(coordinate / cellSize);
    }

    private static long Key(int x, int z)
    {
        return ((long)x << 32) ^ (uint)z;
    }
}
