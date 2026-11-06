using System;
using System.Configuration;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(ScaleoutSample.Startup))]

namespace ScaleoutSample
{
    // SignalR 2 with a scale-out backplane, set up as on IIS: the backplane on GlobalHost's dependency resolver, then
    // MapSignalR. SCALEOUT_BACKPLANE (stackexchangeredis, redis, sqlserver) and SCALEOUT_CONNECTION choose it; a web
    // farm would have them in Web.config's appSettings and connectionStrings.
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            string backplane = Environment.GetEnvironmentVariable("SCALEOUT_BACKPLANE") ?? "";
            string connection = Environment.GetEnvironmentVariable("SCALEOUT_CONNECTION");
            // The Redis channel: one per test run, so that runs on a shared Redis don't see each other's messages.
            string key = Environment.GetEnvironmentVariable("SCALEOUT_KEY") ?? "webforcore-scaleout";
            switch (backplane)
            {
                case "stackexchangeredis":
                    // Microsoft.AspNet.SignalR.StackExchangeRedis: StackExchange.Redis 2.
                    GlobalHost.DependencyResolver.UseStackExchangeRedis(new RedisScaleoutConfiguration(connection, key));
                    break;
                case "redis":
                    // Microsoft.AspNet.SignalR.Redis: StackExchange.Redis.StrongName 1.x (Startup.Redis.cs).
                    RedisBackplane.Use(connection, key);
                    break;
                case "sqlserver":
                    GlobalHost.DependencyResolver.UseSqlServer(new SqlScaleoutConfiguration(connection) { TableCount = 2 });
                    break;
                case "":
                    break;
                default:
                    throw new ConfigurationErrorsException("Unknown SCALEOUT_BACKPLANE: " + backplane);
            }
            app.MapSignalR();
        }
    }

    // Which instance answered: SCALEOUT_INSTANCE (A or B).
    public class ScaleoutHub : Hub
    {
        private static string Instance => Environment.GetEnvironmentVariable("SCALEOUT_INSTANCE") ?? "?";

        public string Whoami() => Instance;

        // To every client of every instance.
        public void Broadcast(string message) => Clients.All.broadcast(message, Instance);

        public System.Threading.Tasks.Task Join(string group) => Groups.Add(Context.ConnectionId, group);

        public System.Threading.Tasks.Task Leave(string group) => Groups.Remove(Context.ConnectionId, group);

        // To the clients in a group, on whichever instance they are connected.
        public void SendToGroup(string group, string message) => Clients.Group(group).groupMessage(group, message, Instance);
    }
}
