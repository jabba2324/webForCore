// webForCore: serialization of .NET Framework types that .NET no longer serializes, in .NET Framework's wire format.
//
// .NET's NameObjectCollectionBase (so NameValueCollection, HttpValueCollection, HttpCookie.Values, ...) throws
// PlatformNotSupportedException from GetObjectData, its serialization constructor and OnDeserialization, and .NET's
// CaseInsensitiveComparer, CaseInsensitiveHashCodeProvider, TextInfo and CultureInfo are not serializable. .NET Framework writes all of
// them (a default NameValueCollection carries a CaseInsensitiveHashCodeProvider and a CaseInsensitiveComparer), and
// System.Web stores them through BinaryFormatter (session state, view state, output cache); MVC's ModelState carries a
// CultureInfo in each ValueProviderResult.
//
// These surrogates are consulted after the caller's SurrogateSelector and write the members .NET Framework 4.8 writes,
// under its type and assembly names, so streams go both ways. Reference: microsoft/referencesource
//   System/compmod/system/collections/specialized/nameobjectcollectionbase.cs (GetObjectData, OnDeserialization)
//   mscorlib/system/collections/caseinsensitivecomparer.cs, caseinsensitivehashcodeprovider.cs
//   mscorlib/system/globalization/textinfo.cs (serialized fields, OnSerializing, OnDeserialized)
//   mscorlib/system/globalization/cultureinfo.cs (serialized fields, OnSerializing, OnDeserialized)

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

#pragma warning disable CS0618 // IHashCodeProvider, CaseInsensitiveHashCodeProvider: obsolete, but what .NET Framework writes

namespace WebFormsForCore.Serialization.Formatters.Binary
{
    // A surrogate whose objects need completing after the whole graph is fixed up, as IDeserializationCallback objects do.
    internal interface IDeserializationCallbackSurrogate
    {
        void OnDeserialization(object obj, object? sender);
    }

    // Asks the caller's selector first, then the built-in surrogates.
    internal sealed class FrameworkSurrogateSelector : ISurrogateSelector
    {
        private readonly ISurrogateSelector? _next;

        private FrameworkSurrogateSelector(ISurrogateSelector? next)
        {
            _next = next;
        }

        internal static ISurrogateSelector Wrap(ISurrogateSelector? selector) => new FrameworkSurrogateSelector(selector);

        public ISerializationSurrogate? GetSurrogate(Type type, StreamingContext context, out ISurrogateSelector selector)
        {
            ArgumentNullException.ThrowIfNull(type);

            if (_next != null)
            {
                ISerializationSurrogate? surrogate = _next.GetSurrogate(type, context, out selector);
                if (surrogate != null)
                {
                    return surrogate;
                }
            }

            selector = this;
            return FrameworkSurrogates.GetSurrogate(type);
        }

        public void ChainSelector(ISurrogateSelector selector) => throw new NotSupportedException();

        public ISurrogateSelector? GetNextSelector() => _next;
    }

    internal static class FrameworkSurrogates
    {
        internal const string Mscorlib = "mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
        internal const string SystemAssembly = "System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

        private static readonly NameObjectCollectionSurrogate s_collection = new NameObjectCollectionSurrogate();
        private static readonly CaseInsensitiveComparerSurrogate s_comparer = new CaseInsensitiveComparerSurrogate();
        private static readonly CaseInsensitiveHashCodeProviderSurrogate s_hashProvider = new CaseInsensitiveHashCodeProviderSurrogate();
        private static readonly TextInfoSurrogate s_textInfo = new TextInfoSurrogate();
        private static readonly CultureInfoSurrogate s_culture = new CultureInfoSurrogate();
        private static readonly CultureMemberSurrogate s_cultureMember = new CultureMemberSurrogate();

        // The culture of each CaseInsensitiveComparer and CaseInsensitiveHashCodeProvider these surrogates read or write;
        // .NET keeps it in a private field.
        internal static readonly ConditionalWeakTable<object, CultureInfo> s_cultures = new ConditionalWeakTable<object, CultureInfo>();

        internal static ISerializationSurrogate? GetSurrogate(Type type)
        {
            if (type == typeof(CaseInsensitiveComparer))
            {
                return s_comparer;
            }
            if (type == typeof(CaseInsensitiveHashCodeProvider))
            {
                return s_hashProvider;
            }
            if (type == typeof(TextInfo))
            {
                return s_textInfo;
            }
            if (type == typeof(CultureInfo))
            {
                return s_culture;
            }
            if (CultureMemberSurrogate.Handles(type))
            {
                return s_cultureMember;
            }
            if (NameObjectCollectionSurrogate.Handles(type))
            {
                return s_collection;
            }
            return null;
        }

        // .NET's NameObjectCollectionBase.OnDeserialization and TextInfo's IDeserializationCallback throw
        // PlatformNotSupportedException. The surrogates complete those objects instead (NameObjectCollectionSurrogate's
        // OnDeserialization; TextInfoSurrogate replaces the TextInfo).
        internal static bool IsUnsupportedDeserializationCallback(object obj) =>
            obj is TextInfo || (obj is NameObjectCollectionBase && NameObjectCollectionSurrogate.Handles(obj.GetType()));

        // The .NET Framework 4.x assemblies this repository builds carry upstream's version (11.0.0.0, so they win over
        // .NET's 4.0.0.0 facades of the same name); on the wire they are written with .NET Framework's version, as
        // System.Web's types were. Other assemblies (the Web Stack's keep their own versions) are written as they are.
        private static readonly HashSet<string> s_frameworkAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Web", "System.Web.ApplicationServices", "System.Web.DataVisualization", "System.Web.DynamicData",
            "System.Web.Entity", "System.Web.Extensions", "System.Web.Mobile", "System.Web.RegularExpressions",
            "System.Web.Services", "System.Drawing",
        };
        private static readonly ConcurrentDictionary<Assembly, string> s_assemblyNames = new ConcurrentDictionary<Assembly, string>();

        internal static string GetAssemblyName(Assembly assembly) => s_assemblyNames.GetOrAdd(assembly, static a =>
        {
            AssemblyName name = a.GetName();
            byte[]? token = name.GetPublicKeyToken();
            if (name.Name != null && s_frameworkAssemblies.Contains(name.Name) && name.Version?.Major > 4 && token is { Length: 8 })
            {
                return $"{name.Name}, Version=4.0.0.0, Culture=neutral, PublicKeyToken={Convert.ToHexStringLower(token)}";
            }
            return a.FullName!;
        });

        internal static CultureInfo CultureOf(object comparer, string field)
        {
            if (s_cultures.TryGetValue(comparer, out CultureInfo? culture))
            {
                return culture;
            }
            // Created by application code: .NET keeps the culture's CompareInfo in a private field.
            FieldInfo? fi = comparer.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi?.GetValue(comparer) is CompareInfo compareInfo)
            {
                return CultureInfo.GetCultureInfo(compareInfo.Name);
            }
            throw new SerializationException(SR.Format(SR.Serialization_NonSerType, comparer.GetType().FullName, comparer.GetType().Assembly.FullName));
        }

        internal static CultureInfo Remember(object comparer, CultureInfo culture)
        {
            s_cultures.AddOrUpdate(comparer, culture);
            return culture;
        }
    }

    // .NET Framework: [Serializable] class CaseInsensitiveComparer { private CompareInfo m_compareInfo; }
    internal sealed class CaseInsensitiveComparerSurrogate : ISerializationSurrogate
    {
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            CultureInfo culture = FrameworkSurrogates.CultureOf(obj, "_compareInfo");
            info.FullTypeName = "System.Collections.CaseInsensitiveComparer";
            info.AssemblyName = FrameworkSurrogates.Mscorlib;
            info.AddValue("m_compareInfo", culture.CompareInfo, typeof(CompareInfo));
        }

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector)
        {
            // CompareInfo is deserialized by .NET itself; its Name is set only by its [OnDeserialized] callback, which runs
            // after this, so take the serialized m_name.
            object? compareInfo = info.GetValue("m_compareInfo", typeof(object));
            string name = CompareInfoName(compareInfo);
            CultureInfo culture = CultureInfo.GetCultureInfo(name);
            var comparer = new CaseInsensitiveComparer(culture);
            FrameworkSurrogates.Remember(comparer, culture);
            return comparer;
        }

        internal static string CompareInfoName(object? compareInfo)
        {
            if (compareInfo is CompareInfo ci)
            {
                FieldInfo? nameField = typeof(CompareInfo).GetField("m_name", BindingFlags.Instance | BindingFlags.NonPublic);
                if (nameField?.GetValue(ci) is string name)
                {
                    return name;
                }
                return ci.Name;
            }
            throw new SerializationException();
        }
    }

    // .NET Framework: [Serializable] class CaseInsensitiveHashCodeProvider { private TextInfo m_text; }
    internal sealed class CaseInsensitiveHashCodeProviderSurrogate : ISerializationSurrogate
    {
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            CultureInfo culture = FrameworkSurrogates.CultureOf(obj, "_compareInfo");
            info.FullTypeName = "System.Collections.CaseInsensitiveHashCodeProvider";
            info.AssemblyName = FrameworkSurrogates.Mscorlib;
            info.AddValue("m_text", culture.TextInfo, typeof(TextInfo));
        }

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector)
        {
            // m_text was read by TextInfoSurrogate, which returns the culture's TextInfo.
            if (info.GetValue("m_text", typeof(object)) is not TextInfo text)
            {
                throw new SerializationException();
            }
            CultureInfo culture = CultureInfo.GetCultureInfo(text.CultureName);
            var provider = new CaseInsensitiveHashCodeProvider(culture);
            FrameworkSurrogates.Remember(provider, culture);
            return provider;
        }
    }

    // .NET Framework 4.8 TextInfo serializes these fields (textinfo.cs); its OnSerializing copies m_cultureName into
    // customCultureName for 2.0 readers, and its OnDeserialized prefers m_cultureName, then customCultureName, then the LCID.
    internal sealed class TextInfoSurrogate : ISerializationSurrogate
    {
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            var text = (TextInfo)obj;
            info.FullTypeName = "System.Globalization.TextInfo";
            info.AssemblyName = FrameworkSurrogates.Mscorlib;
            info.AddValue("m_listSeparator", text.ListSeparator, typeof(string));
            info.AddValue("m_isReadOnly", text.IsReadOnly);
            info.AddValue("m_cultureName", text.CultureName, typeof(string));
            info.AddValue("customCultureName", text.CultureName, typeof(string));
            info.AddValue("m_nDataItem", 0);
            info.AddValue("m_useUserOverride", false);
            info.AddValue("m_win32LangID", text.LCID);
        }

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector)
        {
            string? name = null;
            int lcid = 0;
            foreach (SerializationEntry entry in info)
            {
                switch (entry.Name)
                {
                    case "m_cultureName":
                        name = (string?)entry.Value ?? name;
                        break;
                    case "customCultureName":
                        name ??= (string?)entry.Value;
                        break;
                    case "m_win32LangID":
                        lcid = Convert.ToInt32(entry.Value, CultureInfo.InvariantCulture);
                        break;
                }
            }
            CultureInfo culture = name != null ? CultureInfo.GetCultureInfo(name) : CultureInfo.GetCultureInfo(lcid);
            return culture.TextInfo;
        }
    }

    // .NET Framework 4.8 CultureInfo serializes these fields (cultureinfo.cs, in declaration order); its OnSerializing sets
    // m_name, m_useUserOverride and cultureID from the culture data, and its OnDeserialized rebuilds the culture from
    // m_name (or cultureID when there is no name) and m_useUserOverride. The other members are created lazily on demand,
    // so they are written as null (a .NET Framework reader recreates them); when a stream carries them they are read and
    // dropped (CultureMemberSurrogate), as the rebuilt culture has its own.
    internal sealed class CultureInfoSurrogate : ISerializationSurrogate
    {
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            var culture = (CultureInfo)obj;
            info.FullTypeName = "System.Globalization.CultureInfo";
            info.AssemblyName = FrameworkSurrogates.Mscorlib;
            info.AddValue("m_isReadOnly", culture.IsReadOnly);
            info.AddValue("compareInfo", null, typeof(CompareInfo));
            info.AddValue("textInfo", null, typeof(TextInfo));
            info.AddValue("numInfo", null, typeof(NumberFormatInfo));
            info.AddValue("dateTimeInfo", null, typeof(DateTimeFormatInfo));
            info.AddValue("calendar", null, typeof(Calendar));
            info.AddValue("m_dataItem", 0);
            info.AddValue("cultureID", culture.LCID);
            info.AddValue("m_name", culture.Name, typeof(string));
            info.AddValue("m_useUserOverride", culture.UseUserOverride);
        }

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector)
        {
            string? name = null;
            int lcid = 0x007f;
            bool useUserOverride = false, isReadOnly = false;
            foreach (SerializationEntry entry in info)
            {
                switch (entry.Name)
                {
                    case "m_name":
                        name = (string?)entry.Value;
                        break;
                    case "cultureID":
                        lcid = Convert.ToInt32(entry.Value, CultureInfo.InvariantCulture);
                        break;
                    case "m_useUserOverride":
                        useUserOverride = Convert.ToBoolean(entry.Value, CultureInfo.InvariantCulture);
                        break;
                    case "m_isReadOnly":
                        isReadOnly = Convert.ToBoolean(entry.Value, CultureInfo.InvariantCulture);
                        break;
                }
            }
            CultureInfo culture = name != null ? new CultureInfo(name, useUserOverride) : new CultureInfo(lcid, useUserOverride);
            return isReadOnly ? CultureInfo.ReadOnly(culture) : culture;
        }
    }

    // NumberFormatInfo, DateTimeFormatInfo and the calendars: .NET does not serialize them. They reach a stream only as
    // members of a .NET Framework CultureInfo, which CultureInfoSurrogate rebuilds without them, so they are read and
    // dropped. Writing one on its own still fails, as it does on .NET.
    internal sealed class CultureMemberSurrogate : ISerializationSurrogate
    {
        internal static bool Handles(Type type) =>
            type == typeof(NumberFormatInfo) || type == typeof(DateTimeFormatInfo)
            || (typeof(Calendar).IsAssignableFrom(type) && type.Assembly == typeof(Calendar).Assembly);

        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context) =>
            throw new SerializationException(SR.Format(SR.Serialization_NonSerType, obj.GetType().FullName, obj.GetType().Assembly.FullName));

        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector) => obj;
    }

    // .NET Framework's NameObjectCollectionBase.GetObjectData / OnDeserialization, member for member.
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "NameObjectCollectionBase members are looked up by name; BinaryFormatter is not trim compatible.")]
    internal sealed class NameObjectCollectionSurrogate : ISerializationSurrogate, IDeserializationCallbackSurrogate
    {
        private const string ReadOnlyName = "ReadOnly";
        private const string CountName = "Count";
        private const string ComparerName = "Comparer";
        private const string HashCodeProviderName = "HashProvider";
        private const string KeysName = "Keys";
        private const string ValuesName = "Values";
        private const string KeyComparerName = "KeyComparer";
        private const string VersionName = "Version";

        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type s_base = typeof(NameObjectCollectionBase);
        private static readonly FieldInfo s_keyComparer = Field("_keyComparer");
        private static readonly FieldInfo s_version = Field("_version");
        // .NET's default comparer (an invariant, case-insensitive CultureAwareComparer of its own); .NET Framework's was
        // StringComparer.InvariantCultureIgnoreCase.
        private static readonly object? s_defaultComparer = s_base.GetField("s_defaultComparer", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        // What the obsolete IHashCodeProvider/IComparer constructors build.
        private static readonly Type? s_compatibleComparer = s_base.Assembly.GetType("System.Collections.CompatibleComparer");
        private static readonly PropertyInfo s_readOnly = s_base.GetProperty("IsReadOnly", Instance)!;
        private static readonly MethodInfo s_getAllKeys = s_base.GetMethod("BaseGetAllKeys", Instance, Type.EmptyTypes)!;
        private static readonly MethodInfo s_getAllValues = s_base.GetMethod("BaseGetAllValues", Instance, Type.EmptyTypes)!;
        private static readonly MethodInfo s_add = s_base.GetMethod("BaseAdd", Instance, new[] { typeof(string), typeof(object) })!;
        private static readonly ConstructorInfo s_ctorComparer = s_base.GetConstructor(Instance, new[] { typeof(int), typeof(IEqualityComparer) })!;
        private static readonly ConstructorInfo s_ctorProviderComparer = s_base.GetConstructor(Instance, new[] { typeof(int), typeof(IHashCodeProvider), typeof(IComparer) })!;

        // Collections being deserialized, until the graph is complete (.NET Framework kept this in _serializationInfo).
        private readonly ConditionalWeakTable<object, SerializationInfo> _pending = new ConditionalWeakTable<object, SerializationInfo>();

        private static FieldInfo Field(string name) =>
            s_base.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(s_base.FullName, name);

        // NameValueCollection and other System types were [Serializable] on .NET Framework; other types must be marked, as
        // .NET Framework required. Types that override GetObjectData or OnDeserialization call .NET's (throwing) base
        // implementation, and their own data would be lost here, so they are left alone.
        internal static bool Handles(Type type)
        {
            if (!s_base.IsAssignableFrom(type) || type.IsAbstract)
            {
                return false;
            }
            if (!type.IsSerializable && type.Assembly != s_base.Assembly)
            {
                return false;
            }
            MethodInfo? getObjectData = type.GetMethod(nameof(ISerializable.GetObjectData), Instance, new[] { typeof(SerializationInfo), typeof(StreamingContext) });
            MethodInfo? onDeserialization = type.GetMethod(nameof(IDeserializationCallback.OnDeserialization), Instance, new[] { typeof(object) });
            return getObjectData?.DeclaringType == s_base && onDeserialization?.DeclaringType == s_base;
        }

        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            if (obj.GetType().Assembly == s_base.Assembly)
            {
                // NameValueCollection is in System on .NET Framework; .NET has no TypeForwardedFrom on it.
                info.AssemblyName = FrameworkSurrogates.SystemAssembly;
            }

            info.AddValue(ReadOnlyName, (bool)s_readOnly.GetValue(obj)!);

            object? keyComparer = s_keyComparer.GetValue(obj);
            if (ReferenceEquals(keyComparer, s_defaultComparer) || ReferenceEquals(keyComparer, StringComparer.InvariantCultureIgnoreCase))
            {
                // .NET Framework's default comparer: written as the V1 invariant hash code provider and comparer.
                info.AddValue(HashCodeProviderName, CaseInsensitiveHashCodeProvider.DefaultInvariant, typeof(IHashCodeProvider));
                info.AddValue(ComparerName, CaseInsensitiveComparer.DefaultInvariant, typeof(IComparer));
                FrameworkSurrogates.Remember(CaseInsensitiveHashCodeProvider.DefaultInvariant, CultureInfo.InvariantCulture);
                FrameworkSurrogates.Remember(CaseInsensitiveComparer.DefaultInvariant, CultureInfo.InvariantCulture);
            }
            else if (keyComparer == null)
            {
                info.AddValue(HashCodeProviderName, null, typeof(IHashCodeProvider));
                info.AddValue(ComparerName, null, typeof(IComparer));
            }
            else if (keyComparer.GetType() == s_compatibleComparer)
            {
                // Built from an IHashCodeProvider and an IComparer (the obsolete constructors), as on .NET Framework.
                info.AddValue(HashCodeProviderName, s_compatibleComparer.GetField("_hcp", Instance)?.GetValue(keyComparer), typeof(IHashCodeProvider));
                info.AddValue(ComparerName, s_compatibleComparer.GetField("_comparer", Instance)?.GetValue(keyComparer), typeof(IComparer));
            }
            else
            {
                info.AddValue(KeyComparerName, keyComparer, typeof(IEqualityComparer));
            }

            string?[] keys = (string?[])s_getAllKeys.Invoke(obj, null)!;
            object?[] values = (object?[])s_getAllValues.Invoke(obj, null)!;
            info.AddValue(CountName, keys.Length);
            info.AddValue(KeysName, keys, typeof(string[]));
            info.AddValue(ValuesName, values, typeof(object[]));
            info.AddValue(VersionName, (int)s_version.GetValue(obj)!);
        }

        // Members may still be incomplete here (as with .NET Framework's serialization constructor), so the collection is
        // filled in OnDeserialization.
        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector? selector)
        {
            _pending.AddOrUpdate(obj, info);
            return obj;
        }

        public void OnDeserialization(object obj, object? sender)
        {
            if (!_pending.TryGetValue(obj, out SerializationInfo? info))
            {
                return;
            }
            _pending.Remove(obj);

            bool readOnly = false;
            int count = 0;
            string?[]? keys = null;
            object?[]? values = null;
            IHashCodeProvider? hashProvider = null;
            IComparer? comparer = null;
            IEqualityComparer? keyComparer = null;
            bool hasVersion = false;
            int serializedVersion = 0;

            foreach (SerializationEntry entry in info)
            {
                switch (entry.Name)
                {
                    case ReadOnlyName:
                        readOnly = info.GetBoolean(ReadOnlyName);
                        break;
                    case HashCodeProviderName:
                        hashProvider = (IHashCodeProvider?)info.GetValue(HashCodeProviderName, typeof(IHashCodeProvider));
                        break;
                    case ComparerName:
                        comparer = (IComparer?)info.GetValue(ComparerName, typeof(IComparer));
                        break;
                    case KeyComparerName:
                        keyComparer = (IEqualityComparer?)info.GetValue(KeyComparerName, typeof(IEqualityComparer));
                        break;
                    case CountName:
                        count = info.GetInt32(CountName);
                        break;
                    case KeysName:
                        keys = (string?[]?)info.GetValue(KeysName, typeof(string[]));
                        break;
                    case ValuesName:
                        values = (object?[]?)info.GetValue(ValuesName, typeof(object[]));
                        break;
                    case VersionName:
                        hasVersion = true;
                        serializedVersion = info.GetInt32(VersionName);
                        break;
                }
            }

            if (keys == null || values == null)
            {
                throw new SerializationException();
            }

            // Run NameObjectCollectionBase's constructor on the (uninitialized) instance, with the comparer .NET Framework
            // would have built.
            if (keyComparer != null)
            {
                s_ctorComparer.Invoke(obj, new object[] { count, keyComparer });
            }
            else if (comparer == null || hashProvider == null)
            {
                throw new SerializationException();
            }
            else if (IsCaseInsensitive(hashProvider, comparer, out CultureInfo? culture))
            {
                // CaseInsensitiveHashCodeProvider + CaseInsensitiveComparer of one culture: the case-insensitive
                // StringComparer of that culture (for the invariant culture, .NET Framework's default comparer).
                IEqualityComparer equalityComparer = culture.Name.Length == 0
                    ? StringComparer.InvariantCultureIgnoreCase
                    : StringComparer.Create(culture, ignoreCase: true);
                s_ctorComparer.Invoke(obj, new object[] { count, equalityComparer });
            }
            else
            {
                s_ctorProviderComparer.Invoke(obj, new object[] { count, hashProvider, comparer });
            }

            for (int i = 0; i < count; i++)
            {
                s_add.Invoke(obj, new object?[] { keys[i], values[i] });
            }

            s_readOnly.SetValue(obj, readOnly); // after the collection is populated
            if (hasVersion)
            {
                s_version.SetValue(obj, serializedVersion);
            }
        }

        private static bool IsCaseInsensitive(IHashCodeProvider hashProvider, IComparer comparer, [NotNullWhen(true)] out CultureInfo? culture)
        {
            culture = null;
            if (hashProvider is not CaseInsensitiveHashCodeProvider || comparer is not CaseInsensitiveComparer)
            {
                return false;
            }
            CultureInfo hashCulture = FrameworkSurrogates.CultureOf(hashProvider, "_compareInfo");
            CultureInfo compareCulture = FrameworkSurrogates.CultureOf(comparer, "_compareInfo");
            if (hashCulture.Name != compareCulture.Name)
            {
                return false;
            }
            culture = compareCulture;
            return true;
        }
    }
}
