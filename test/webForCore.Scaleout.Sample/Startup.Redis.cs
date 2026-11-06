extern alias redis1;

using Microsoft.AspNet.SignalR;

namespace ScaleoutSample
{
    // Microsoft.AspNet.SignalR.Redis, referenced through the alias redis1: its RedisScaleoutConfiguration and
    // StackExchangeRedis's have the same full name.
    internal static class RedisBackplane
    {
        public static void Use(string connection, string key)
        {
            redis1::Microsoft.AspNet.SignalR.DependencyResolverExtensions.UseRedis(GlobalHost.DependencyResolver,
                new redis1::Microsoft.AspNet.SignalR.RedisScaleoutConfiguration(connection, key));
        }
    }
}
