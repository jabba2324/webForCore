using Xunit;

namespace SessionCacheGate
{
    // Microsoft.Web.RedisOutputCacheProvider 4.0.1 as <caching><outputCache defaultProvider>, with System.Web's
    // OutputCacheModule, on two instances with the site in a folder each.
    [Collection(SessionCacheCollection.Name)]
    public sealed class RedisOutputCacheTests : IClassFixture<RedisPair>
    {
        private readonly RedisPair _pair;

        public RedisOutputCacheTests(RedisPair pair)
        {
            _pair = pair;
        }

        // <%@ OutputCache Duration="4" VaryByParam="none" %>: rendered on one instance, served from Redis by the other
        // until it expires; then rendered again, and that is served to both.
        [Fact]
        public async Task APageCachedOnOneInstanceIsServedByTheOtherUntilItExpires()
        {
            await Task.Delay(TimeSpan.FromSeconds(5)); // an entry of an earlier test or run expires
            var first = (await Page.Get(_pair.A, "/OutputCache.aspx?id=1")).Ok();
            Assert.Equal("A", first["instance"]);
            Assert.Equal("MyRedisOutputCache", first["provider"]);

            var cached = (await Page.Get(_pair.B, "/OutputCache.aspx?id=2")).Ok();
            Assert.Equal("A", cached["instance"]);
            Assert.Equal("1", cached["id"]);
            Assert.Equal(first["rendered"], cached["rendered"]);

            await Task.Delay(TimeSpan.FromSeconds(5));
            var later = (await Page.Get(_pair.B, "/OutputCache.aspx?id=3")).Ok();
            Assert.Equal("B", later["instance"]);
            Assert.NotEqual(first["rendered"], later["rendered"]);
            var laterOnA = (await Page.Get(_pair.A, "/OutputCache.aspx")).Ok();
            Assert.Equal(later["rendered"], laterOnA["rendered"]);
        }

        // VaryByParam="id": RedisOutputCacheProvider 4.0.1's Add casts the RedisResult of its script to byte[]
        // (castclass, InvalidCastException) where 3.x converted it: Add fails, after storing the page's CachedVary the
        // first time, and the provider returns null. System.Web then stores each response with the CachedVary of its own
        // request, whose ID isn't the stored one's, so no response is found again: once the CachedVary is in Redis,
        // every request renders the page. The same assembly does the same on IIS. (3.0.1 converts the result, but
        // serializes with .NET's BinaryFormatter, which throws on .NET.)
        [Fact]
        public async Task WithVaryByParamTheProviderFindsNothing()
        {
            (await Page.Get(_pair.A, "/OutputCacheVary.aspx?id=first")).Ok(); // the CachedVary, if it isn't there yet
            string id = Guid.NewGuid().ToString("N");
            var first = (await Page.Get(_pair.A, "/OutputCacheVary.aspx?id=" + id)).Ok();
            var again = (await Page.Get(_pair.A, "/OutputCacheVary.aspx?id=" + id)).Ok();
            var other = (await Page.Get(_pair.B, "/OutputCacheVary.aspx?id=" + id)).Ok();
            Assert.NotEqual(first["rendered"], again["rendered"]);
            Assert.Equal("B", other["instance"]);
        }

        // The providers' assemblies load with what they depend on: CosmosDBSessionStateProviderAsync (built, but there is
        // no Cosmos DB here), SqlSessionStateProviderAsync and the Redis providers.
        [Fact]
        public async Task TheProvidersLoad()
        {
            var page = (await Page.Get(_pair.A, "/Types.aspx"
                + "?type=Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync, Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync"
                + "&type=Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync, Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync"
                + "&type=Microsoft.Web.Redis.RedisSessionStateProvider, Microsoft.Web.RedisSessionStateProvider"
                + "&type=Microsoft.Web.Redis.RedisOutputCacheProvider, Microsoft.Web.RedisOutputCacheProvider")).Ok();
            Assert.Contains("Microsoft.AspNet.SessionState.CosmosDBSessionStateProviderAsync: Microsoft.AspNet.SessionState.SessionStateStoreProviderAsyncBase\n", page.Body);
            Assert.Contains("Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync: Microsoft.AspNet.SessionState.SessionStateStoreProviderAsyncBase\n", page.Body);
            Assert.Contains("Microsoft.Web.Redis.RedisSessionStateProvider: Microsoft.AspNet.SessionState.SessionStateStoreProviderAsyncBase\n", page.Body);
            Assert.Contains("Microsoft.Web.Redis.RedisOutputCacheProvider: System.Web.Caching.OutputCacheProviderAsync\n", page.Body);
        }
    }

    public sealed class RedisOutputCacheModuleAsyncPair : Pair
    {
        protected override string ConfigName => "RedisOutputCacheModuleAsync.config";

        protected override string Replace(string config) => config.Replace("%REDIS%", Stores.Redis);
    }

    // With the output cache module swap that Microsoft.AspNet.OutputCache.OutputCacheModuleAsync 1.0.2 (a dependency of
    // RedisOutputCacheProvider 4.0.1) adds, the module hands the provider its own entry types, which System.Web's
    // OutputCache.Serialize refuses (only System.Web's own types are accepted, as in .NET Framework 4.8): the provider
    // stores nothing and every request renders the page, as on IIS. docs/MIGRATING.md says to remove the swap.
    [Collection(SessionCacheCollection.Name)]
    public sealed class RedisOutputCacheModuleAsyncTests : IClassFixture<RedisOutputCacheModuleAsyncPair>
    {
        private readonly RedisOutputCacheModuleAsyncPair _pair;

        public RedisOutputCacheModuleAsyncTests(RedisOutputCacheModuleAsyncPair pair)
        {
            _pair = pair;
        }

        [Fact]
        public async Task NothingIsCached()
        {
            await Task.Delay(TimeSpan.FromSeconds(5)); // an entry of an earlier test expires
            var first = (await Page.Get(_pair.A, "/OutputCache.aspx")).Ok();
            var second = (await Page.Get(_pair.A, "/OutputCache.aspx")).Ok();
            var other = (await Page.Get(_pair.B, "/OutputCache.aspx")).Ok();
            Assert.NotEqual(first["rendered"], second["rendered"]);
            Assert.Equal("B", other["instance"]);
        }
    }
}
