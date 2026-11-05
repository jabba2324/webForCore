using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(OidcSample.Startup))]

namespace OidcSample
{
    // As the Visual Studio template has it: Startup.Auth.cs does the work.
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
