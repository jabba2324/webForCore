using System.Web.Mvc;
using Unity;
using Unity.AspNet.Mvc;

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

    // As Unity.Mvc's template has it (ApplicantTracking, Bonobo Git Server), except that the template registers Start
    // with WebActivatorEx's PreApplicationStartMethod, whose start methods don't run on webForCore (#21): Global.asax's
    // Application_Start calls it instead (docs/MIGRATING.md).
    public static class UnityMvcActivator
    {
        public static void Start()
        {
            var container = new UnityContainer();
            container.RegisterType<IGreeter, Greeter>();
            DependencyResolver.SetResolver(new UnityDependencyResolver(container));
        }
    }
}
