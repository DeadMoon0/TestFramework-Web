using System;
using System.Globalization;
using System.Net.Http;
using TestFramework.Web.Runtime;
using Xunit;

namespace TestFramework.Web.Tests;

/// <summary>
/// Covers the bound on the client pool, which is what keeps a long-lived host from accumulating one
/// client per run.
/// </summary>
public class HttpClientPoolTests
{
    [Fact]
    public void TheSameKey_ReusesOneClient()
    {
        HttpClientPool pool = new(4);

        HttpClient first = pool.GetOrAdd("a", () => new HttpClient());
        HttpClient second = pool.GetOrAdd("a", () => new HttpClient());

        Assert.Same(first, second);
        Assert.Equal(1, pool.Count);
    }

    [Fact]
    public void PastTheCapacity_TheOldestClientIsEvictedAndDisposed()
    {
        HttpClientPool pool = new(2);

        HttpClient oldest = pool.GetOrAdd("a", () => new HttpClient());
        pool.GetOrAdd("b", () => new HttpClient());
        pool.GetOrAdd("c", () => new HttpClient());

        Assert.Equal(2, pool.Count);
        Assert.Throws<ObjectDisposedException>(() => oldest.Timeout = TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AReusedClient_IsNotTheOneEvicted()
    {
        HttpClientPool pool = new(2);

        HttpClient kept = pool.GetOrAdd("a", () => new HttpClient());
        pool.GetOrAdd("b", () => new HttpClient());

        // Touching "a" makes "b" the least recently used one.
        pool.GetOrAdd("a", () => new HttpClient());
        pool.GetOrAdd("c", () => new HttpClient());

        Assert.Same(kept, pool.GetOrAdd("a", () => new HttpClient()));
    }

    [Fact]
    public void ManyDistinctEndpoints_DoNotGrowThePoolWithoutBound()
    {
        // One key per run is what a container lane produces: a new ephemeral port every time.
        HttpClientPool pool = new(8);

        for (int run = 0; run < 200; run++)
            pool.GetOrAdd(run.ToString(CultureInfo.InvariantCulture), () => new HttpClient());

        Assert.Equal(8, pool.Count);
    }
}
