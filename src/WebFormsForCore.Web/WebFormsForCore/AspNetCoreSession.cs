using System.IO;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace System.Web.SessionState;

/// <summary>
/// Out-of-process session state in ASP.NET Core's distributed cache (<see cref="IDistributedCache"/>: in memory, Redis,
/// SQL Server, ...), registered by services.AddDistributedMemoryCache() (or AddStackExchangeRedisCache, ...). It works
/// like IIS's out-of-process modes (StateServer, SQLServer): one entry per System.Web session ID, which ends
/// <c>Session.Timeout</c> minutes after it was last used. The session ID is System.Web's (SessionIDManager), so the only
/// cookie is <c>&lt;sessionState cookieName&gt;</c> (ASP.NET_SessionId by default).
/// ASP.NET Core's own session (ISession, AddSession()/UseSession() and its .AspNetCore.Session cookie) isn't used (#79).
/// </summary>
public class AspNetCoreSessionProvider : SessionStateStoreProviderBase
{
    private const string SessionStateKeyPrefix = "WebFormsForCore.SessionState.";

    private static string GetSessionStateKey(string id) => SessionStateKeyPrefix + id;

    private static IDistributedCache Cache(HttpContext context) =>
        context.CoreContext.RequestServices.GetService<IDistributedCache>()
            ?? throw new InvalidOperationException("Session state through UseAspNetCoreSessionProvider() needs ASP.NET Core's distributed cache: " +
                "add services.AddDistributedMemoryCache() (or AddStackExchangeRedisCache, AddDistributedSqlServerCache, ...) to Program.cs.");

    public override void Dispose()
    {
    }

    public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
    {
        // As in IIS's out-of-process modes, there is no Session_End: the cache doesn't say when an entry expires.
        return false;
    }

    public override void InitializeRequest(HttpContext context)
    {
    }

    public override void EndRequest(HttpContext context)
    {
    }

    public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
    {
        return DoGet(context, id, out locked, out lockAge, out lockId, out actions);
    }

    public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
    {
        return DoGet(context, id, out locked, out lockAge, out lockId, out actions);
    }

    private static SessionStateStoreData DoGet(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
    {
        locked = false;
        lockAge = TimeSpan.Zero;
        lockId = null;
        actions = SessionStateActions.None;

        // Reading the entry restarts its sliding expiration.
        byte[] buffer = Cache(context).Get(GetSessionStateKey(id));
        if (buffer == null)
        {
            return null;
        }

        using var stream = new MemoryStream(buffer);
        return SessionStateUtility.DeserializeStoreData(context, stream, false);
    }

    public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
    {
    }

    public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
    {
        SessionStateUtility.SerializeStoreData(item, 4096, out byte[] buffer, out int length, false);

        byte[] bytes = buffer;
        if (length != buffer.Length)
        {
            bytes = new byte[length];
            Array.Copy(buffer, bytes, length);
        }

        // Session.Timeout (<sessionState timeout>, 20 minutes by default, or what the application set) is how long the
        // session lasts unused.
        Cache(context).Set(GetSessionStateKey(id), bytes,
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(item.Timeout) });
    }

    public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
    {
        Cache(context).Remove(GetSessionStateKey(id));
    }

    public override void ResetItemTimeout(HttpContext context, string id)
    {
        Cache(context).Refresh(GetSessionStateKey(id));
    }

    public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
    {
        return SessionStateUtility.CreateLegitStoreData(context, null, null, timeout);
    }

    public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
    {
        SessionStateStoreData item = CreateNewStoreData(context, timeout);
        SetAndReleaseItemExclusive(context, id, item, null, true);
    }
}
