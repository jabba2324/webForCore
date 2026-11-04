// webForCore: the object graphs of the .NET Framework wire-format fixtures (#156), compiled into Serialization.Tests
// (.NET 10, the BinaryFormatter port) and into tools/serialization/FrameworkStreams (.NET Framework 4.8, Microsoft's
// BinaryFormatter), which writes FrameworkStreams/<name>.bin. Each graph is made the same way on both runtimes, so
// what one runtime reads is compared with the graph that runtime makes. Only .NET Framework types are used: a type of
// this assembly would have another assembly name on each side.
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Web;

namespace WebForCore.Serialization.Tests
{
    public sealed class FrameworkStreamCase
    {
        public FrameworkStreamCase(string name, Func<object> create, bool portWrites = true)
        {
            Name = name;
            Create = create;
            PortWrites = portWrites;
        }

        // FrameworkStreams/<Name>.bin.
        public string Name { get; }

        public Func<object> Create { get; }

        // False for what .NET can't have (System.Empty): only read.
        public bool PortWrites { get; }

        public override string ToString() => Name;
    }

    public static class FrameworkStreamCases
    {
        private static Type ListT() => typeof(List<>).GetGenericArguments()[0];

        private static MethodInfo ArrayIndexOf2() =>
            typeof(Array).GetMethods().Single(m => m.Name == "IndexOf" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);

        public static readonly FrameworkStreamCase[] All =
        {
            // UnitySerializationHolder, RuntimeTypeUnity (4)
            new FrameworkStreamCase("type-string", () => typeof(string)),
            new FrameworkStreamCase("type-void", () => typeof(void)),
            new FrameworkStreamCase("type-array", () => typeof(int[])),
            new FrameworkStreamCase("type-array-2d", () => typeof(int[,])),
            new FrameworkStreamCase("type-jagged", () => typeof(string[][])),
            new FrameworkStreamCase("type-pointer", () => typeof(int).MakePointerType()),
            new FrameworkStreamCase("type-byref", () => typeof(int).MakeByRefType()),
            new FrameworkStreamCase("type-nested", () => typeof(Environment.SpecialFolder)),
            new FrameworkStreamCase("type-nullable", () => typeof(int?)),
            new FrameworkStreamCase("type-generic", () => typeof(Dictionary<string, List<int>>)),
            new FrameworkStreamCase("type-generic-definition", () => typeof(Dictionary<,>)),
            new FrameworkStreamCase("type-generic-nested", () => typeof(Dictionary<string, int>.KeyCollection)),
            new FrameworkStreamCase("type-system", () => typeof(NameValueCollection)),
            new FrameworkStreamCase("type-system-web", () => typeof(HttpCookie)),
            new FrameworkStreamCase("type-generic-system-web", () => typeof(List<HttpCookie>[])),
            // GenericParameterTypeUnity (7) and PartialInstantiationTypeUnity (8)
            new FrameworkStreamCase("type-generic-parameter", () => ListT()),
            new FrameworkStreamCase("type-generic-parameter-array", () => ListT().MakeArrayType()),
            new FrameworkStreamCase("type-method-generic-parameter", () => typeof(Array).GetMethod("Empty").GetGenericArguments()[0]),
            new FrameworkStreamCase("type-partial", () => typeof(Dictionary<,>).MakeGenericType(typeof(string), ListT())),
            new FrameworkStreamCase("type-generic-definition-array", () => typeof(List<>).MakeArrayType()),
            // One object, referenced twice, and the issue's shape: types among other values.
            new FrameworkStreamCase("type-graph", () => new object[] { typeof(string), "text", typeof(string), 42, typeof(Uri), null }),

            // MemberInfoSerializationHolder
            new FrameworkStreamCase("method", () => typeof(object).GetMethod("ToString")),
            new FrameworkStreamCase("method-overload", () => typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) })),
            new FrameworkStreamCase("method-static-overload", () => typeof(Math).GetMethod("Abs", new[] { typeof(decimal) })),
            new FrameworkStreamCase("method-byref", () => typeof(int).GetMethod("TryParse", new[] { typeof(string), typeof(int).MakeByRefType() })),
            new FrameworkStreamCase("method-params", () => typeof(string).GetMethod("Format", new[] { typeof(IFormatProvider), typeof(string), typeof(object[]) })),
            new FrameworkStreamCase("method-generic-definition", () => typeof(Array).GetMethod("AsReadOnly")),
            new FrameworkStreamCase("method-generic-instance", () => typeof(Array).GetMethod("AsReadOnly").MakeGenericMethod(typeof(int))),
            new FrameworkStreamCase("method-generic-overload", () => ArrayIndexOf2()),
            new FrameworkStreamCase("method-generic-overload-instance", () => ArrayIndexOf2().MakeGenericMethod(typeof(string))),
            new FrameworkStreamCase("method-on-generic-type", () => typeof(Dictionary<string, int>).GetMethod("TryGetValue")),
            new FrameworkStreamCase("method-reflected-type", () => typeof(ArgumentNullException).GetMethod("GetType")),
            new FrameworkStreamCase("method-system-web", () => typeof(HttpCookie).GetMethod("ToString")),
            new FrameworkStreamCase("method-nonpublic", () => typeof(Version).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)),
            new FrameworkStreamCase("constructor", () => typeof(string).GetConstructor(new[] { typeof(char), typeof(int) })),
            new FrameworkStreamCase("constructor-generic-type", () => typeof(List<int>).GetConstructor(new[] { typeof(int) })),
            new FrameworkStreamCase("field", () => typeof(string).GetField("Empty")),
            new FrameworkStreamCase("field-const", () => typeof(int).GetField("MaxValue")),
            new FrameworkStreamCase("field-enum", () => typeof(DayOfWeek).GetField("Friday")),
            new FrameworkStreamCase("property", () => typeof(string).GetProperty("Length")),
            new FrameworkStreamCase("property-indexer", () => typeof(List<int>).GetProperty("Item")),
            new FrameworkStreamCase("property-system-web", () => typeof(HttpCookie).GetProperty("Value")),
            new FrameworkStreamCase("property-overload", () => typeof(NameValueCollection).GetProperty("Item", new[] { typeof(int) })),
            new FrameworkStreamCase("event", () => typeof(AppDomain).GetEvent("AssemblyLoad")),

            // The other unity types
            new FrameworkStreamCase("dbnull", () => DBNull.Value),
            new FrameworkStreamCase("missing", () => Missing.Value),
            new FrameworkStreamCase("assembly-mscorlib", () => typeof(object).Assembly),
            new FrameworkStreamCase("assembly-system-web", () => typeof(HttpCookie).Assembly),
            new FrameworkStreamCase("module-mscorlib", () => typeof(object).Module),
            new FrameworkStreamCase("module-system-web", () => typeof(HttpCookie).Module),
#if NETFRAMEWORK
            new FrameworkStreamCase("empty", () => EmptyUnity.Instance, portWrites: false),
#else
            new FrameworkStreamCase("empty", () => throw new NotSupportedException(".NET has no System.Empty"), portWrites: false),
#endif
        };

        // What a runtime read is the graph it makes: the same reflection object (or DBNull, Missing), or an array of them.
        public static bool Matches(object expected, object actual)
        {
            if (expected is object[] expectedArray)
            {
                if (!(actual is object[] actualArray) || actualArray.Length != expectedArray.Length)
                {
                    return false;
                }
                for (int i = 0; i < expectedArray.Length; i++)
                {
                    if (!(expectedArray[i] == null ? actualArray[i] == null : actualArray[i] != null && Matches(expectedArray[i], actualArray[i])))
                    {
                        return false;
                    }
                }
                return true;
            }
            return Equals(expected, actual);
        }
    }

#if NETFRAMEWORK
    // What System.Empty.GetObjectData writes (UnitySerializationHolder.GetUnitySerializationInfo(info, EmptyUnity, null,
    // null)): System.Empty is internal to mscorlib, and only COM interop's VT_EMPTY variant makes one.
    [Serializable]
    public sealed class EmptyUnity : System.Runtime.Serialization.ISerializable
    {
        public static readonly EmptyUnity Instance = new EmptyUnity();

        public void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
        {
            info.FullTypeName = "System.UnitySerializationHolder";
            info.AssemblyName = typeof(object).Assembly.FullName;
            info.AddValue("Data", null, typeof(string));
            info.AddValue("UnityType", 1);
            info.AddValue("AssemblyName", string.Empty);
        }
    }
#endif
}
