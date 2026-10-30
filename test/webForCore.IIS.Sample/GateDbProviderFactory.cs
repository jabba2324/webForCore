using System.Data.Common;

namespace IISSample
{
    // A provider registered in Web.config's <system.data><DbProviderFactories> (data/Providers.aspx reads it back).
    public sealed class GateDbProviderFactory : DbProviderFactory
    {
        public static readonly GateDbProviderFactory Instance = new GateDbProviderFactory();
    }
}
