using System.Web.Mvc;
using Unity;
using Unity.AspNet.Mvc;

// As Unity.Mvc's template has it (ApplicantTracking, Bonobo Git Server): Unity is registered by WebActivatorEx before
// the application starts (#21).
[assembly: WebActivatorEx.PreApplicationStartMethod(typeof(SmokeMvc.UnityMvcActivator), nameof(SmokeMvc.UnityMvcActivator.Start))]
[assembly: WebActivatorEx.PostApplicationStartMethod(typeof(SmokeMvc.UnityMvcActivator), nameof(SmokeMvc.UnityMvcActivator.Started))]

namespace SmokeMvc
{
    public interface IGreeter
    {
        string Greet();
    }

    public class Greeter : IGreeter
    {
        public string Greet() => "hello from Unity";
    }

    public static class UnityMvcActivator
    {
        public static bool PostStartRan;

        public static void Start()
        {
            var container = new UnityContainer();
            container.RegisterType<IGreeter, Greeter>();
            DependencyResolver.SetResolver(new UnityDependencyResolver(container));
        }

        public static void Started() => PostStartRan = true;
    }
}
