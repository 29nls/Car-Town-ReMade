using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// Covers the bucketing, radius filtering and reuse behaviour of SpatialHash,
// which the traffic system relies on for neighbour lookups.
public class SpatialHashTests
{
    private const float CellSize = 5f;

    private readonly List<Transform> spawned = new List<Transform>();

    private Transform CreateAt(float x, float z)
    {
        GameObject go = new GameObject("SpatialHashTests");
        go.transform.position = new Vector3(x, 0f, z);
        spawned.Add(go.transform);
        return go.transform;
    }

    private static List<Transform> Query(SpatialHash<Transform> hash, Vector3 center, float radius)
    {
        List<Transform> results = new List<Transform>();
        hash.Query(center, radius, results);
        return results;
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null)
            {
                Object.DestroyImmediate(spawned[i].gameObject);
            }
        }

        spawned.Clear();
    }

    [Test]
    public void Insert_PlacesNearbyItemsInTheSameCell()
    {
        SpatialHash<Transform> hash = new SpatialHash<Transform>(CellSize);
        hash.Insert(CreateAt(0f, 0f));
        hash.Insert(CreateAt(1f, 1f));

        Assert.AreEqual(1, hash.CellCount);
    }

    [Test]
    public void Insert_PlacesDistantItemsInSeparateCells()
    {
        SpatialHash<Transform> hash = new SpatialHash<Transform>(CellSize);
        hash.Insert(CreateAt(0f, 0f));
        hash.Insert(CreateAt(1000f, 1000f));

        Assert.AreEqual(2, hash.CellCount);
    }

    [Test]
    public void Query_ReturnsOnlyItemsInsideTheRadius()
    {
        SpatialHash<Transform> hash = new SpatialHash<Transform>(CellSize);
        Transform near = CreateAt(1f, 1f);
        hash.Insert(near);
        hash.Insert(CreateAt(20f, 20f));

        List<Transform> results = Query(hash, Vector3.zero, 4f);

        Assert.AreEqual(1, results.Count);
        Assert.AreSame(near, results[0]);
    }

    [Test]
    public void Query_IncludesItemsExactlyOnTheRadius()
    {
        SpatialHash<Transform> hash = new SpatialHash<Transform>(CellSize);
        Transform edge = CreateAt(3f, 0f);
        hash.Insert(edge);

        Assert.AreEqual(1, Query(hash, Vector3.zero, 3f).Count, "an item exactly on the radius counts as inside");
        Assert.AreEqual(0, Query(hash, Vector3.zero, 2.9f).Count);
    }

    [Test]
    public void Query_SpansEveryCellTouchedByTheRadius()
    {
        SpatialHash<Transform> hash = new SpatialHash<Transform>(2f);
        hash.Insert(CreateAt(0f, 0f));
        hash.Insert(CreateAt(4f, 0f));
        hash.Insert(CreateAt(8f, 0f));
        hash.Insert(CreateAt(40f, 40f));

        Assert.AreEqual(3, Query(hash, new Vector3(4f, 0f, 0f), 10f).Count);
    }

    [Test]
    public void Query_AppendsToTheResultsList()
    {
        SpatialHash<Transform> hash = new SpatialHash<Transform>(CellSize);
        hash.Insert(CreateAt(0f, 0f));

        List<Transform> results = new List<Transform>();
        results.Add(CreateAt(500f, 500f));
        hash.Query(Vector3.zero, CellSize, results);

        Assert.AreEqual(2, results.Count, "Query must not clear the list it appends to");
    }

    [Test]
    public void Query_OnEmptyHash_ReturnsNothing()
    {
        SpatialHash<Transform> hash = new SpatialHash<Transform>(CellSize);

        Assert.AreEqual(0, Query(hash, Vector3.zero, 100f).Count);
    }

    [Test]
    public void Query_FindsItemsAtNegativeCoordinates()
    {
        SpatialHash<Transform> hash = new SpatialHash<Transform>(CellSize);
        Transform negative = CreateAt(-12f, -7f);
        hash.Insert(negative);

        List<Transform> results = Query(hash, new Vector3(-12f, 0f, -7f), 0.5f);

        Assert.AreEqual(1, results.Count);
        Assert.AreSame(negative, results[0]);
        Assert.AreEqual(0, Query(hash, new Vector3(12f, 0f, 7f), 0.5f).Count);
    }

    [Test]
    public void Clear_EmptiesCellsButKeepsThem()
    {
        SpatialHash<Transform> hash = new SpatialHash<Transform>(CellSize);
        hash.Insert(CreateAt(0f, 0f));

        hash.Clear();

        Assert.AreEqual(1, hash.CellCount, "cells are reused between frames");
        Assert.AreEqual(0, Query(hash, Vector3.zero, CellSize).Count);
    }

    [Test]
    public void Query_SkipsItemsDestroyedAfterInsert()
    {
        SpatialHash<Transform> hash = new SpatialHash<Transform>(CellSize);
        Transform doomed = CreateAt(0f, 0f);
        hash.Insert(doomed);

        Object.DestroyImmediate(doomed.gameObject);

        Assert.AreEqual(0, Query(hash, Vector3.zero, CellSize).Count);
    }

    [Test]
    public void Constructor_ClampsNonPositiveCellSizes()
    {
        SpatialHash<Transform> hash = new SpatialHash<Transform>(0f);
        hash.Insert(CreateAt(0f, 0f));
        hash.Insert(CreateAt(0.005f, 0.005f));

        Assert.AreEqual(1, hash.CellCount, "cells must never collapse to a zero size");
        Assert.AreEqual(2, Query(hash, Vector3.zero, 1f).Count);

        SpatialHash<Transform> negative = new SpatialHash<Transform>(-4f);
        negative.Insert(CreateAt(0f, 0f));

        Assert.AreEqual(1, negative.CellCount);
    }
}
