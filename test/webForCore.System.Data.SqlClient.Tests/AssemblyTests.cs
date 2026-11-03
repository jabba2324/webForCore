// webForCore's test (MIT). Issue #41: Microsoft's System.Data.SqlClient packages for .NET (4.4.3 to 4.9.1) contain
// SqlGuidCaster, which overlays a byte[] on SqlGuid; on .NET SqlGuid's only field is a Guid?, so the runtime refuses to
// load the type and Assembly.GetTypes() on the assembly threw a ReflectionTypeLoadException. Applications that scan
// every loaded assembly (Opserver's tabs, DI containers, AutoMapper profiles, MEF) failed.

using System.Data.SqlTypes;
using System.Linq;
using System.Reflection;
using Xunit;

namespace System.Data.SqlClient.Tests
{
    public class AssemblyTests
    {
        private static readonly Assembly SqlClient = typeof(SqlConnection).Assembly;

        [Fact]
        public void GetTypesLoadsEveryType()
        {
            Type[] types = SqlClient.GetTypes();

            Assert.DoesNotContain(null, types);
            Assert.Contains(typeof(SqlConnection), types);
            Assert.DoesNotContain(types, t => t.Name == "SqlGuidCaster");
        }

        [Fact]
        public void EveryTypeHasItsMembers()
        {
            foreach (Type type in SqlClient.GetTypes())
            {
                type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            }
        }

        [Fact]
        public void SqlGuidCtorMakesTheSqlGuidOfTheBytes()
        {
            Type workarounds = SqlClient.GetType("System.Data.SqlTypes.SqlTypeWorkarounds", throwOnError: true);
            MethodInfo ctor = workarounds.GetMethod("SqlGuidCtor", BindingFlags.NonPublic | BindingFlags.Static);
            Guid guid = Guid.NewGuid();

            var value = (SqlGuid)ctor.Invoke(null, new object[] { guid.ToByteArray(), true });

            Assert.Equal(guid, value.Value);
        }

        [Fact]
        public void HasMicrosoftsIdentity()
        {
            AssemblyName name = SqlClient.GetName();

            Assert.Equal("System.Data.SqlClient", name.Name);
            Assert.Equal(new Version(4, 6, 1, 6), name.Version);
            Assert.Equal("b03f5f7f11d50a3a", Convert.ToHexString(name.GetPublicKeyToken()).ToLowerInvariant());
        }
    }
}
