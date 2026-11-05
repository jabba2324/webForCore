using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web;
using Ninject.Activation;

namespace PackagesSample.Services
{
    // What happened in this application: NinjectWebCommon's start and stop methods, and the request-scoped objects
    // disposed. The start and stop methods are also written to packages-sample-events.log in the temp folder, with the
    // process, so that the gate can see them across a restart.
    public static class Events
    {
        private static readonly ConcurrentQueue<string> s_events = new ConcurrentQueue<string>();

        public static void Record(string what) => s_events.Enqueue(what);

        public static void RecordToFile(string what)
        {
            Record(what);
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "packages-sample-events.log"),
                what + " " + System.Diagnostics.Process.GetCurrentProcess().Id + "\n");
        }

        public static string[] All => s_events.ToArray();

        public static bool Disposed(string id) => s_events.Contains("disposed " + id);
    }

    public interface IRequestService
    {
        string Id { get; }
    }

    // Bound InRequestScope: one per request, disposed at the request's end (OnePerRequestHttpModule, EndRequest).
    public sealed class RequestService : IRequestService, IDisposable
    {
        private static int s_next;

        public RequestService() => Id = "request-" + Interlocked.Increment(ref s_next);

        public string Id { get; }

        public void Dispose() => Events.Record("disposed " + Id);
    }

    public interface IGreeter
    {
        IRequestService Request { get; }

        string Greet(string name);
    }

    // Transient, with a request-scoped dependency: within a request it gets the request's RequestService.
    public sealed class Greeter : IGreeter
    {
        public Greeter(IRequestService request) => Request = request;

        public IRequestService Request { get; }

        public string Greet(string name) => "Hello, " + name;
    }

    public interface IOwinScoped
    {
        string Id { get; }
    }

    // Scoped to Ninject.Web.Common.OwinHost's request scope: the OwinRequestScope its middleware puts in the OWIN
    // environment ("NinjectOwinRequestScope") and disposes when the rest of the pipeline has run.
    public sealed class OwinScoped : IOwinScoped, IDisposable
    {
        private static int s_next;

        public OwinScoped() => Id = "owin-" + Interlocked.Increment(ref s_next);

        public string Id { get; }

        public void Dispose() => Events.Record("disposed " + Id);

        public static object Scope(IContext context)
        {
            var http = HttpContext.Current;
            if (http == null || !(http.Items["owin.Environment"] is IDictionary<string, object> environment)) return null;
            return environment.TryGetValue("NinjectOwinRequestScope", out var scope) ? scope : null;
        }
    }

    public sealed class Product
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public decimal Price { get; set; }
    }

    public interface IProductStore
    {
        IEnumerable<Product> All();

        Product Get(int id);

        Product Save(Product product);
    }

    public sealed class ProductStore : IProductStore
    {
        private readonly ConcurrentDictionary<int, Product> _products = new ConcurrentDictionary<int, Product>(new[]
        {
            new KeyValuePair<int, Product>(1, new Product { Id = 1, Name = "Tomato Soup", Price = 1 }),
            new KeyValuePair<int, Product>(2, new Product { Id = 2, Name = "Yo-yo", Price = 3.75M }),
        });

        public IEnumerable<Product> All() => _products.Values.OrderBy(p => p.Id).Select(Copy).ToList();

        public Product Get(int id) => _products.TryGetValue(id, out var product) ? Copy(product) : null;

        public Product Save(Product product)
        {
            lock (_products)
            {
                if (product.Id == 0) product.Id = _products.Keys.DefaultIfEmpty(0).Max() + 1;
                _products[product.Id] = Copy(product);
            }
            return Copy(product);
        }

        private static Product Copy(Product p) => new Product { Id = p.Id, Name = p.Name, Price = p.Price };
    }
}
