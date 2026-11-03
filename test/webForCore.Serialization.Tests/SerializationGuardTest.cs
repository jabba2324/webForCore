using System.Runtime.Serialization;
using Xunit;
using BinaryFormatter = WebFormsForCore.Serialization.Formatters.Binary.BinaryFormatter;

namespace WebForCore.Serialization.Tests
{
    // The BinaryFormatter port doesn't turn on .NET's SerializationGuard (BinaryObjectReader.cs, #57). Upstream calls
    // SerializationInfo.StartDeserialization(), which isn't in .NET's public reference assemblies; reaching it by
    // reflection breaks CONTRIBUTING.md rule 6. With the guard on, a file write during deserialization would throw.
    public class SerializationGuardTest
    {
        [Serializable]
        public sealed class WritesFileWhenDeserialized
        {
            public string Path = "";

            [OnDeserialized]
            private void OnDeserialized(StreamingContext context) => File.WriteAllText(Path, "deserialized");
        }

        [Fact]
        public void DeserializationCallbacksRunWithoutTheGuard()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wfc-guard-" + Guid.NewGuid().ToString("N"));
            try
            {
                var ms = new MemoryStream();
                new BinaryFormatter().Serialize(ms, new WritesFileWhenDeserialized { Path = path });
                ms.Position = 0;

                var result = (WritesFileWhenDeserialized)new BinaryFormatter().Deserialize(ms);

                Assert.Equal(path, result.Path);
                Assert.Equal("deserialized", File.ReadAllText(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
