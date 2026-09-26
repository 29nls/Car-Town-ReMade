using NUnit.Framework;
using UnityEngine;

// Covers the recycling rules of CarPool: prewarming, the instance cap, reuse and
// the handling of destroyed idle instances.
public class CarPoolTests
{
    private GameObject poolObject;
    private GameObject templateObject;
    private CarPool pool;
    private Car template;

    [SetUp]
    public void SetUp()
    {
        poolObject = new GameObject("CarPoolTests.Pool");
        pool = poolObject.AddComponent<CarPool>();

        templateObject = new GameObject("CarPoolTests.Template");
        template = templateObject.AddComponent<Car>();
    }

    [TearDown]
    public void TearDown()
    {
        if (poolObject != null)
        {
            Object.DestroyImmediate(poolObject);
        }

        if (templateObject != null)
        {
            Object.DestroyImmediate(templateObject);
        }
    }

    [Test]
    public void Configure_PrewarmsIdleInstances()
    {
        pool.Configure(template, 3);

        Assert.AreSame(template, pool.Template);
        Assert.AreEqual(3, pool.IdleCount);
        Assert.AreEqual(0, pool.ActiveCount);
        Assert.AreEqual(3, pool.TotalCount);
        Assert.IsFalse(pool.IsFull);
    }

    [Test]
    public void PrewarmedInstances_StartInactiveAndParentedToThePool()
    {
        pool.Configure(template, 2);

        Car idle = pool.Get();
        pool.Release(idle);

        Assert.IsFalse(idle.gameObject.activeSelf);
        Assert.AreSame(pool.transform, idle.transform.parent);
        Assert.AreEqual(template.name, idle.name);
    }

    [Test]
    public void Configure_WithNegativeValues_KeepsTheCurrentSettings()
    {
        pool.Configure(template, 3);
        pool.Configure(template, -1, 10);

        Assert.AreEqual(3, pool.PrewarmCount);
        Assert.AreEqual(10, pool.MaxSize);
    }

    [Test]
    public void Get_ActivatesAnIdleInstance()
    {
        pool.Configure(template, 2);

        Car car = pool.Get();

        Assert.IsNotNull(car);
        Assert.IsTrue(car.gameObject.activeSelf);
        Assert.AreEqual(1, pool.ActiveCount);
        Assert.AreEqual(1, pool.IdleCount);
        Assert.AreEqual(2, pool.TotalCount);
    }

    [Test]
    public void Get_ReusesInstancesInsteadOfGrowingThePool()
    {
        pool.Configure(template, 2);

        Car car = pool.Get();
        pool.Release(car);
        Car reused = pool.Get();

        Assert.AreSame(car, reused);
        Assert.AreEqual(2, pool.TotalCount);
    }

    [Test]
    public void Get_ReturnsNullOnceThePoolIsFullAndNothingIsIdle()
    {
        pool.Configure(template, 1, 1);

        Car car = pool.Get();

        Assert.IsNotNull(car);
        Assert.IsTrue(pool.IsFull);
        Assert.AreEqual(1, pool.ActiveCount);
        Assert.IsNull(pool.Get(), "a full pool without idle instances must not allocate");
        Assert.AreEqual(1, pool.TotalCount);
    }

    [Test]
    public void Prewarm_IsClampedByTheInstanceCap()
    {
        pool.Configure(template, 5, 2);

        Assert.AreEqual(2, pool.TotalCount);
        Assert.IsTrue(pool.IsFull);
    }

    [Test]
    public void Release_ReturnsTheInstanceToIdle()
    {
        pool.Configure(template, 2);
        Car car = pool.Get();

        pool.Release(car);

        Assert.AreEqual(0, pool.ActiveCount);
        Assert.AreEqual(2, pool.IdleCount);
        Assert.IsFalse(car.gameObject.activeSelf);
    }

    [Test]
    public void Release_IgnoresInstancesThatAreNotActive()
    {
        pool.Configure(template, 1);
        Car car = pool.Get();

        pool.Release(car);
        pool.Release(car);

        Assert.AreEqual(0, pool.ActiveCount);
        Assert.AreEqual(1, pool.IdleCount, "releasing twice must not queue the instance twice");
    }

    [Test]
    public void Release_NullIsIgnored()
    {
        pool.Configure(template, 1);

        Assert.DoesNotThrow(() => pool.Release(null));
        Assert.AreEqual(1, pool.TotalCount);
    }

    [Test]
    public void Get_WithoutTemplate_ReturnsNull()
    {
        Assert.IsNull(pool.Get());
        Assert.AreEqual(0, pool.TotalCount);
    }

    [Test]
    public void Prewarm_WithoutTemplate_DoesNothing()
    {
        pool.Prewarm();

        Assert.AreEqual(0, pool.TotalCount);
    }

    [Test]
    public void Get_SkipsIdleInstancesThatWereDestroyed()
    {
        pool.Configure(template, 2);
        Car destroyedIdle = pool.Get();
        pool.Release(destroyedIdle);
        Object.DestroyImmediate(destroyedIdle.gameObject);

        Car car = pool.Get();

        Assert.IsNotNull(car, "a destroyed idle instance must not be handed out");
        Assert.AreEqual(1, pool.ActiveCount);
        Assert.AreEqual(0, pool.IdleCount);
    }

    [Test]
    public void Get_WithoutIdleInstances_CreatesNewOnesUpToTheCap()
    {
        pool.Configure(template, 0, 3);

        Car first = pool.Get();
        Car second = pool.Get();
        Car third = pool.Get();

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.IsNotNull(third);
        Assert.AreNotSame(first, second);
        Assert.AreEqual(3, pool.TotalCount);
        Assert.AreEqual(3, pool.ActiveCount);
    }
}
