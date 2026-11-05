// The CodeDom options matrix (#135): one line per combination of GenerateInMemory, IncludeDebugInformation,
// CompilerOptions, OutputAssembly and TempFiles.KeepFiles, with what CompileAssemblyFromSource did. C# 5, so that
// .NET Framework's csc.exe compiles it too.
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

public static class CodeDomMatrix
{
    public static string Run(Func<CompilerParameters, string, CompilerResults> compile, string folder)
    {
        var sb = new StringBuilder();
        Directory.CreateDirectory(folder);
        int n = 0;
        foreach (bool mem in new[] { false, true })
        foreach (bool dbg in new[] { false, true })
        foreach (string opts in new[] { null, "", "/optimize+" })
        foreach (bool setOut in new[] { false, true })
        foreach (bool keep in new[] { false, true })
        {
            n++;
            string name = "M" + n + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string caseFolder = Path.Combine(folder, name);
            Directory.CreateDirectory(caseFolder);
            string outPath = Path.Combine(caseFolder, name + ".dll");
            var p = new CompilerParameters();
            p.GenerateInMemory = mem;
            p.IncludeDebugInformation = dbg;
            p.CompilerOptions = opts;
            if (setOut) p.OutputAssembly = outPath;
            p.TempFiles = new TempFileCollection(caseFolder, keep);
            string source = "public class Generated" + n + " { public static int Answer() { return 42; } }";
            sb.Append("mem=" + B(mem) + " dbg=" + B(dbg) + " opts=" + (opts == null ? "null" : opts == "" ? "empty" : opts) +
                " out=" + (setOut ? "set" : "unset") + " keep=" + B(keep) + ": ");
            try
            {
                CompilerResults r = compile(p, source);
                string output = p.OutputAssembly;
                sb.Append("ret=" + r.NativeCompilerReturnValue + " errors=" + r.Errors.Count);
                if (r.Errors.Count > 0) sb.Append(" (" + r.Errors[0].ErrorNumber + ": " + r.Errors[0].ErrorText + ")");
                sb.Append(" outputAssembly=" + (output == null ? "null" : output == outPath ? "set" : "temp"));
                sb.Append(" path=" + (r.PathToAssembly == null ? "null" : r.PathToAssembly == output ? "output" : "other"));
                sb.Append(" options=" + (p.CompilerOptions == null ? "null" : p.CompilerOptions == opts ? "same" : "changed"));
                string assembly;
                try
                {
                    Assembly a = r.CompiledAssembly;
                    if (a == null) assembly = "null";
                    else
                    {
                        object answer = a.GetType("Generated" + n).GetMethod("Answer").Invoke(null, null);
                        assembly = (a.Location == "" ? "memory" : a.Location == output ? "output" : "file") + "/" + answer;
                    }
                }
                catch (Exception e)
                {
                    assembly = e.GetType().Name;
                }
                sb.Append(" assembly=" + assembly);
                sb.Append(" dll=" + B(output != null && File.Exists(output)));
                sb.Append(" pdb=" + B(output != null && File.Exists(Path.ChangeExtension(output, ".pdb"))));
                // The files left in the case's folder: the output by name, temporary files by their extensions.
                var left = new List<string>();
                foreach (string f in Directory.GetFiles(caseFolder))
                {
                    string file = Path.GetFileName(f);
                    left.Add(file.StartsWith(name) ? "out" + file.Substring(name.Length) : file.Substring(file.IndexOf('.')));
                }
                left.Sort(StringComparer.Ordinal);
                sb.Append(" files=" + string.Join(",", left.ToArray()));
            }
            catch (Exception e)
            {
                sb.Append("exception=" + e.GetType().FullName);
            }
            sb.Append("\n");
        }
        return sb.ToString();
    }

    static string B(bool b) { return b ? "T" : "F"; }
}
