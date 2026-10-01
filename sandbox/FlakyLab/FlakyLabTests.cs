using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;

namespace FlakyLab;

// ---------------- Production code (deliberately imperfect) ----------------

public class QuoteService
{
    private static readonly Random Rng = new();

    public async Task<decimal> GetMidAsync(string ccyPair)
    {
        await Task.Delay(Rng.Next(5, 40)); // simulated network latency
        return 1.0850m;
    }
}

public static class RateCache
{
    public static readonly ConcurrentDictionary<string, decimal> Rates = new();
}

public class TradeBlotter
{
    private readonly List<string> _published = new();

    public IReadOnlyList<string> Published
    {
        get { lock (_published) return _published.ToList(); }
    }

    public async Task PublishAsync(string tradeId)
    {
        await Task.Delay(Random.Shared.Next(0, 20)); // simulated bus latency
        lock (_published) _published.Add(tradeId);
    }

    public void PublishFireAndForget(string tradeId) => _ = PublishAsync(tradeId);
}

public class NotionalGenerator
{
    public decimal Next() => Random.Shared.Next(1, 100) * 100_000m;
}

// ---------------- Tests ----------------

public class QuoteServiceTests
{
    [Fact]
    public async Task GetMid_returns_within_sla()
    {
        var sw = Stopwatch.StartNew();
        await new QuoteService().GetMidAsync("EURUSD");
        Assert.True(sw.ElapsedMilliseconds < 30, $"Took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Mid_is_positive()
    {
        var mid = await new QuoteService().GetMidAsync("EURUSD");
        Assert.True(mid > 0);
    }
}

public class RateCacheWriterTests
{
    [Fact]
    public async Task Writer_sets_eurusd()
    {
        RateCache.Rates["EURUSD"] = 1.10m;
        await Task.Delay(25);
        Assert.Equal(1.10m, RateCache.Rates["EURUSD"]);
    }
}

public class RateCacheResetTests
{
    [Fact]
    public async Task Reset_clears_cache()
    {
        RateCache.Rates.Clear();
        await Task.Delay(25);
        Assert.Empty(RateCache.Rates);
    }
}

public class TradeBlotterTests
{
    [Fact]
    public async Task Publish_adds_trade_to_blotter()
    {
        var blotter = new TradeBlotter();
        blotter.PublishFireAndForget("T1");
        await Task.Delay(10);
        Assert.Contains("T1", blotter.Published);
    }
}

public class NotionalGeneratorTests
{
    [Fact]
    public void Generated_notional_is_at_least_1m()
    {
        Assert.True(new NotionalGenerator().Next() >= 1_000_000m);
    }
}
