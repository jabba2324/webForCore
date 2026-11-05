using System;
using System.Collections.Concurrent;
using System.Web.Caching;

// RegressionTests (#184): an output cache provider kept in memory, as an application's own provider (a distributed
// cache's) is: ASP.NET hands it the response as OutputCacheEntry's byte arrays (OutputCache.Convert).
public class GateOutputCacheProvider : OutputCacheProvider
{
    static readonly ConcurrentDictionary<string, Tuple<object, DateTime>> Entries = new ConcurrentDictionary<string, Tuple<object, DateTime>>();

    public override object Get(string key)
    {
        Tuple<object, DateTime> entry;
        if (!Entries.TryGetValue(key, out entry)) return null;
        if (entry.Item2 > DateTime.UtcNow) return entry.Item1;
        Entries.TryRemove(key, out entry);
        return null;
    }

    public override object Add(string key, object entry, DateTime utcExpiry)
    {
        return Entries.GetOrAdd(key, Tuple.Create(entry, utcExpiry)).Item1;
    }

    public override void Set(string key, object entry, DateTime utcExpiry)
    {
        Entries[key] = Tuple.Create(entry, utcExpiry);
    }

    public override void Remove(string key)
    {
        Tuple<object, DateTime> entry;
        Entries.TryRemove(key, out entry);
    }
}
