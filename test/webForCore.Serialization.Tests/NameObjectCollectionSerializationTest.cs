using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Web;
using Xunit;
using BinaryFormatter = WebFormsForCore.Serialization.Formatters.Binary.BinaryFormatter;

namespace WebForCore.Serialization.Tests
{
    // NameValueCollection and its subclasses through the BinaryFormatter port, in .NET Framework's format
    // (FrameworkSurrogates.cs; referencesource nameobjectcollectionbase.cs).
    public class NameObjectCollectionSerializationTest
    {
        private static readonly PropertyInfo s_isReadOnly =
            typeof(NameObjectCollectionBase).GetProperty("IsReadOnly", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static byte[] Serialize(object graph)
        {
            var ms = new MemoryStream();
            new BinaryFormatter().Serialize(ms, graph);
            return ms.ToArray();
        }

        private static T RoundTrip<T>(T graph, out byte[] bytes) where T : notnull
        {
            bytes = Serialize(graph);
            return (T)new BinaryFormatter().Deserialize(new MemoryStream(bytes));
        }

        private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

        // Member names in a stream, in order: the length-prefixed strings that follow a type name.
        private static void AssertInOrder(string text, params string[] items)
        {
            int at = 0;
            foreach (string item in items)
            {
                int next = text.IndexOf(item, at, StringComparison.Ordinal);
                Assert.True(next >= 0, $"'{item}' not found after position {at}");
                at = next + item.Length;
            }
        }

        [Fact]
        public void DefaultComparer_RoundTripsKeysValuesAndCaseInsensitivity()
        {
            var original = new NameValueCollection { { "a", "1" }, { "a", "2" }, { "B", "3" }, { null, "nullkey" } };

            NameValueCollection copy = RoundTrip(original, out _);

            Assert.Equal(3, copy.Count);
            Assert.Equal(new[] { "a", "B", null }, copy.AllKeys);
            Assert.Equal(new[] { "1", "2" }, copy.GetValues("A"));
            Assert.Equal("3", copy["b"]);
            Assert.Equal("nullkey", copy[null]);
        }

        [Fact]
        public void DefaultComparer_WritesFrameworkLayout()
        {
            string text = Text(Serialize(new NameValueCollection { { "k", "v" } }));

            // .NET Framework names: NameValueCollection is in System; the comparer is written as the V1 hash code provider
            // and comparer, which are mscorlib types (written without an assembly record).
            AssertInOrder(text,
                "System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
                "System.Collections.Specialized.NameValueCollection",
                "ReadOnly", "HashProvider", "Comparer", "Count", "Keys", "Values", "Version",
                "System.Collections.CaseInsensitiveHashCodeProvider", "m_text", "System.Globalization.TextInfo",
                "System.Collections.CaseInsensitiveComparer", "m_compareInfo", "System.Globalization.CompareInfo");
            AssertInOrder(text, "System.Globalization.TextInfo", "m_listSeparator", "m_isReadOnly", "m_cultureName",
                "customCultureName", "m_nDataItem", "m_useUserOverride", "m_win32LangID");
            Assert.DoesNotContain("System.Private.CoreLib", text);
            Assert.DoesNotContain("System.Collections.Specialized, Version", text);
            Assert.DoesNotContain("System.Collections.NonGeneric", text);
            Assert.DoesNotContain("KeyComparer", text);
        }

        [Fact]
        public void OtherComparers_WrittenAsKeyComparer()
        {
            var original = new NameValueCollection(StringComparer.OrdinalIgnoreCase) { { "x", "y" } };

            NameValueCollection copy = RoundTrip(original, out byte[] bytes);

            AssertInOrder(Text(bytes), "ReadOnly", "KeyComparer", "Count", "Keys", "Values", "Version", "System.OrdinalComparer");
            Assert.Equal("y", copy["X"]);
        }

        [Fact]
        public void CultureComparer_RoundTrips()
        {
            var tr = CultureInfo.GetCultureInfo("tr-TR");
            var original = new NameValueCollection(StringComparer.Create(tr, ignoreCase: true)) { { "İ", "dotted" } };

            NameValueCollection copy = RoundTrip(original, out _);

            Assert.Equal("dotted", copy["i"]); // Turkish: i upper-cases to dotted capital I
        }

        [Fact]
        public void V1ProviderAndComparer_RoundTripAsThatCulture()
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            var original = new NameValueCollection(new CaseInsensitiveHashCodeProvider(fr), new CaseInsensitiveComparer(fr)) { { "Q", "r" } };

            NameValueCollection copy = RoundTrip(original, out byte[] bytes);

            AssertInOrder(Text(bytes), "HashProvider", "Comparer", "System.Collections.CaseInsensitiveHashCodeProvider");
            Assert.Contains("fr-FR", Text(bytes));
            Assert.Equal("r", copy["q"]);
        }

        [Fact]
        public void ReadOnlyAndVersion_Restored()
        {
            var original = new NameValueCollection { { "z", "1" } };
            s_isReadOnly.SetValue(original, true);

            NameValueCollection copy = RoundTrip(original, out _);

            Assert.True((bool)s_isReadOnly.GetValue(copy)!);
            Assert.Throws<NotSupportedException>(() => copy.Add("q", "w"));
        }

        [Fact]
        public void SharedAndNestedReferences_Preserved()
        {
            var shared = new NameValueCollection { { "k", "v" } };
            var graph = new Hashtable { { "one", shared }, { "two", shared }, { "nested", new object[] { shared } } };

            Hashtable copy = RoundTrip(graph, out _);

            Assert.Same(copy["one"], copy["two"]);
            Assert.Same(copy["one"], ((object[])copy["nested"]!)[0]);
            Assert.Equal("v", ((NameValueCollection)copy["one"]!)["K"]);
        }

        [Fact]
        public void HttpCookieValues_RoundTrip()
        {
            // HttpCookie.Values is System.Web's HttpValueCollection (OrdinalIgnoreCase), as stored by session state.
            var cookie = new HttpCookie("stuff");
            cookie.Values["a"] = "1";
            cookie.Values["b"] = "2";

            NameValueCollection copy = RoundTrip(cookie.Values, out byte[] bytes);

            Assert.Equal("System.Web.HttpValueCollection", copy.GetType().FullName);
            Assert.Contains("System.Web, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", Text(bytes));
            Assert.Equal("1", copy["A"]);
            Assert.Equal("a=1&b=2", copy.ToString());
        }

        [Fact]
        public void TextInfoAndCaseInsensitiveComparers_RoundTrip()
        {
            var de = CultureInfo.GetCultureInfo("de-DE");
            var graph = new object[] { de.TextInfo, new CaseInsensitiveComparer(de), new CaseInsensitiveHashCodeProvider(de) };

            object[] copy = RoundTrip(graph, out _);

            Assert.Equal("de-DE", ((TextInfo)copy[0]).CultureName);
            Assert.Equal(0, ((CaseInsensitiveComparer)copy[1]).Compare("STRASSE", "strasse"));
            Assert.Equal(((CaseInsensitiveHashCodeProvider)copy[2]).GetHashCode("ABC"), ((CaseInsensitiveHashCodeProvider)copy[2]).GetHashCode("abc"));
        }

        [Fact]
        public void CultureInfo_WritesFrameworkFields_AndRoundTrips()
        {
            // MVC's ValueProviderResult carries a CultureInfo (ModelState in session state or TempData).
            var graph = new object[] { CultureInfo.GetCultureInfo("fr-FR"), new CultureInfo("es-MX", false), CultureInfo.InvariantCulture };

            object[] copy = RoundTrip(graph, out byte[] bytes);

            string text = Text(bytes);
            Assert.Contains("System.Globalization.CultureInfo", text);
            Assert.DoesNotContain("System.Private.CoreLib", text);
            int at = 0;
            foreach (string field in new[] { "m_isReadOnly", "compareInfo", "textInfo", "numInfo", "dateTimeInfo", "calendar",
                                             "m_dataItem", "cultureID", "m_name", "m_useUserOverride" })
            {
                int next = text.IndexOf(field, at, StringComparison.Ordinal);
                Assert.True(next >= at, field + " in .NET Framework's order");
                at = next;
            }
            var fr = (CultureInfo)copy[0];
            Assert.Equal("fr-FR", fr.Name);
            Assert.True(fr.IsReadOnly);
            Assert.Equal("es-MX", ((CultureInfo)copy[1]).Name);
            Assert.False(((CultureInfo)copy[1]).IsReadOnly);
            Assert.False(((CultureInfo)copy[1]).UseUserOverride);
            Assert.Equal("", ((CultureInfo)copy[2]).Name);
            Assert.Equal("1,5", 1.5.ToString(fr));
        }
    }
}
