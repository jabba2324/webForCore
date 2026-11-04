// ==++==
//
//   Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ==--==
//
// webForCore: microsoft/referencesource mscorlib/system/reflection/memberinfoserializationholder.cs (MIT), the reading
// half, for the System.Reflection.MemberInfoSerializationHolder records .NET Framework writes for MethodInfo,
// ConstructorInfo, FieldInfo, PropertyInfo and EventInfo (#156). BinaryObjectReader binds that name (in mscorlib) to this
// class. The writing half is ReflectionSurrogates (webForCore's). Changes, each marked "webForCore:": the reflected type
// is found as the rest of the reader finds types; the members' SerializationToString is ReflectionSurrogates'; the
// optional members are read as SerializationInfo.GetValueNoThrow did; the messages are mscorlib.txt's.

using System.Reflection;
using System.Runtime.Serialization;

namespace WebFormsForCore.Serialization.Formatters.Binary
{
    [Serializable]
    internal class MemberInfoSerializationHolder : ISerializable, IObjectReference
    {
        // webForCore: mscorlib.txt's messages.
        internal const string NotSupportedMethod = "Method is not supported.";
        internal const string NotSupportedGlobalMethodSerialization = "Serialization of global methods (including implicit serialization via the use of asynchronous delegates) is not supported.";
        private const string InsufficientState = "Insufficient state to return the real object.";
        private const string NullSignature = "The method signature cannot be null.";
        private const string UnknownMember = "Cannot get the member '{0}'.";
        private const string MemberTypeNotRecognized = "Unknown member type.";

        #region Private Data Members
        private string? m_memberName;
        private Type? m_reflectedType;
        private string? m_signature;
        private string? m_signature2;
        private MemberTypes m_memberType;
        private SerializationInfo m_info;
        #endregion

        #region Constructor
        internal MemberInfoSerializationHolder(SerializationInfo info, StreamingContext context)
        {
            ArgumentNullException.ThrowIfNull(info);

            string? assemblyName = info.GetString("AssemblyName");
            string? typeName = info.GetString("ClassName");

            if (assemblyName == null || typeName == null)
                throw new SerializationException(InsufficientState);

            // webForCore: as the reader finds types.
            m_reflectedType = ObjectReader.GetTypeFromAssemblyName(assemblyName, typeName);
            m_memberName = info.GetString("Name");
            m_signature = info.GetString("Signature");
            m_signature2 = (string?)GetValueNoThrow(info, "Signature2");
            m_memberType = (MemberTypes)info.GetInt32("MemberType");
            m_info = info;
        }
        #endregion

        // webForCore: SerializationInfo.GetValueNoThrow is internal to .NET; the members are enumerated instead.
        private static object? GetValueNoThrow(SerializationInfo info, string name)
        {
            foreach (SerializationEntry entry in info)
            {
                if (entry.Name == name)
                    return entry.Value;
            }
            return null;
        }

        // webForCore: RuntimeMethodInfo, RuntimeConstructorInfo and RuntimePropertyInfo.SerializationToString().
        private static string? SerializationToString(MemberInfo member) => ReflectionSurrogates.SerializationToString(member);

        #region ISerializable
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            throw new NotSupportedException(NotSupportedMethod);
        }
        #endregion

        #region IObjectReference
        public virtual object? GetRealObject(StreamingContext context)
        {
            if (m_memberName == null || m_reflectedType == null || m_memberType == 0)
                throw new SerializationException(InsufficientState);

            BindingFlags bindingFlags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.OptionalParamBinding;

            switch (m_memberType)
            {
                #region case MemberTypes.Field:
                case MemberTypes.Field:
                {
                    FieldInfo[] fields = (FieldInfo[])m_reflectedType.GetMember(m_memberName, MemberTypes.Field, bindingFlags);

                    if (fields.Length == 0)
                        throw new SerializationException(string.Format(UnknownMember, m_memberName));

                    return fields[0];
                }
                #endregion

                #region case MemberTypes.Event:
                case MemberTypes.Event:
                {
                    EventInfo[] events = (EventInfo[])m_reflectedType.GetMember(m_memberName, MemberTypes.Event, bindingFlags);

                    if (events.Length == 0)
                        throw new SerializationException(string.Format(UnknownMember, m_memberName));

                    return events[0];
                }
                #endregion

                #region case MemberTypes.Property:
                case MemberTypes.Property:
                {
                    PropertyInfo[] properties = (PropertyInfo[])m_reflectedType.GetMember(m_memberName, MemberTypes.Property, bindingFlags);

                    if (properties.Length == 0)
                        throw new SerializationException(string.Format(UnknownMember, m_memberName));

                    if (properties.Length == 1)
                        return properties[0];

                    if (properties.Length > 1)
                    {
                        for (int i = 0; i < properties.Length; i++)
                        {
                            if (m_signature2 != null)
                            {
                                if (SerializationToString(properties[i])!.Equals(m_signature2))
                                    return properties[i];
                            }
                            else
                            {
                                if ((properties[i]).ToString()!.Equals(m_signature))
                                    return properties[i];
                            }
                        }
                    }

                    throw new SerializationException(string.Format(UnknownMember, m_memberName));
                }
                #endregion

                #region case MemberTypes.Constructor:
                case MemberTypes.Constructor:
                {
                    if (m_signature == null)
                        throw new SerializationException(NullSignature);

                    ConstructorInfo[] constructors = (ConstructorInfo[])m_reflectedType.GetMember(m_memberName, MemberTypes.Constructor, bindingFlags);

                    if (constructors.Length == 1)
                        return constructors[0];

                    if (constructors.Length > 1)
                    {
                        for (int i = 0; i < constructors.Length; i++)
                        {
                            if (m_signature2 != null)
                            {
                                if (SerializationToString(constructors[i])!.Equals(m_signature2))
                                    return constructors[i];
                            }
                            else
                            {
                                if (constructors[i].ToString()!.Equals(m_signature))
                                    return constructors[i];
                            }
                        }
                    }

                    throw new SerializationException(string.Format(UnknownMember, m_memberName));
                }
                #endregion

                #region case MemberTypes.Method:
                case MemberTypes.Method:
                {
                    MethodInfo? methodInfo = null;

                    if (m_signature == null)
                        throw new SerializationException(NullSignature);

                    Type[]? genericArguments = GetValueNoThrow(m_info, "GenericArguments") as Type[];

                    // webForCore: the generic arguments are objects of the graph too, and may not be complete yet; null
                    // tells the ObjectManager to come back later (as below), where MakeGenericMethod would throw.
                    if (genericArguments != null && System.Array.IndexOf(genericArguments, null) >= 0)
                        return null;

                    MethodInfo[] methods = (MethodInfo[])m_reflectedType.GetMember(m_memberName, MemberTypes.Method, bindingFlags);

                    if (methods.Length == 1)
                        methodInfo = methods[0];

                    else if (methods.Length > 1)
                    {
                        for (int i = 0; i < methods.Length; i++)
                        {
                            if (m_signature2 != null)
                            {
                                if (SerializationToString(methods[i])!.Equals(m_signature2))
                                {
                                    methodInfo = methods[i];
                                    break;
                                }
                            }
                            else
                            {

                                if (methods[i].ToString()!.Equals(m_signature))
                                {
                                    methodInfo = methods[i];
                                    break;
                                }
                            }

                            if (genericArguments != null && methods[i].IsGenericMethod)
                            {
                                if (methods[i].GetGenericArguments().Length == genericArguments.Length)
                                {
                                    MethodInfo candidateMethod = methods[i].MakeGenericMethod(genericArguments);

                                    if (m_signature2 != null)
                                    {
                                        if (SerializationToString(candidateMethod)!.Equals(m_signature2))
                                        {
                                            methodInfo = candidateMethod;
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        if (candidateMethod.ToString()!.Equals(m_signature))
                                        {
                                            methodInfo = candidateMethod;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (methodInfo == null)
                        throw new SerializationException(string.Format(UnknownMember, m_memberName));

                    if (!methodInfo.IsGenericMethodDefinition)
                        return methodInfo;

                    if (genericArguments == null)
                        return methodInfo;

                    if (genericArguments[0] == null)
                        return null;

                    return methodInfo.MakeGenericMethod(genericArguments);
                }
                #endregion

                default:
                    throw new ArgumentException(MemberTypeNotRecognized);
            }
        }
        #endregion
    }
}
