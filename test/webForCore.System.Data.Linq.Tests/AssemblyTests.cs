// webForCore's tests (MIT), #142: System.Data.Linq keeps .NET Framework's identity, so that a .NET Framework library
// built against it (Web API OData's model builder, an application's .dbml classes) binds to webForCore's.

using System.Linq;
using System.Reflection;
using Xunit;

namespace System.Data.Linq.Tests
{
    public class AssemblyTests
    {
        private static readonly Assembly Linq = typeof(DataContext).Assembly;

        [Fact]
        public void HasFrameworksIdentity()
        {
            AssemblyName name = Linq.GetName();

            Assert.Equal("System.Data.Linq", name.Name);
            Assert.Equal(new Version(4, 0, 0, 0), name.Version);
            Assert.Equal("b77a5c561934e089", Convert.ToHexString(name.GetPublicKeyToken()).ToLowerInvariant());
            // The ECMA key, as .NET Framework's System.Data.Linq has.
            Assert.Equal("00000000000000000400000000000000", Convert.ToHexString(name.GetPublicKey()));
        }

        // A .NET Framework 4 library's reference, and a .NET 3.5 one's (System.Data.Linq 3.5.0.0), bind to it.
        [Theory]
        [InlineData("System.Data.Linq, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
        [InlineData("System.Data.Linq, Version=3.5.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
        public void FrameworkReferencesBindToIt(string reference)
        {
            Assert.Same(Linq, Assembly.Load(reference));
            Assert.Same(typeof(Binary), Type.GetType("System.Data.Linq.Binary, " + reference, throwOnError: true));
        }

        [Fact]
        public void GetTypesLoadsEveryType()
        {
            Type[] types = Linq.GetTypes();

            Assert.DoesNotContain(null, types);
            Assert.Contains(typeof(DataContext), types);
        }

        [Fact]
        public void HasFrameworksAssemblyAttributes()
        {
            Assert.Equal("Microsoft Corporation", Linq.GetCustomAttribute<AssemblyCompanyAttribute>().Company);
            Assert.Equal("Microsoft® .NET Framework", Linq.GetCustomAttribute<AssemblyProductAttribute>().Product);
            Assert.Equal("System.Data.Linq.dll", Linq.GetCustomAttribute<AssemblyTitleAttribute>().Title);
            Assert.Equal("en-US", Linq.GetCustomAttribute<Resources.NeutralResourcesLanguageAttribute>().CultureName);
            Assert.True(Linq.GetCustomAttribute<CLSCompliantAttribute>().IsCompliant);
            // ThisAssembly, which SqlProvider's log names, is the file version, as on .NET Framework.
            Assert.Equal(Linq.GetCustomAttribute<AssemblyFileVersionAttribute>().Version, ThisAssembly.InformationalVersion);
        }

        // It uses System.Data.SqlClient's public API, webForCore's build of it, and webForCore's BinaryFormatter port.
        [Fact]
        public void ReferencesWebForCoresSqlClient()
        {
            var references = Linq.GetReferencedAssemblies().ToDictionary(r => r.Name);

            Assert.Equal(new Version(4, 6, 1, 6), references["System.Data.SqlClient"].Version);
            Assert.Equal("b03f5f7f11d50a3a", Convert.ToHexString(references["System.Data.SqlClient"].GetPublicKeyToken()).ToLowerInvariant());
            Assert.Contains("WebFormsForCore.Serialization.Formatters", references.Keys);
        }
    }
}
