[assembly: WebActivatorEx.PreApplicationStartMethod(typeof(PackagesSample.App_Start.NinjectWebCommon), "Start")]
[assembly: WebActivatorEx.ApplicationShutdownMethodAttribute(typeof(PackagesSample.App_Start.NinjectWebCommon), "Stop")]

namespace PackagesSample.App_Start
{
    using System;
    using System.Web;

    using Microsoft.Web.Infrastructure.DynamicModuleHelper;

    using Ninject;
    using Ninject.Web.Common;
    using Ninject.Web.Common.WebHost;

    // Ninject.Web.Common.WebHost 3.3.2's App_Start/NinjectWebCommon.cs.pp, as the package wrote it into the application,
    // with RegisterServices filled in (and shared with the OWIN startup's kernel), the kernel kept (the OWIN bootstrapper
    // replaces Bootstrapper's static one), and what the gate checks recorded.
    public static class NinjectWebCommon 
    {
        private static readonly Bootstrapper bootstrapper = new Bootstrapper();

        /// <summary>
        /// The kernel CreateKernel made.
        /// </summary>
        public static IKernel Kernel { get; private set; }

        /// <summary>
        /// Starts the application.
        /// </summary>
        public static void Start() 
        {
            DynamicModuleUtility.RegisterModule(typeof(OnePerRequestHttpModule));
            DynamicModuleUtility.RegisterModule(typeof(NinjectHttpModule));
            bootstrapper.Initialize(CreateKernel);
            Services.Events.RecordToFile("NinjectWebCommon.Start");
        }

        /// <summary>
        /// Stops the application.
        /// </summary>
        public static void Stop()
        {
            bootstrapper.ShutDown();
            Services.Events.RecordToFile("NinjectWebCommon.Stop");
        }

        /// <summary>
        /// Creates the kernel that will manage your application.
        /// </summary>
        /// <returns>The created kernel.</returns>
        private static IKernel CreateKernel()
        {
            var kernel = new StandardKernel();
            try
            {
                kernel.Bind<Func<IKernel>>().ToMethod(ctx => () => new Bootstrapper().Kernel);
                kernel.Bind<IHttpModule>().To<HttpApplicationInitializationHttpModule>();
                RegisterServices(kernel);
                Kernel = kernel;
                return kernel;
            }
            catch
            {
                kernel.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Load your modules or register your services here!
        /// </summary>
        /// <param name="kernel">The kernel.</param>
        internal static void RegisterServices(IKernel kernel)
        {
            kernel.Bind<Services.IRequestService>().To<Services.RequestService>().InRequestScope();
            kernel.Bind<Services.IGreeter>().To<Services.Greeter>();
            kernel.Bind<Services.IOwinScoped>().To<Services.OwinScoped>().InScope(Services.OwinScoped.Scope);
            kernel.Bind<Services.IProductStore>().To<Services.ProductStore>().InSingletonScope();
        }
    }
}
