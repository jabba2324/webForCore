// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
//
// webForCore: microsoft/referencesource mscorlib/system/unityserializationholder.cs (MIT), the reading half, for the
// System.UnitySerializationHolder records .NET Framework writes for Type, DBNull, Missing, Empty, Module and Assembly
// (#156). BinaryObjectReader binds that name (in mscorlib) to this class; .NET's own UnitySerializationHolder reads only
// DBNull. The writing half is ReflectionSurrogates (webForCore's). Changes, each marked "webForCore:": the assembly and
// the type are found as the rest of the reader finds them; mscorlib's assembly and module are .NET's core library's;
// .NET has no System.Empty; the messages are mscorlib.txt's.

using System.Reflection;
using System.Runtime.Serialization;

namespace WebFormsForCore.Serialization.Formatters.Binary
{
    [Serializable]
    internal class UnitySerializationHolder : ISerializable, IObjectReference
    {
        #region Internal Constants
        internal const int EmptyUnity       = 0x0001;
        internal const int NullUnity        = 0x0002;
        internal const int MissingUnity     = 0x0003;
        internal const int RuntimeTypeUnity = 0x0004;
        internal const int ModuleUnity      = 0x0005;
        internal const int AssemblyUnity    = 0x0006;
        internal const int GenericParameterTypeUnity = 0x0007;
        internal const int PartialInstantiationTypeUnity = 0x0008;

        internal const int Pointer          = 0x0001;
        internal const int Array            = 0x0002;
        internal const int SzArray          = 0x0003;
        internal const int ByRef            = 0x0004;

        // webForCore: mscorlib.txt's messages.
        internal const string NotSupportedUnitySerHolder = "The UnitySerializationHolder object is designed to transmit information about other types and is not serializable itself.";
        private const string InsufficientDeserializationState = "Insufficient state to deserialize the object. Missing field '{0}'. More information is needed.";
        private const string UnableToFindModule = "The given module {0} cannot be found within the assembly {1}.";
        private const string InvalidUnity = "Invalid Unity type.";
        // webForCore: mscorlib's module on .NET Framework.
        internal const string MscorlibScopeName = "CommonLanguageRuntimeLibrary";
        #endregion

        #region Private Data Members
        private Type[]? m_instantiation;
        private int[]? m_elementTypes;
        private int m_genericParameterPosition;
        private Type? m_declaringType;
        private MethodBase? m_declaringMethod;
        private string? m_data;
        private string? m_assemblyName;
        private int m_unityType;
        #endregion

        internal Type MakeElementTypes(Type type)
        {
            for (int i = m_elementTypes!.Length - 1; i >= 0; i --)
            {
                if (m_elementTypes[i] == SzArray)
                {
                    type = type.MakeArrayType();
                }
                else if (m_elementTypes[i] == Array)
                {
                    type = type.MakeArrayType(m_elementTypes[--i]);
                }
                else if ((m_elementTypes[i] == Pointer))
                {
                    type = type.MakePointerType();
                }
                else if ((m_elementTypes[i] == ByRef))
                {
                    type = type.MakeByRefType();
                }
            }

            return type;
        }

        #region Constructor
        internal UnitySerializationHolder(SerializationInfo info, StreamingContext context)
        {
            ArgumentNullException.ThrowIfNull(info);

            m_unityType = info.GetInt32("UnityType");

            if (m_unityType == MissingUnity)
                return;

            if (m_unityType == GenericParameterTypeUnity)
            {
                m_declaringMethod = info.GetValue("DeclaringMethod", typeof(MethodBase)) as MethodBase;
                m_declaringType = info.GetValue("DeclaringType", typeof(Type)) as Type;
                m_genericParameterPosition = info.GetInt32("GenericParameterPosition");
                m_elementTypes = info.GetValue("ElementTypes", typeof(int[])) as int[];

                return;
            }

            if (m_unityType == PartialInstantiationTypeUnity)
            {
                m_instantiation = info.GetValue("GenericArguments", typeof(Type[])) as Type[];
                m_elementTypes = info.GetValue("ElementTypes", typeof(int[])) as int[];
            }

            m_data = info.GetString("Data");
            m_assemblyName = info.GetString("AssemblyName");
        }
        #endregion

        #region Private Methods
        private void ThrowInsufficientInformation(string field)
        {
            throw new SerializationException(string.Format(InsufficientDeserializationState, field));
        }
        #endregion

        #region ISerializable
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            throw new NotSupportedException(NotSupportedUnitySerHolder);
        }
        #endregion

        #region IObjectReference
        public virtual object? GetRealObject(StreamingContext context)
        {
            Assembly assembly;

            switch (m_unityType)
            {
                case EmptyUnity:
                {
                    // webForCore: .NET has no System.Empty (the VT_EMPTY variant of COM interop).
                    throw new SerializationException(InvalidUnity + " (System.Empty)");
                }

                case NullUnity:
                {
                    return DBNull.Value;
                }

                case MissingUnity:
                {
                    return Missing.Value;
                }

                case PartialInstantiationTypeUnity:
                {
                    m_unityType = RuntimeTypeUnity;
                    Type? definition = GetRealObject(context) as Type;
                    m_unityType = PartialInstantiationTypeUnity;

                    if (m_instantiation![0] == null)
                        return null;

                    return MakeElementTypes(definition!.MakeGenericType(m_instantiation));
                }

                case GenericParameterTypeUnity:
                {
                    if (m_declaringMethod == null && m_declaringType == null)
                        ThrowInsufficientInformation("DeclaringMember");

                    if (m_declaringMethod != null)
                        return m_declaringMethod.GetGenericArguments()[m_genericParameterPosition];

                    return MakeElementTypes(m_declaringType!.GetGenericArguments()[m_genericParameterPosition]);
                }

                case RuntimeTypeUnity:
                {
                    if (m_data == null || m_data.Length == 0)
                        ThrowInsufficientInformation("Data");

                    if (m_assemblyName == null)
                        ThrowInsufficientInformation("AssemblyName");

                    if (m_assemblyName!.Length == 0)
                        return Type.GetType(m_data!, true, false);

                    // webForCore: as the reader finds the types of objects (a .NET Framework assembly name may
                    // name a newer assembly, or a facade that forwards the type).
                    return ObjectReader.GetTypeFromAssemblyName(m_assemblyName, m_data!);
                }

                case ModuleUnity:
                {
                    if (m_data == null || m_data.Length == 0)
                        ThrowInsufficientInformation("Data");

                    if (m_assemblyName == null)
                        ThrowInsufficientInformation("AssemblyName");

                    assembly = LoadAssembly(m_assemblyName!);

                    Module? namedModule = assembly.GetModule(m_data!);

                    // webForCore: mscorlib's module is the core library's.
                    if (namedModule == null && assembly == typeof(object).Assembly && m_data == MscorlibScopeName)
                        namedModule = typeof(object).Module;

                    if (namedModule == null)
                        throw new SerializationException(string.Format(UnableToFindModule, m_data, m_assemblyName));

                    return namedModule;
                }

                case AssemblyUnity:
                {
                    if (m_data == null || m_data.Length == 0)
                        ThrowInsufficientInformation("Data");

                    if (m_assemblyName == null)
                        ThrowInsufficientInformation("AssemblyName");

                    assembly = LoadAssembly(m_assemblyName!);

                    return assembly;
                }

                default:
                    throw new ArgumentException(InvalidUnity);
            }
        }
        #endregion

        // webForCore: mscorlib is .NET's core library (Assembly.Load would give its facade), and other assemblies load as
        // the reader loads them.
        private static Assembly LoadAssembly(string assemblyName)
        {
            if (ReflectionSurrogates.IsMscorlib(assemblyName))
                return typeof(object).Assembly;
            return ObjectReader.LoadAssembly(assemblyName);
        }
    }
}
