// webForCore (#156): writes each graph of FrameworkStreamCases with .NET Framework's BinaryFormatter to <name>.bin, reads
// it back with .NET Framework and checks it is the same graph. With a second directory (the committed
// test/webForCore.Serialization.Tests/FrameworkStreams), each committed stream must be what .NET Framework writes, and
// .NET Framework must read it as that graph; Serialization.Tests checks that the port writes those same bytes, so this
// is also the check that .NET Framework reads what the port writes.
//
// Usage (on Windows, .NET Framework 4.8): FrameworkStreams.exe <output directory> [<committed directory>]
// Exits with 1 on a difference.
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using WebForCore.Serialization.Tests;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 2)
        {
            Console.Error.WriteLine("usage: FrameworkStreams <output directory> [<committed directory>]");
            return 2;
        }
        string output = args[0];
        string committed = args.Length > 1 ? args[1] : null;
        Directory.CreateDirectory(output);
        Console.WriteLine(".NET Framework " + Environment.Version + ", " + typeof(object).Assembly.FullName);

        int failures = 0;
        foreach (FrameworkStreamCase streamCase in FrameworkStreamCases.All)
        {
            byte[] bytes = Serialize(streamCase.Create());
            File.WriteAllBytes(Path.Combine(output, streamCase.Name + ".bin"), bytes);
            string problem = Check(streamCase, bytes, "written");

            if (problem == null && committed != null)
            {
                string path = Path.Combine(committed, streamCase.Name + ".bin");
                if (!File.Exists(path))
                {
                    problem = "not committed";
                }
                else
                {
                    byte[] committedBytes = File.ReadAllBytes(path);
                    problem = committedBytes.SequenceEqual(bytes)
                        ? Check(streamCase, committedBytes, "committed")
                        : "the committed stream differs from what .NET Framework writes";
                }
            }

            Console.WriteLine((problem == null ? "ok      " : "FAILED  ") + streamCase.Name + (problem == null ? "" : ": " + problem));
            if (problem != null)
            {
                failures++;
            }
        }
        Console.WriteLine(failures == 0 ? "All streams ok." : failures + " failed.");
        return failures == 0 ? 0 : 1;
    }

    private static byte[] Serialize(object graph)
    {
        var stream = new MemoryStream();
        new BinaryFormatter().Serialize(stream, graph);
        return stream.ToArray();
    }

    private static string Check(FrameworkStreamCase streamCase, byte[] bytes, string what)
    {
        object read;
        try
        {
            read = new BinaryFormatter().Deserialize(new MemoryStream(bytes));
        }
        catch (Exception e)
        {
            return "reading the " + what + " stream: " + e.GetType().FullName + ": " + e.Message;
        }
        if (!streamCase.PortWrites)
        {
            // System.Empty, internal to mscorlib.
            return read != null && read.GetType().FullName == "System.Empty" ? null : "read " + read + " (expected System.Empty)";
        }
        return FrameworkStreamCases.Matches(streamCase.Create(), read) ? null : "the " + what + " stream read as " + read;
    }
}
