#if NETCOREAPP

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Web;
using System.Threading;

namespace WebFormsForCore.Test
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			var app = builder.Build();

			// Session state (SessionTest.aspx) is in process, as <sessionState> (no mode: InProc) had it on IIS. Only an
			// application whose session state was out of process (StateServer, SQLServer) adds a distributed cache and
			// UseAspNetCoreSessionProvider() (docs/MIGRATING.md, "Session state", #180).
			app.UseWebForms(options => options
				.HandleAllRequestsWithWebForms());

			app.Run();

		}
	}
}

#endif