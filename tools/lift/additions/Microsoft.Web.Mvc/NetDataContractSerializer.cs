// webForCore: the NetDataContractSerializer that MvcSerializer (Html.Serialize, [Deserialize]) uses, which .NET does not
// have. MIT license. Copied into src/webForCore.Microsoft.Web.Mvc by tools/lift/lift_project.py; MvcSerializer finds it
// by its simple name, so the lifted MvcSerializer.cs is unchanged.
//
// NetDataContractSerializer was DataContractSerializer writing each object's .NET type and assembly, so a graph read
// back without a list of known types. A DataContractResolver does the same here: types the data contract serializer
// already knows (primitives, ...) keep their usual names, and every other type is written as its full name in its
// assembly's "namespace". The output is not byte-compatible with .NET Framework's, which only matters for a value
// serialized there and read here.
//
// MvcSerializer signs and encrypts the bytes with the machine key before they leave the server and checks them before
// reading, so the type names come from the server itself; types that can run code when constructed or invoked
// (delegates) are refused anyway.
using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml;

namespace Microsoft.Web.Mvc
{
    internal sealed class NetDataContractSerializer
    {
        public void WriteObject(XmlDictionaryWriter writer, object graph)
        {
            CreateSerializer().WriteObject(writer, graph);
        }

        public object ReadObject(XmlDictionaryReader reader)
        {
            return CreateSerializer().ReadObject(reader);
        }

        private static DataContractSerializer CreateSerializer()
        {
            return new DataContractSerializer(typeof(object), new DataContractSerializerSettings
            {
                DataContractResolver = new AssemblyQualifiedResolver(),
                MaxItemsInObjectGraph = Int32.MaxValue,
                PreserveObjectReferences = true, // as NetDataContractSerializer did
            });
        }

        private sealed class AssemblyQualifiedResolver : DataContractResolver
        {
            // The data contract serializer's own contracts: XML Schema primitives (int, string, ...) and its additions
            // (char, Guid, TimeSpan, arrays, ...).
            private static bool IsBuiltIn(string typeNamespace) =>
                typeNamespace.StartsWith("http://www.w3.org/2001/XMLSchema", StringComparison.Ordinal)
                || typeNamespace.StartsWith("http://schemas.microsoft.com/2003/10/Serialization/", StringComparison.Ordinal);

            public override bool TryResolveType(Type type, Type declaredType, DataContractResolver knownTypeResolver,
                out XmlDictionaryString typeName, out XmlDictionaryString typeNamespace)
            {
                // The serializer's own contracts keep their usual names; every other type is written by assembly and full name.
                if (knownTypeResolver.TryResolveType(type, declaredType, null, out typeName, out typeNamespace)
                    && IsBuiltIn(typeNamespace.Value))
                {
                    return true;
                }
                EnsureAllowed(type);
                XmlDictionary dictionary = new XmlDictionary();
                typeName = dictionary.Add(type.FullName);
                typeNamespace = dictionary.Add(type.Assembly.FullName);
                return true;
            }

            public override Type ResolveName(string typeName, string typeNamespace, Type declaredType, DataContractResolver knownTypeResolver)
            {
                // A contract namespace (a URI) is the serializer's; an assembly name is one this resolver wrote.
                if (typeNamespace.Contains("://"))
                {
                    return knownTypeResolver.ResolveName(typeName, typeNamespace, declaredType, null);
                }
                Type type;
                try
                {
                    type = Assembly.Load(new AssemblyName(typeNamespace)).GetType(typeName, throwOnError: true);
                }
                catch (Exception ex)
                {
                    throw new SerializationException("The type '" + typeName + "' in '" + typeNamespace + "' could not be loaded.", ex);
                }
                EnsureAllowed(type);
                return type;
            }

            private static void EnsureAllowed(Type type)
            {
                if (typeof(Delegate).IsAssignableFrom(type))
                {
                    throw new SerializationException("The type '" + type.FullName + "' cannot be serialized.");
                }
            }
        }
    }
}
