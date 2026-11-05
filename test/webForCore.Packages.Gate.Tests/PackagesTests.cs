using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace PackagesGate
{
    // Third-party net45 packages a customer application keeps as they are (the original packages, unchanged), on
    // webForCore: Ninject's web integration, Swashbuckle.Core and CacheCow.Server.WebApi. Each test reads the responses
    // PackagesGate recorded; CI's iis-compare job checks that IIS 10 gives the same ones.
    [Collection(PackagesCollection.Name)]
    public class PackagesTests
    {
        private readonly PackagesGate _gate;

        public PackagesTests(PackagesGate gate)
        {
            _gate = gate;
        }

        private JsonObject Ok(string request, int occurrence = 1)
        {
            var response = _gate.Response(request, occurrence);
            Assert.True((int)response["status"] == 200, $"{request}: {response["status"]}\n{response["body"]}\n{_gate.Output}");
            return response;
        }

        // Ninject.Web.Common.WebHost: WebActivatorEx 2.2.0 ran NinjectWebCommon.Start (its PreApplicationStartMethod), which
        // registered OnePerRequestHttpModule and NinjectHttpModule with DynamicModuleUtility; InRequestScope, keyed on
        // HttpContext.Current, gives one object for the request, which OnePerRequestHttpModule disposes at EndRequest.
        [Fact]
        public void NinjectWebHostStartsFromWebActivatorExAndScopesToTheRequest()
        {
            var first = PackagesGate.Json(Ok("GET /Scope.ashx"));
            Assert.True((bool)first["started"]);
            Assert.Equal(new[] { "Ninject.Web.Common.WebHost.NinjectHttpModule", "Ninject.Web.Common.WebHost.OnePerRequestHttpModule" },
                first["modules"]!.AsArray().Select(m => (string)m));
            Assert.True((bool)first["requestSame"]);

            var second = PackagesGate.Json(Ok("GET /Scope.ashx", 2));
            Assert.True((bool)second["requestSame"]);
            Assert.True((bool)second["newForThisRequest"]);
            Assert.True((bool)second["previousDisposed"], "the first request's object wasn't disposed at its end");
        }

        // Ninject.Web.Common.OwinHost: UseNinjectMiddleware's kernel, resolved from an OWIN middleware; an object scoped to
        // the OwinRequestScope it puts in the OWIN environment is one per request and disposed when the request ends.
        [Fact]
        public void AnOwinMiddlewareResolvesFromTheKernelInTheOwinRequestScope()
        {
            var first = PackagesGate.Json(Ok("GET /owin/scope"));
            Assert.True((bool)first["owinScopedSame"]);
            Assert.Equal("Hello, OWIN", (string)first["greeting"]);
            var second = PackagesGate.Json(Ok("GET /owin/scope", 2));
            Assert.True((bool)second["owinScopedSame"]);
            Assert.True((bool)second["newForThisRequest"]);
            Assert.True((bool)second["previousDisposed"], "the first request's object wasn't disposed at its end");
        }

        // Ninject.Web.WebApi.OwinHost: UseNinjectWebApi sets its dependency resolver, which constructs the controllers with
        // their dependencies. Its request scope reaching a dependency's dependency needs Ninject.Extensions.ContextPreservation
        // (Ninject's own behaviour, on IIS too), which the application doesn't have.
        [Fact]
        public void UseNinjectWebApiConstructsControllers()
        {
            var greeting = PackagesGate.Json(Ok("GET /api/greeting"));
            Assert.Equal("Hello, world", (string)greeting["message"]);
            Assert.Equal("Ninject.Web.WebApi.OwinHost.OwinNinjectDependencyResolver", (string)greeting["resolver"]);
            Assert.False((bool)greeting["requestSame"]);
            Assert.Equal("Hello, webForCore", (string)PackagesGate.Json(Ok("GET /api/greeting?name=webForCore"))["message"]);
        }

        // Swashbuckle.Core 5.6.0 (net40): the Swagger 2.0 document of the API's controllers, and the UI, whose page and
        // files are resources embedded in Swashbuckle.Core.dll, served by its Web API message handlers.
        [Fact]
        public void SwashbuckleServesTheDocumentAndTheEmbeddedUi()
        {
            var doc = PackagesGate.Json(Ok("GET /swagger/docs/v1"));
            Assert.Equal("2.0", (string)doc["swagger"]);
            Assert.Equal("Packages sample", (string)doc["info"]!["title"]);
            var paths = doc["paths"]!.AsObject().Select(p => p.Key).ToList();
            Assert.Contains("/api/Greeting", paths);
            Assert.Contains("/api/Products", paths);
            Assert.Contains("/api/Products/{id}", paths);
            Assert.Equal(new[] { "get", "put" }, doc["paths"]!["/api/Products/{id}"]!.AsObject().Select(p => p.Key).Order());
            Assert.NotNull(doc["definitions"]!["Product"]);

            var redirect = _gate.Response("GET /swagger");
            Assert.Equal(301, (int)redirect["status"]);
            Assert.EndsWith("/swagger/ui/index", PackagesGate.Header(redirect, "Location"));

            var index = Ok("GET /swagger/ui/index");
            Assert.StartsWith("text/html", PackagesGate.Header(index, "Content-Type"));
            Assert.Contains("<title>Swagger UI</title>", (string)index["body"]);
            var assets = PackagesGate.SwaggerUiAssets((string)index["body"]).ToList();
            Assert.Contains("swagger-ui-min-js", assets);
            Assert.Contains("css/screen-css", assets);
            Assert.Contains("lib/jquery-1-8-0-min-js", assets);
            Assert.Contains("images/logo_small-png", assets);
            foreach (string asset in assets)
            {
                var response = Ok("GET /swagger/ui/" + asset);
                string type = PackagesGate.Header(response, "Content-Type");
                Assert.True(asset.EndsWith("-js") ? type.StartsWith("text/javascript") : asset.EndsWith("-css") ? type.StartsWith("text/css") : type.StartsWith("image/png"), $"{asset}: {type}");
                Assert.NotEmpty((string)response["body"]);
            }
        }

        // CacheCow.Server.WebApi 2.5.0 (net452): [HttpCache] adds an ETag computed from the response to a GET, answers
        // 304 Not Modified to an If-None-Match with it, and a PUT or POST that changes the resource changes the ETag.
        [Fact]
        public void CacheCowValidatesWithETags()
        {
            var product = Ok("GET /api/products/1");
            string etag = PackagesGate.Header(product, "ETag");
            Assert.Matches("^\"[^\"]+\"$", etag);
            Assert.Equal("must-revalidate, max-age=0, private", PackagesGate.Header(product, "Cache-Control"));
            Assert.Equal("{\"Id\":1,\"Name\":\"Tomato Soup\",\"Price\":1.0}", (string)product["body"]);

            var notModified = _gate.Response("GET /api/products/1 If-None-Match (current)");
            Assert.Equal(304, (int)notModified["status"]);
            Assert.Equal("", (string)notModified["body"]);

            Assert.Equal("{\"Id\":1,\"Name\":\"Tomato Soup\",\"Price\":1.25}", (string)Ok("PUT /api/products/1")["body"]);
            var changed = Ok("GET /api/products/1 If-None-Match (before the PUT)");
            Assert.Equal("{\"Id\":1,\"Name\":\"Tomato Soup\",\"Price\":1.25}", (string)changed["body"]);
            Assert.NotEqual(etag, PackagesGate.Header(changed, "ETag"));
            Assert.Equal(304, (int)_gate.Response("GET /api/products/1 If-None-Match (after the PUT)")["status"]);

            string listTag = PackagesGate.Header(Ok("GET /api/products"), "ETag");
            Assert.Equal(304, (int)_gate.Response("GET /api/products If-None-Match (current)")["status"]);
            Assert.Equal(201, (int)_gate.Response("POST /api/products")["status"]);
            var list = Ok("GET /api/products If-None-Match (before the POST)");
            Assert.Contains("Hammer", (string)list["body"]);
            Assert.NotEqual(listTag, PackagesGate.Header(list, "ETag"));
        }

        // A web.config change restarts the application in a new worker: WebActivatorEx runs NinjectWebCommon.Stop (its
        // ApplicationShutdownMethod) in the old one and Start in the new one, and the packages serve again.
        [Fact]
        public async Task ARestartStopsAndStartsNinjectAndThePackagesServeAgain()
        {
            string log = Path.Combine(_gate.TempDirectory, "packages-sample-events.log");
            string[] Lines() => File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
            string firstStart = Assert.Single(Lines(), l => l.StartsWith("NinjectWebCommon.Start "));
            int workers = Regex.Matches(_gate.Output, @"Started worker \d+").Count;

            File.SetLastWriteTimeUtc(Path.Combine(_gate.SampleDirectory, "Web.config"), DateTime.UtcNow);

            var exited = new Regex(@"Recycling worker (\d+) \(process (\d+)\)");
            for (int i = 0; i < 300 && !(exited.Match(_gate.Output) is { Success: true } m && _gate.Output.Contains($"(process {m.Groups[2].Value}) exited (0).")); i++)
            {
                await Task.Delay(200);
            }
            var recycled = exited.Match(_gate.Output);
            Assert.True(recycled.Success, "no worker was recycled\n" + _gate.Output);
            Assert.Contains($"(process {recycled.Groups[2].Value}) exited (0).", _gate.Output);

            Assert.Equal(HttpStatusCode.OK, (await _gate.Client.GetAsync("/Scope.ashx")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _gate.Client.GetAsync("/api/greeting")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _gate.Client.GetAsync("/swagger/docs/v1")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _gate.Client.GetAsync("/api/products/2")).StatusCode);
            Assert.True(Regex.Matches(_gate.Output, @"Started worker \d+").Count > workers);

            string oldProcess = firstStart.Split(' ')[1];
            var lines = Lines();
            Assert.Contains("NinjectWebCommon.Stop " + oldProcess, lines);
            Assert.Contains(lines, l => l.StartsWith("NinjectWebCommon.Start ") && l != firstStart);
        }
    }
}
