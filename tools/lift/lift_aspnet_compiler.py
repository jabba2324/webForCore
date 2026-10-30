#!/usr/bin/env python3
"""Replaces aspnet_compiler's front end in src/WebFormsForCore.AspNetCompiler, which WebFormsForCore decompiled from
aspnet_compiler.exe, with Microsoft's MIT-licensed source from the .NET Framework Reference Source
(microsoft/referencesource, aspnet_compiler/), so that no decompiled code remains (docs/LICENSING.md).

Usage: lift_aspnet_compiler.py <referencesource checkout> [ForkRoot]

aspnet_compiler.cs and the strongly-typed CompilerResources class are copied to ReferenceSource/aspnet_compiler/ and
changed only by FIXUPS, which connect them to WebFormsForCore's additions in Precompiler.WebFormsForCore.cs (multiple
bin folders and target frameworks, precompilation in its own load context, error events for the MSBuild task).
"""
import os
import subprocess
import sys

COMMIT = "ec9fa9ae770d522a5b5f0607898044b7478574a3"
PROJECT = "src/WebFormsForCore.AspNetCompiler"
DEST = "ReferenceSource"

REMOVE = ["Precompiler.cs", "CompilerResources.cs"]  # decompiled

COPY = ["aspnet_compiler/aspnet_compiler.cs", "aspnet_compiler/DataAnnotationsResources.Designer.cs"]

FIXUPS = {
    "aspnet_compiler/aspnet_compiler.cs": [
        ("    class Precompiler {", "    public partial class Precompiler {", 1),
        ("        static ClientBuildManager _client;\n", "", 1),
        ("        static PrecompilationFlags _precompilationFlags;",
         "        static PrecompileFlags _precompilationFlags; // webForCore: WebFormsForCore's PrecompileFlags mirrors PrecompilationFlags", 1),
        ("CompilerResources.brand_text, ThisAssembly.InformationalVersion)",
         "CompilerResources.brand_text, Assembly.GetExecutingAssembly().GetName().Version.ToString(3))", 1),
        # Precompile in WebFormsForCore's own load context, for each bin folder and target framework.
        ("""                ClientBuildManagerParameter parameter = new ClientBuildManagerParameter();
                parameter.PrecompilationFlags = _precompilationFlags;
                parameter.StrongNameKeyFile = _keyFile;
                parameter.StrongNameKeyContainer = _keyContainer;
                parameter.ExcludedVirtualPaths.AddRange(_excludedVirtualPaths);

                _client = new ClientBuildManager(_sourceVirtualDir, _sourcePhysicalDir,
                    _targetPhysicalDir, parameter);

                _client.PrecompileApplication(new CBMCallback());

                // Return 0 when successful
                return 0;
""", """                // webForCore: WebFormsForCore precompiles in its own load context, per bin folder and target framework
                PrecompileParameter parameter = new PrecompileParameter();
                parameter.PrecompilationFlags = _precompilationFlags;
                parameter.StrongNameKeyFile = _keyFile;
                parameter.StrongNameKeyContainer = _keyContainer;
                parameter.BinFolder = _binFolder;
                parameter.TargetFramework = _targetFramework;
                parameter.ExcludedVirtualPaths.AddRange(_excludedVirtualPaths);

                return Precompile(_sourceVirtualDir, _sourcePhysicalDir, _targetPhysicalDir, parameter);
""", 1),
        ("PrecompilationFlags.", "PrecompileFlags.", 14),
        ('''            Console.WriteLine("                [-x excludeVirtualPath [...]]");
''', '''            Console.WriteLine("                [-x excludeVirtualPath [...]]");
            Console.WriteLine("                [-b binFolder]");
''', 1),
        ('''            Console.WriteLine("                [-errorstack]");
''', '''            Console.WriteLine("                [-errorstack]");
            Console.WriteLine("                [[-t] targetFramework]");
''', 1),
        ('''            DisplaySwitchWithHelp("-d", CompilerResources.d_help);
''', '''            DisplaySwitchWithHelp("-d", CompilerResources.d_help);
            DisplaySwitchWithHelp("-t", CompilerResources.t_help);
''', 1),
        ('''            DisplaySwitchWithHelp("-x", CompilerResources.x_help);
''', '''            DisplaySwitchWithHelp("-x", CompilerResources.x_help);
            DisplaySwitchWithHelp("-b", CompilerResources.b_help);
''', 1),
        ('''                    case "nologo":
''', '''                    case "b": // webForCore: WebFormsForCore's bin folder (the app's output folder, e.g. bin_dotnet)
                        _binFolder = GetNextArgument(args, ref i);
                        if (_binFolder == null)
                            return false;
                        break;

                    case "t": // webForCore: WebFormsForCore's target framework
                        _targetFramework = GetNextArgument(args, ref i);
                        if (_targetFramework == null)
                            return false;
                        break;

                    case "nologo":
''', 1),
        ("            if (exception is HttpCompileException) {", '            if (IsException(exception, "HttpCompileException")) {', 1),
        ("            else if (exception is HttpParseException) {", '            else if (IsException(exception, "HttpParseException")) {', 1),
        ("""                if (exception is ConfigurationException) {
                    ConfigurationException e = (ConfigurationException)exception;
                    DumpError(e.Filename, e.Line, false, "ASPCONFIG", e.BareMessage);
""", """                if (IsException(exception, "ConfigurationException")) {
                    // webForCore: the exception is from the precompilation's load context, read by name
                    Type t = exception.GetType();
                    DumpError((string)t.GetProperty("Filename").GetValue(exception), (int)t.GetProperty("Line").GetValue(exception),
                        false, "ASPCONFIG", (string)t.GetProperty("BareMessage").GetValue(exception));
""", 1),
        ("            if (e is HttpCompileException || e is HttpParseException || e is ConfigurationException) {",
         '            if (IsException(e, "HttpCompileException") || IsException(e, "HttpParseException") || IsException(e, "ConfigurationException")) {', 1),
        ("""        static void DumpExceptionStack(Exception e) {
""", """        static void DumpExceptionStack(Exception e) {
            // webForCore: WebFormsForCore's MSBuild task takes the exception; Silent suppresses the console
            if (OnException != null) {
                OnException(e);
                return;
            }
            if (Silent)
                return;
""", 1),
        ("""        static void DumpError(string filename, int line, bool warning, string errorNumber, string message) {
""", """        static void DumpError(string filename, int line, bool warning, string errorNumber, string message) {
            // webForCore: WebFormsForCore records the error and hands it to the MSBuild task, if there is one
            HasErrors = true;
            if (OnError != null) {
                OnError(filename, line, warning, errorNumber, message);
                return;
            }
            if (Silent)
                return;
""", 1),
    ],
    # WebFormsForCore's three strings (-b, -t) are added in Precompiler.WebFormsForCore.cs.
    "aspnet_compiler/DataAnnotationsResources.Designer.cs": [
        ("    internal class CompilerResources {", "    internal partial class CompilerResources {", 1),
    ],
}


def main():
    rs = sys.argv[1]
    fork = sys.argv[2] if len(sys.argv) > 2 else os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    project = os.path.join(fork, PROJECT)
    for rel in REMOVE:
        if os.path.exists(os.path.join(project, rel)):
            os.remove(os.path.join(project, rel))
    manifest = [f"# microsoft/referencesource {COMMIT[:7]}: source path (relative to repo root)\tdestination (relative to fork root)"]
    for src in COPY:
        raw = subprocess.run(["git", "-C", rs, "show", f"{COMMIT}:{src}"], capture_output=True, check=True).stdout
        bom = raw.startswith(b"\xef\xbb\xbf")
        text = raw.decode("utf-8-sig")
        crlf = "\r\n" in text
        text = text.replace("\r\n", "\n")
        for old, new, count in FIXUPS.get(src, []):
            found = text.count(old)
            if found != count:
                sys.exit(f"{src}: fixup matched {found} times, expected {count}: {old[:80]!r}")
            text = text.replace(old, new)
        if crlf:
            text = text.replace("\n", "\r\n")
        dest = os.path.join(project, DEST, src)
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, "wb") as f:
            f.write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))
        manifest.append(f"{src}\t{PROJECT}/{DEST}/{src}")
    with open(os.path.join(fork, "tools", "lift", "manifests", "aspnet_compiler.tsv"), "w", encoding="utf-8") as f:
        f.write("\n".join(manifest) + "\n")
    print(f"{len(REMOVE)} decompiled files removed, {len(COPY)} Reference Source files copied")


if __name__ == "__main__":
    main()
