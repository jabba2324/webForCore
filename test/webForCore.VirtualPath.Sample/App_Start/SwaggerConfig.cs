using System.Web.Http;
using Swashbuckle.Application;

namespace VirtualPathSample
{
    // Swashbuckle 5's Swagger document for the Web API, as SwaggerConfig.cs registers it (Swashbuckle.Core: no
    // WebActivatorEx). Its root URL comes from the request and Web API's virtual path root (#190).
    public static class SwaggerConfig
    {
        public static void Register(HttpConfiguration config)
        {
            config.EnableSwagger(c => c.SingleApiVersion("v1", "VirtualPath sample"));
        }
    }
}
