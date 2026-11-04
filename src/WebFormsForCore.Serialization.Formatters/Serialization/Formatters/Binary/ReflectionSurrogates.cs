// webForCore: serialization of System.Type, the other reflection objects, Missing, Assembly and Module, in .NET
// Framework's wire format (#156).
//
// On .NET Framework, RuntimeType, RuntimeAssembly, RuntimeModule and Missing write themselves as a
// System.UnitySerializationHolder, and RuntimeMethodInfo, RuntimeConstructorInfo, RuntimeFieldInfo, RuntimePropertyInfo
// and RuntimeEventInfo as a System.Reflection.MemberInfoSerializationHolder; each reads back as the original object.
// .NET doesn't serialize them (only DBNull still writes its UnitySerializationHolder), so session state, view state and
// binary caches holding a Type field (Orchard 1.10's cache of NHibernate's Configuration) failed with "Type
// 'System.RuntimeType' ... is not marked as serializable". These surrogates write the members .NET Framework 4.8 writes,
// under its type and assembly names, and BinaryObjectReader binds the two holders to this port's copies of them
// (UnitySerializationHolder.cs, MemberInfoSerializationHolder.cs), so streams go both ways. Reference:
// microsoft/referencesource
//   mscorlib/system/unityserializationholder.cs (GetUnitySerializationInfo, AddElementTypes)
//   mscorlib/system/reflection/memberinfoserializationholder.cs (GetSerializationInfo)
//   mscorlib/system/rttype.cs, reflection/methodinfo.cs, ConstructorInfo.cs, fieldinfo.cs, propertyinfo.cs, eventinfo.cs,
//   assembly.cs, module.cs, missing.cs (GetObjectData, SerializationToString, FormatTypeName)
//   mscorlib/system/reflection/methodbase.cs (ConstructParameters)

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;

namespace WebFormsForCore.Serialization.Formatters.Binary
{
    internal static class ReflectionSurrogates
    {
        internal const string UnityHolderTypeName = "System.UnitySerializationHolder";
        internal const string MemberHolderTypeName = "System.Reflection.MemberInfoSerializationHolder";

        // The runtime's concrete reflection types, from objects of each kind (their names are .NET's own business).
        private static readonly Type s_runtimeType = typeof(object).GetType();
        private static readonly Type s_runtimeAssembly = typeof(object).Assembly.GetType();
        private static readonly Type s_runtimeModule = typeof(object).Module.GetType();
        private static readonly HashSet<Type> s_memberTypes = new HashSet<Type>
        {
            typeof(object).GetMethod(nameof(ToString), Type.EmptyTypes)!.GetType(),
            typeof(object).GetConstructor(Type.EmptyTypes)!.GetType(),
            typeof(string).GetField(nameof(string.Empty))!.GetType(),
            typeof(int).GetField(nameof(int.MaxValue))!.GetType(), // a literal field
            typeof(string).GetProperty(nameof(string.Length))!.GetType(),
            typeof(AppDomain).GetEvent(nameof(AppDomain.ProcessExit))!.GetType(),
        };

        private static readonly TypeSurrogate s_type = new TypeSurrogate();
        private static readonly MemberSurrogate s_member = new MemberSurrogate();
        private static readonly AssemblySurrogate s_assembly = new AssemblySurrogate();
        private static readonly ModuleSurrogate s_module = new ModuleSurrogate();
        private static readonly MissingSurrogate s_missing = new MissingSurrogate();

        internal static ISerializationSurrogate? GetSurrogate(Type type)
        {
            if (type == s_runtimeType)
            {
                return s_type;
            }
            if (s_memberTypes.Contains(type))
            {
                return s_member;
            }
            if (type == s_runtimeAssembly)
            {
                return s_assembly;
            }
            if (type == s_runtimeModule)
            {
                return s_module;
            }
            if (type == typeof(Missing))
            {
                return s_missing;
            }
            // The abstract types, as declared types of arrays and of null members: [Serializable] on .NET Framework, and
            // written as their objects are.
            if (type == typeof(Type) || type == typeof(TypeInfo))
            {
                return s_type;
            }
            if (type == typeof(MemberInfo) || type == typeof(MethodBase) || type == typeof(MethodInfo) || type == typeof(ConstructorInfo)
                || type == typeof(FieldInfo) || type == typeof(PropertyInfo) || type == typeof(EventInfo))
            {
                return s_member;
            }
            if (type == typeof(Assembly))
            {
                return s_assembly;
            }
            if (type == typeof(Module))
            {
                return s_module;
            }
            return null;
        }

        // The holders' names as .NET Framework writes them: the port's own classes read them.
        internal static Type? BindHolder(string? assemblyName, string typeName)
        {
            if (typeName != UnityHolderTypeName && typeName != MemberHolderTypeName)
            {
                return null;
            }
            if (assemblyName != null && !IsMscorlib(assemblyName))
            {
                return null;
            }
            return typeName == UnityHolderTypeName ? typeof(UnitySerializationHolder) : typeof(MemberInfoSerializationHolder);
        }

        // .NET's own UnitySerializationHolder (what a stream's name resolves to on .NET) reads only DBNull.
        internal static bool IsRuntimeUnityHolder(Type type) =>
            type.FullName == UnityHolderTypeName && type.Assembly == typeof(object).Assembly;

        internal static bool IsMscorlib(string assemblyName)
        {
            int comma = assemblyName.IndexOf(',');
            ReadOnlySpan<char> name = (comma < 0 ? assemblyName.AsSpan() : assemblyName.AsSpan(0, comma)).Trim();
            return name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase);
        }

        // ---- Names as .NET Framework has them ----

        // Where each type was on .NET Framework, for the types .NET moved: TypeForwardedFrom where .NET has it; the core
        // library's types are mscorlib's; this repository's System.Web and companions have .NET Framework's version
        // (FrameworkSurrogates.GetAssemblyName); otherwise the .NET Framework facade that .NET forwards the type from.
        private static readonly string[] s_frameworkFacades =
        {
            FrameworkSurrogates.SystemAssembly,
            "System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
            "System.Xml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
            "System.Data, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
            "System.Configuration, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
            "System.Runtime.Serialization, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
            "System.Numerics, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
            "System.Xml.Linq, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
            "System.ComponentModel.DataAnnotations, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35",
            "System.Transactions, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
            "System.Net.Http, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a",
        };
        private static readonly ConcurrentDictionary<Type, string> s_assemblyNames = new ConcurrentDictionary<Type, string>();

        // The assembly name .NET Framework writes for a type (for an array, pointer or by-ref type, its element type's).
        internal static string FrameworkAssemblyName(Type type)
        {
            while (type.HasElementType)
            {
                type = type.GetElementType()!;
            }
            if (type.IsGenericType)
            {
                type = type.GetGenericTypeDefinition();
            }
            return s_assemblyNames.GetOrAdd(type, static t =>
            {
                foreach (Attribute first in t.GetCustomAttributes(typeof(TypeForwardedFromAttribute), false))
                {
                    return ((TypeForwardedFromAttribute)first).AssemblyFullName;
                }
                Type outer = t;
                while (outer.DeclaringType != null)
                {
                    outer = outer.DeclaringType;
                }
                foreach (Attribute first in outer.GetCustomAttributes(typeof(TypeForwardedFromAttribute), false))
                {
                    return ((TypeForwardedFromAttribute)first).AssemblyFullName;
                }
                string name = FrameworkSurrogates.GetAssemblyName(t.Assembly);
                if (name != t.Assembly.FullName || t.FullName == null)
                {
                    return name;
                }
                foreach (string facade in s_frameworkFacades)
                {
                    if (ForwardsTo(facade, t))
                    {
                        return facade;
                    }
                }
                return name;
            });
        }

        private static bool ForwardsTo(string facade, Type type)
        {
            try
            {
                return Assembly.Load(new AssemblyName(facade)).GetType(type.FullName!, throwOnError: false) == type;
            }
            catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException)
            {
                return false;
            }
        }

        internal static string FrameworkAssemblyName(Assembly assembly) => FrameworkSurrogates.GetAssemblyName(assembly);

        // Type.FullName as .NET Framework has it: the generic arguments' assemblies are .NET Framework's. Null where
        // FullName is null (a type that contains generic parameters but isn't a generic type definition).
        internal static string? FrameworkFullName(Type type)
        {
            if (type.FullName == null)
            {
                return null;
            }
            var builder = new StringBuilder();
            AppendFullName(builder, type);
            return builder.ToString();
        }

        private static void AppendFullName(StringBuilder builder, Type type)
        {
            if (type.HasElementType)
            {
                AppendFullName(builder, type.GetElementType()!);
                AppendElementSuffix(builder, type);
                return;
            }
            if (!type.IsGenericType || type.IsGenericTypeDefinition)
            {
                builder.Append(type.FullName);
                return;
            }
            builder.Append(type.GetGenericTypeDefinition().FullName).Append('[');
            Type[] arguments = type.GetGenericArguments();
            for (int i = 0; i < arguments.Length; i++)
            {
                builder.Append(i == 0 ? "[" : ",[");
                AppendFullName(builder, arguments[i]);
                builder.Append(", ").Append(FrameworkAssemblyName(arguments[i])).Append(']');
            }
            builder.Append(']');
        }

        private static void AppendElementSuffix(StringBuilder builder, Type type)
        {
            if (type.IsSZArray)
            {
                builder.Append("[]");
            }
            else if (type.IsArray)
            {
                int rank = type.GetArrayRank();
                builder.Append(rank == 1 ? "[*]" : "[" + new string(',', rank - 1) + "]");
            }
            else if (type.IsPointer)
            {
                builder.Append('*');
            }
            else if (type.IsByRef)
            {
                builder.Append('&');
            }
        }

        // RuntimeType.FormatTypeName(true): the type's name in TypeNameFormatFlags.FormatSerialization (namespace, "!" and
        // "!!" for type and method generic parameters, generic arguments with their assemblies), which .NET Framework
        // matches overloads by.
        internal static string SerializationTypeName(Type type)
        {
            var builder = new StringBuilder();
            AppendSerializationName(builder, type, genericParameterMarks: true);
            return builder.ToString();
        }

        private static void AppendSerializationName(StringBuilder builder, Type type, bool genericParameterMarks)
        {
            if (type.HasElementType)
            {
                AppendSerializationName(builder, type.GetElementType()!, genericParameterMarks);
                AppendElementSuffix(builder, type);
                return;
            }
            if (type.IsGenericParameter)
            {
                if (genericParameterMarks)
                {
                    builder.Append(type.DeclaringMethod != null ? "!!" : "!");
                }
                builder.Append(type.Name);
                return;
            }
            if (!type.IsGenericType || type.IsGenericTypeDefinition)
            {
                builder.Append(type.FullName ?? type.Name);
                return;
            }
            builder.Append(type.GetGenericTypeDefinition().FullName);
            AppendInstantiation(builder, type.GetGenericArguments());
        }

        // TypeString::AppendInst with FormatSerialization: an argument that is a generic parameter is written by name,
        // the others with their assembly, in brackets.
        private static void AppendInstantiation(StringBuilder builder, Type[] arguments)
        {
            builder.Append('[');
            for (int i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                Type argument = arguments[i];
                if (argument.IsGenericParameter)
                {
                    AppendSerializationName(builder, argument, genericParameterMarks: false);
                }
                else
                {
                    builder.Append('[');
                    AppendSerializationName(builder, argument, genericParameterMarks: true);
                    builder.Append(", ").Append(FrameworkAssemblyName(argument)).Append(']');
                }
            }
            builder.Append(']');
        }

        // MethodBase.ConstructParameters(..., serialization: true).
        private static void AppendParameters(StringBuilder builder, ParameterInfo[] parameters, CallingConventions callingConvention)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                AppendSerializationName(builder, parameters[i].ParameterType, genericParameterMarks: true);
            }
            if ((callingConvention & CallingConventions.VarArgs) == CallingConventions.VarArgs)
            {
                builder.Append(parameters.Length > 0 ? ", ..." : "...");
            }
        }

        // RuntimeMethodInfo, RuntimeConstructorInfo and RuntimePropertyInfo.SerializationToString(): .NET Framework's
        // "Signature2", null for the members that have none.
        internal static string? SerializationToString(MemberInfo member)
        {
            var builder = new StringBuilder();
            switch (member)
            {
                case MethodInfo method:
                    AppendSerializationName(builder, method.ReturnType, genericParameterMarks: true);
                    builder.Append(' ').Append(method.Name);
                    if (method.IsGenericMethod)
                    {
                        AppendInstantiation(builder, method.GetGenericArguments());
                    }
                    builder.Append('(');
                    AppendParameters(builder, method.GetParameters(), method.CallingConvention);
                    builder.Append(')');
                    return builder.ToString();
                case ConstructorInfo constructor:
                    builder.Append(constructor.Name).Append('(');
                    AppendParameters(builder, constructor.GetParameters(), constructor.CallingConvention);
                    builder.Append(')');
                    return builder.ToString();
                case PropertyInfo property:
                    AppendSerializationName(builder, property.PropertyType, genericParameterMarks: true);
                    builder.Append(' ').Append(property.Name);
                    ParameterInfo[] index = property.GetIndexParameters();
                    if (index.Length > 0)
                    {
                        builder.Append(" [");
                        AppendParameters(builder, index, CallingConventions.Standard);
                        builder.Append(']');
                    }
                    return builder.ToString();
                default:
                    return null;
            }
        }

        // UnitySerializationHolder.GetUnitySerializationInfo(info, unityType, data, assembly).
        internal static void SetUnityInfo(SerializationInfo info, int unityType, string? data, string? assemblyName)
        {
            info.FullTypeName = UnityHolderTypeName;
            info.AssemblyName = FrameworkSurrogates.Mscorlib;
            info.AddValue("Data", data, typeof(string));
            info.AddValue("UnityType", unityType);
            info.AddValue("AssemblyName", assemblyName ?? string.Empty);
        }

        // RuntimeType.GetObjectData: UnitySerializationHolder.GetUnitySerializationInfo(info, RuntimeType).
        private sealed class TypeSurrogate : ISerializationSurrogate
        {
            public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
            {
                var type = (Type)obj;
                Type root = type;
                while (root.HasElementType)
                {
                    root = root.GetElementType()!;
                }

                if (root.IsGenericParameter)
                {
                    type = AddElementTypes(info, type);
                    info.FullTypeName = UnityHolderTypeName;
                    info.AssemblyName = FrameworkSurrogates.Mscorlib;
                    info.AddValue("UnityType", UnitySerializationHolder.GenericParameterTypeUnity);
                    info.AddValue("GenericParameterPosition", type.GenericParameterPosition);
                    info.AddValue("DeclaringMethod", type.DeclaringMethod, typeof(MethodBase));
                    info.AddValue("DeclaringType", type.DeclaringType, typeof(Type));
                    return;
                }

                int unityType = UnitySerializationHolder.RuntimeTypeUnity;
                if (!type.IsGenericTypeDefinition && type.ContainsGenericParameters)
                {
                    unityType = UnitySerializationHolder.PartialInstantiationTypeUnity;
                    type = AddElementTypes(info, type);
                    info.AddValue("GenericArguments", type.GetGenericArguments(), typeof(Type[]));
                    type = type.GetGenericTypeDefinition();
                }

                SetUnityInfo(info, unityType, FrameworkFullName(type), FrameworkAssemblyName(type));
            }

            private static Type AddElementTypes(SerializationInfo info, Type type)
            {
                var elementTypes = new List<int>();
                while (type.HasElementType)
                {
                    if (type.IsSZArray)
                    {
                        elementTypes.Add(UnitySerializationHolder.SzArray);
                    }
                    else if (type.IsArray)
                    {
                        elementTypes.Add(type.GetArrayRank());
                        elementTypes.Add(UnitySerializationHolder.Array);
                    }
                    else if (type.IsPointer)
                    {
                        elementTypes.Add(UnitySerializationHolder.Pointer);
                    }
                    else if (type.IsByRef)
                    {
                        elementTypes.Add(UnitySerializationHolder.ByRef);
                    }
                    type = type.GetElementType()!;
                }
                info.AddValue("ElementTypes", elementTypes.ToArray(), typeof(int[]));
                return type;
            }

            public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector) =>
                throw new NotSupportedException(UnitySerializationHolder.NotSupportedUnitySerHolder);
        }

        // The members' GetObjectData: MemberInfoSerializationHolder.GetSerializationInfo.
        private sealed class MemberSurrogate : ISerializationSurrogate
        {
            public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
            {
                var member = (MemberInfo)obj;
                Type reflectedType = member.ReflectedType
                    ?? throw new NotSupportedException(MemberInfoSerializationHolder.NotSupportedGlobalMethodSerialization);

                string? signature;
                MemberTypes memberType;
                Type[]? genericArguments = null;
                switch (member)
                {
                    case MethodInfo method:
                        signature = method.ToString();
                        memberType = MemberTypes.Method;
                        if (method.IsGenericMethod && !method.IsGenericMethodDefinition)
                        {
                            genericArguments = method.GetGenericArguments();
                        }
                        break;
                    case ConstructorInfo constructor:
                        signature = constructor.ToString();
                        memberType = MemberTypes.Constructor;
                        break;
                    case PropertyInfo property:
                        signature = property.ToString();
                        memberType = MemberTypes.Property;
                        break;
                    case FieldInfo field:
                        signature = field.ToString();
                        memberType = MemberTypes.Field;
                        break;
                    case EventInfo:
                        signature = null;
                        memberType = MemberTypes.Event;
                        break;
                    default:
                        throw new SerializationException(SR.Format(SR.Serialization_NonSerType, obj.GetType().FullName, obj.GetType().Assembly.FullName));
                }

                info.FullTypeName = MemberHolderTypeName;
                info.AssemblyName = FrameworkSurrogates.Mscorlib;
                info.AddValue("Name", member.Name, typeof(string));
                info.AddValue("AssemblyName", FrameworkAssemblyName(reflectedType), typeof(string));
                info.AddValue("ClassName", FrameworkFullName(reflectedType), typeof(string));
                info.AddValue("Signature", signature, typeof(string));
                info.AddValue("Signature2", SerializationToString(member), typeof(string));
                info.AddValue("MemberType", (int)memberType);
                info.AddValue("GenericArguments", genericArguments, typeof(Type[]));
            }

            public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector) =>
                throw new NotSupportedException(MemberInfoSerializationHolder.NotSupportedMethod);
        }

        // RuntimeAssembly.GetObjectData: the assembly's name as data and as the assembly name.
        private sealed class AssemblySurrogate : ISerializationSurrogate
        {
            public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
            {
                string name = FrameworkAssemblyName((Assembly)obj);
                SetUnityInfo(info, UnitySerializationHolder.AssemblyUnity, name, name);
            }

            public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector) =>
                throw new NotSupportedException(UnitySerializationHolder.NotSupportedUnitySerHolder);
        }

        // RuntimeModule.GetObjectData: the module's ScopeName and its assembly. mscorlib's module was
        // "CommonLanguageRuntimeLibrary".
        private sealed class ModuleSurrogate : ISerializationSurrogate
        {
            public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
            {
                var module = (Module)obj;
                string scopeName = module.Assembly == typeof(object).Assembly ? UnitySerializationHolder.MscorlibScopeName : module.ScopeName;
                SetUnityInfo(info, UnitySerializationHolder.ModuleUnity, scopeName, FrameworkAssemblyName(module.Assembly));
            }

            public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector) =>
                throw new NotSupportedException(UnitySerializationHolder.NotSupportedUnitySerHolder);
        }

        // Missing.GetObjectData: UnitySerializationHolder.GetUnitySerializationInfo(info, Missing), the unity type alone.
        private sealed class MissingSurrogate : ISerializationSurrogate
        {
            public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
            {
                info.FullTypeName = UnityHolderTypeName;
                info.AssemblyName = FrameworkSurrogates.Mscorlib;
                info.AddValue("UnityType", UnitySerializationHolder.MissingUnity);
            }

            public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector) =>
                throw new NotSupportedException(UnitySerializationHolder.NotSupportedUnitySerHolder);
        }
    }
}
