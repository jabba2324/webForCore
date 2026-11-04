using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Xunit;
using BinaryFormatter = WebFormsForCore.Serialization.Formatters.Binary.BinaryFormatter;

namespace WebForCore.Serialization.Tests
{
    // #156: Type, the other reflection objects, DBNull, Missing, Assembly and Module through the BinaryFormatter port, in
    // .NET Framework's format (System.UnitySerializationHolder and System.Reflection.MemberInfoSerializationHolder;
    // ReflectionSurrogates.cs). Orchard 1.10 caches NHibernate's Configuration, which holds Types, with BinaryFormatter:
    // writing it failed with "Type 'System.RuntimeType' ... is not marked as serializable" on every start.
    //
    // FrameworkStreams/<name>.bin were written by .NET Framework 4.8's BinaryFormatter (tools/serialization/FrameworkStreams,
    // CI's serialization-framework-streams job, which also checks that .NET Framework still writes them and reads them
    // back): the port reads each one as the graph FrameworkStreamCases makes, and writes that graph as the same bytes.
    public class ReflectionSerializationTest
    {
        [Serializable]
        private sealed class Holder
        {
            public Type T = typeof(string);
            public Type Same = typeof(string);
            public Type[] Types = { typeof(int[]), typeof(List<>), typeof(Dictionary<string, Uri>) };
            public MethodInfo Method = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) })!;
            public PropertyInfo Property = typeof(Holder).GetProperty(nameof(Value))!;
            public FieldInfo Field = typeof(Holder).GetField(nameof(T))!;
            public object Missing = System.Reflection.Missing.Value;
            public object Null = DBNull.Value;
            public string Value => T.Name;
        }

        public static IEnumerable<object[]> Cases() => FrameworkStreamCases.All.Select(c => new object[] { c });

        public static IEnumerable<object[]> WrittenCases() => FrameworkStreamCases.All.Where(c => c.PortWrites).Select(c => new object[] { c });

        private static byte[] Serialize(object graph, SerializationBinder? binder = null)
        {
            var ms = new MemoryStream();
            new BinaryFormatter { Binder = binder }.Serialize(ms, graph);
            return ms.ToArray();
        }

        private static object Deserialize(byte[] bytes, SerializationBinder? binder = null) =>
            new BinaryFormatter { Binder = binder }.Deserialize(new MemoryStream(bytes));

        private static byte[] FrameworkStream(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "FrameworkStreams", name + ".bin");
            Assert.True(File.Exists(path), $"{path}: written by .NET Framework 4.8 (tools/serialization/FrameworkStreams)");
            return File.ReadAllBytes(path);
        }

        // The issue's repro: a [Serializable] class with a Type field.
        [Fact]
        public void TypeField_RoundTrips()
        {
            var copy = (Holder)Deserialize(Serialize(new Holder()));

            Assert.Same(typeof(string), copy.T);
            Assert.Same(copy.T, copy.Same);
            Assert.Equal(new[] { typeof(int[]), typeof(List<>), typeof(Dictionary<string, Uri>) }, copy.Types);
            Assert.Equal(typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) }), copy.Method);
            Assert.Equal(typeof(Holder).GetProperty(nameof(Holder.Value)), copy.Property);
            Assert.Equal(typeof(Holder).GetField(nameof(Holder.T)), copy.Field);
            Assert.Same(Missing.Value, copy.Missing);
            Assert.Same(DBNull.Value, copy.Null);
            Assert.Equal("String", copy.Value);
        }

        [Fact]
        public void Type_WrittenAsUnitySerializationHolder()
        {
            string text = Encoding.UTF8.GetString(Serialize(new object[] { typeof(List<Uri>) }));

            // mscorlib's types are written without an assembly record, as on .NET Framework.
            Assert.Contains("System.UnitySerializationHolder", text);
            Assert.Contains("System.Collections.Generic.List`1[[System.Uri, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]", text);
            Assert.Contains("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", text);
            Assert.DoesNotContain("System.Private", text);
            Assert.DoesNotContain("System.RuntimeType", text);
        }

        // Each type with the assembly .NET Framework had it in: TypeForwardedFrom (Uri), the .NET Framework facade .NET
        // forwards it from (Enumerable: System.Core; NameValueCollection: System), this repository's System.Web as 4.0.0.0.
        [Theory]
        [InlineData(typeof(Uri), "System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
        [InlineData(typeof(Enumerable), "System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
        [InlineData(typeof(System.Collections.Specialized.NameValueCollection), "System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
        [InlineData(typeof(System.Web.HttpContext), "System.Web, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
        public void Type_WrittenWithFrameworkAssembly(Type type, string assembly)
        {
            byte[] bytes = Serialize(type);
            string text = Encoding.UTF8.GetString(bytes);

            Assert.Contains(type.FullName!, text);
            Assert.Contains(assembly, text);
            Assert.Same(type, Deserialize(bytes));
        }

        [Fact]
        public void Members_WrittenAsMemberInfoSerializationHolder()
        {
            string text = Encoding.UTF8.GetString(Serialize(typeof(Dictionary<string, int>).GetMethod("TryGetValue")!));

            Assert.Contains("System.Reflection.MemberInfoSerializationHolder", text);
            Assert.Contains("Boolean TryGetValue(System.String, Int32 ByRef)", text);
            Assert.Contains("System.Boolean TryGetValue(System.String, System.Int32&)", text);
            Assert.DoesNotContain("System.Private", text);
        }

        // A binder that resolves every name as Type.GetType does finds .NET's own UnitySerializationHolder, which reads
        // only DBNull; the port's reads the rest.
        [Fact]
        public void BinderResolvingByName_StillReadsTypes()
        {
            var binder = new TypeGetTypeBinder();
            var graph = new object[] { typeof(string), typeof(string).GetMethod("Trim", Type.EmptyTypes)!, DBNull.Value };

            var copy = (object[])Deserialize(Serialize(graph), binder);

            Assert.Equal(graph, copy);
            Assert.Contains("System.UnitySerializationHolder", binder.Names);
        }

        private sealed class TypeGetTypeBinder : SerializationBinder
        {
            public readonly List<string> Names = new List<string>();

            public override Type? BindToType(string assemblyName, string typeName)
            {
                Names.Add(typeName);
                return Type.GetType(typeName + ", " + assemblyName);
            }
        }

        [Fact]
        public void GlobalMethods_NotSupported()
        {
            MethodInfo? global = typeof(BinaryFormatter).Assembly.ManifestModule.GetMethods().FirstOrDefault();
            if (global == null)
            {
                // No module-level methods to try; a dynamic one has no ReflectedType either.
                return;
            }
            Assert.Throws<NotSupportedException>(() => Serialize(global));
        }

        [Theory]
        [MemberData(nameof(WrittenCases))]
        public void RoundTrips(FrameworkStreamCase streamCase)
        {
            object graph = streamCase.Create();
            byte[] bytes = Serialize(graph);

            if (streamCase.ReadThrows != null)
            {
                Assert.Throws(streamCase.ReadThrows, () => Deserialize(bytes));
                return;
            }
            object copy = Deserialize(bytes);

            Assert.True(FrameworkStreamCases.Matches(graph, copy), $"{streamCase.Name}: read {copy}");
        }

        // .NET Framework -> the port.
        [Theory]
        [MemberData(nameof(Cases))]
        public void ReadsFrameworkStream(FrameworkStreamCase streamCase)
        {
            byte[] bytes = FrameworkStream(streamCase.Name);

            if (!streamCase.PortWrites)
            {
                // System.Empty: .NET has none.
                Assert.Throws<SerializationException>(() => Deserialize(bytes));
                return;
            }
            if (streamCase.ReadThrows != null)
            {
                Assert.Throws(streamCase.ReadThrows, () => Deserialize(bytes));
                return;
            }
            object copy = Deserialize(bytes);

            Assert.True(FrameworkStreamCases.Matches(streamCase.Create(), copy), $"{streamCase.Name}: read {copy}");
        }

        // The port -> .NET Framework: the same bytes .NET Framework writes, which it reads back (the CI job checks that).
        [Theory]
        [MemberData(nameof(WrittenCases))]
        public void WritesFrameworkBytes(FrameworkStreamCase streamCase)
        {
            byte[] expected = FrameworkStream(streamCase.Name);

            byte[] actual = Serialize(streamCase.Create());

            Assert.Equal(Printable(expected), Printable(actual));
            Assert.Equal(expected, actual);
        }

        // Bytes as text, for a readable difference.
        private static string Printable(byte[] bytes) =>
            new string(bytes.Select(b => b >= 0x20 && b < 0x7f ? (char)b : '.').ToArray());
    }
}
