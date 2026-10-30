namespace System.Web.Compilation {
    using System;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Threading;

    public partial class Precompiler {

        static string _sourcePhysicalDir;
        static string _metabasePath;
        static string _sourceVirtualDir;
        static string _targetPhysicalDir;
        static string _keyFile;
        static string _keyContainer;
        static PrecompileFlags _precompilationFlags; // webForCore: WebFormsForCore's PrecompileFlags mirrors PrecompilationFlags
        static bool _showErrorStack;
        static List<string> _excludedVirtualPaths;

        static int maxLineLength = 80;

        public static int Main(string[] args) {
            _excludedVirtualPaths = new List<string>();

            bool nologo = false;

            try {
                // Get the proper line length based on the Console window settings.
                // This can fail in some cases, so ignore errors, and stay with the default
                maxLineLength = Console.BufferWidth;
            }
            catch { }

            // Set the console app's UI culture (VSWhidbey 316444)
            SetThreadUICulture();

            // First, check if there is a -nologo switch
            for (int i = 0; i < args.Length; i++) {
                string currentSwitch = args[i].ToLower(CultureInfo.InvariantCulture);
                if (currentSwitch == "-nologo" || currentSwitch == "/nologo")
                    nologo = true;
            }

            if (!nologo) {
                Console.WriteLine(String.Format(CultureInfo.CurrentCulture, CompilerResources.brand_text, Assembly.GetExecutingAssembly().GetName().Version.ToString(3)));
                Console.WriteLine(CompilerResources.header_text);
                Console.WriteLine(CompilerResources.copyright);
                Console.WriteLine();
            }

            if (args.Length == 0) {
                Console.WriteLine(CompilerResources.short_usage_text);
                return 1;
            }

            if (!ValidateArgs(args)) {
                return 1;
            }

            try {
                // ClientBuildManager automatically detects vpath vs mbpath
                if (_sourceVirtualDir == null)
                    _sourceVirtualDir = _metabasePath;

                // webForCore: WebFormsForCore precompiles in its own load context, per bin folder and target framework
                PrecompileParameter parameter = new PrecompileParameter();
                parameter.PrecompilationFlags = _precompilationFlags;
                parameter.StrongNameKeyFile = _keyFile;
                parameter.StrongNameKeyContainer = _keyContainer;
                parameter.BinFolder = _binFolder;
                parameter.TargetFramework = _targetFramework;
                parameter.ExcludedVirtualPaths.AddRange(_excludedVirtualPaths);

                return Precompile(_sourceVirtualDir, _sourcePhysicalDir, _targetPhysicalDir, parameter);
            }
            catch (FileLoadException e) {
                // If the assembly fails to be loaded because of strong name verification,
                // return a better error message.
                if ((_precompilationFlags & PrecompileFlags.DelaySign) != 0) {
                    PropertyInfo pInfo = typeof(FileLoadException).GetProperty("HResult",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    MethodInfo methodInfo = pInfo.GetGetMethod(true /*nonPublic*/);
                    uint hresult = (uint)(int)methodInfo.Invoke(e, null);

                    if (hresult == 0x8013141A) {
                        DumpErrors(new FileLoadException(String.Format(CultureInfo.CurrentCulture, CompilerResources.Strongname_failure, e.FileName), e.FileName, e));
                        return 1;
                    }
                }

                DumpErrors(e);
            }
            catch (Exception e) {
                DumpErrors(e);
            }

            // Some exception happened, so return 1
            return 1;
        }

        private static void SetThreadUICulture() {

            //
            // This logic comes from the sample refered to in http://netglobe/htmldata/commandline.mht
            //

            Thread.CurrentThread.CurrentUICulture = CultureInfo.CurrentUICulture.GetConsoleFallbackUICulture();

            if ((System.Console.OutputEncoding.CodePage != 65001) &&
                (System.Console.OutputEncoding.CodePage != Thread.CurrentThread.CurrentUICulture.TextInfo.OEMCodePage) &&
                (System.Console.OutputEncoding.CodePage != Thread.CurrentThread.CurrentUICulture.TextInfo.ANSICodePage)) {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            }
        }

        private static void DisplayUsage() {
            Console.WriteLine(CompilerResources.usage);
            Console.WriteLine("aspnet_compiler [-?] [-m metabasePath | -v virtualPath [-p physicalDir]]");
            Console.WriteLine("                [[-u] [-f] [-d] [-fixednames] targetDir] [-c]");
            Console.WriteLine("                [-x excludeVirtualPath [...]]");
            Console.WriteLine("                [-b binFolder]");
            Console.WriteLine("                [[-keyfile file | -keycontainer container]");
            Console.WriteLine("                     [-aptca] [-delaySign]]");
            Console.WriteLine("                [-errorstack]");
            Console.WriteLine("                [[-t] targetFramework]");
            Console.WriteLine();

            DisplaySwitchWithHelp("-?", CompilerResources.questionmark_help);
            DisplaySwitchWithHelp("-m", CompilerResources.m_help);
            DisplaySwitchWithHelp("-v", CompilerResources.v_help);
            DisplaySwitchWithHelp("-p", CompilerResources.p_help);
            DisplaySwitchWithHelp("-u", CompilerResources.u_help);
            DisplaySwitchWithHelp("-f", CompilerResources.f_help);
            DisplaySwitchWithHelp("-d", CompilerResources.d_help);
            DisplaySwitchWithHelp("-t", CompilerResources.t_help);
            DisplaySwitchWithHelp("targetDir", CompilerResources.targetDir_help);
            DisplaySwitchWithHelp("-c", CompilerResources.c_help);
            DisplaySwitchWithHelp("-x", CompilerResources.x_help);
            DisplaySwitchWithHelp("-b", CompilerResources.b_help);
            DisplaySwitchWithHelp("-keyfile", CompilerResources.keyfile_help);
            DisplaySwitchWithHelp("-keycontainer", CompilerResources.keycontainer_help);
            DisplaySwitchWithHelp("-aptca", CompilerResources.aptca_help);
            DisplaySwitchWithHelp("-delaysign", CompilerResources.delaysign_help);
            DisplaySwitchWithHelp("-fixednames", CompilerResources.fixednames_help);
            DisplaySwitchWithHelp("-nologo", CompilerResources.nologo_help);
            DisplaySwitchWithHelp("-errorstack", CompilerResources.errorstack_help);
            Console.WriteLine();
            Console.WriteLine(CompilerResources.examples);
            Console.WriteLine();
            DisplayWordWrappedString(CompilerResources.example1);
            Console.WriteLine(@"    aspnet_compiler -m /LM/W3SVC/1/Root/MyApp c:\MyTarget");
            Console.WriteLine(@"    aspnet_compiler -v /MyApp c:\MyTarget");
            Console.WriteLine();
            DisplayWordWrappedString(CompilerResources.example2);
            Console.WriteLine(@"    aspnet_compiler -v /MyApp");
            Console.WriteLine();
            DisplayWordWrappedString(CompilerResources.example3);
            Console.WriteLine(@"    aspnet_compiler -v /MyApp -p c:\myapp c:\MyTarget");
            Console.WriteLine();
        }

        // Margin for the help string to the right of the switches
        const int leftMargin = 14;

        private static void DisplaySwitchWithHelp(string switchString, string stringHelpString) {
            Console.Write(switchString);
            DisplayWordWrappedString(stringHelpString, switchString.Length, leftMargin);
        }

        private static void DisplayWordWrappedString(string s) {
            DisplayWordWrappedString(s, 0, 0);
        }

        private static void DisplayWordWrappedString(string s, int currentOffset, int leftMargin) {
            string[] words = s.Split(' ');
            bool first = true;

            foreach (string word in words) {
                // Calculate how much space we need for the next word
                int neededChars = word.Length;
                if (!first)
                    neededChars++;

                // Too big for the line: go to the next line
                if (currentOffset + neededChars >= maxLineLength) {
                    Console.WriteLine();
                    currentOffset = 0;
                    first = true;
                }

                if (first) {
                    // Apply the margin if any
                    for (; currentOffset < leftMargin; currentOffset++)
                        Console.Write(' ');
                }
                else {
                    Console.Write(' ');
                    currentOffset++;
                }
                Console.Write(word);
                currentOffset += word.Length;
                first = false;
            }

            Console.WriteLine();
        }

        private static string GetNextArgument(string[] args, ref int index) {
            if (index == args.Length - 1) {
                Console.WriteLine(String.Format(CultureInfo.CurrentCulture, CompilerResources.missing_arg, args[index]));
                return null;
            }

            return args[++index];
        }

        private static bool ValidateArgs(string[] args) {

            if (args.Length == 0)
                return false;

            for (int i = 0; i < args.Length; i++) {

                string currentArg = args[i];

                // If it's not a switch, expect the target path
                if (currentArg[0] != '/' && currentArg[0] != '-') {
                    if (_targetPhysicalDir == null) {
                        _targetPhysicalDir = currentArg;
                        _targetPhysicalDir = GetFullPath(_targetPhysicalDir);
                        if (_targetPhysicalDir == null)
                            return false;
                    }
                    else {
                        DumpError("1001", String.Format(CultureInfo.CurrentCulture, CompilerResources.unexpected_param, currentArg));
                        return false;
                    }

                    continue;
                }

                string currentSwitch = currentArg.Substring(1).ToLower(CultureInfo.InvariantCulture);

                switch (currentSwitch) {

                    case "?":
                        DisplayUsage();
                        return false;

                    case "m":
                        _metabasePath = GetNextArgument(args, ref i);
                        if (_metabasePath == null) return false;
                        break;

                    case "v":
                        _sourceVirtualDir = GetNextArgument(args, ref i);
                        if (_sourceVirtualDir == null) return false;

                        if (!IsValidVirtualPath(_sourceVirtualDir)) {
                            DumpError("1011", String.Format(CultureInfo.CurrentCulture, CompilerResources.invalid_vpath, _sourceVirtualDir));
                            return false;
                        }
                        break;

                    case "p":
                        _sourcePhysicalDir = GetNextArgument(args, ref i);
                        if (_sourcePhysicalDir == null) return false;
                        _sourcePhysicalDir = GetFullPath(_sourcePhysicalDir);
                        if (_sourcePhysicalDir == null)
                            return false;

                        if (!Directory.Exists(_sourcePhysicalDir)) {
                            DumpError("1003", String.Format(CultureInfo.CurrentCulture, CompilerResources.dir_not_exist, _sourcePhysicalDir));
                            return false;
                        }
                        break;

                    case "x":
                        string path = GetNextArgument(args, ref i);
                        if (path == null) return false;
                        _excludedVirtualPaths.Add(path);
                        break;

                    case "u":
                        _precompilationFlags |= PrecompileFlags.Updatable;
                        break;

                    case "f":
                        _precompilationFlags |= PrecompileFlags.OverwriteTarget;
                        break;

                    case "d":
                        _precompilationFlags |= PrecompileFlags.ForceDebug;
                        break;

                    case "c":
                        _precompilationFlags |= PrecompileFlags.Clean;
                        break;

                    case "b": // webForCore: WebFormsForCore's bin folder (the app's output folder, e.g. bin_dotnet)
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
                        // Just ignore it since it was handled early on
                        break;

                    case "errorstack":
                        _showErrorStack = true;
                        break;

                    case "keyfile":
                        _keyFile = GetNextArgument(args, ref i);
                        if (_keyFile == null) {
                            return false;
                        }

                        if (!File.Exists(_keyFile)) {
                            DumpError("1012", String.Format(CultureInfo.CurrentCulture, CompilerResources.invalid_keyfile, _keyFile));
                            return false;
                        }

                        _keyFile = Path.GetFullPath(_keyFile);
                        break;

                    case "keycontainer":
                        _keyContainer = GetNextArgument(args, ref i);
                        if (_keyContainer == null) {
                            return false;
                        }
                        break;

                    case "aptca":
                        _precompilationFlags |= PrecompileFlags.AllowPartiallyTrustedCallers;
                        break;

                    case "delaysign":
                        _precompilationFlags |= PrecompileFlags.DelaySign;
                        break;

                    case "fixednames":
                        _precompilationFlags |= PrecompileFlags.FixedNames;
                        break;

                    default:
                        DumpError("1004", String.Format(CultureInfo.CurrentCulture, CompilerResources.unknown_switch, currentArg));
                        return false;
                }
            }

            // There must be exactly one of -m and -v
            if ((_sourceVirtualDir == null) == (_metabasePath == null)) {
                DumpError("1005", CompilerResources.need_m_or_v);
                return false;
            }

            // There must be exactly one of -m and -p
            if (_sourcePhysicalDir != null && _metabasePath != null) {
                DumpError("1006", CompilerResources.no_m_and_p);
                return false;
            }

            if ((_precompilationFlags & PrecompileFlags.Updatable) != 0 && _targetPhysicalDir == null) {
                DumpError("1007", String.Format(CultureInfo.CurrentCulture, CompilerResources.flag_requires_target, "u"));
                return false;
            }

            if ((_precompilationFlags & PrecompileFlags.OverwriteTarget) != 0 && _targetPhysicalDir == null) {
                DumpError("1008", String.Format(CultureInfo.CurrentCulture, CompilerResources.flag_requires_target, "f"));
                return false;
            }

            if ((_precompilationFlags & PrecompileFlags.ForceDebug) != 0 && _targetPhysicalDir == null) {
                DumpError("1009", String.Format(CultureInfo.CurrentCulture, CompilerResources.flag_requires_target, "d"));
                return false;
            }

            if (_keyFile != null && _targetPhysicalDir == null) {
                DumpError("1017", String.Format(CultureInfo.CurrentCulture, CompilerResources.flag_requires_target, "keyfile"));
                return false;
            }

            if (_keyContainer != null && _targetPhysicalDir == null) {
                DumpError("1018", String.Format(CultureInfo.CurrentCulture, CompilerResources.flag_requires_target, "keycontainer"));
                return false;
            }

            if ((_precompilationFlags & PrecompileFlags.FixedNames) != 0 && _targetPhysicalDir == null) {
                DumpError("1019", String.Format(CultureInfo.CurrentCulture, CompilerResources.flag_requires_target, "fixednames"));
                return false;
            }

            if ((_precompilationFlags & PrecompileFlags.DelaySign) != 0) {
                if (_keyFile == null && _keyContainer == null) {
                    DumpError("1013", CompilerResources.invalid_delaysign);
                    return false;
                }
                else if (_targetPhysicalDir == null) {
                    DumpError("1015", String.Format(CultureInfo.CurrentCulture, CompilerResources.flag_requires_target, "delaysign"));
                    return false;
                }
            }

            if ((_precompilationFlags & PrecompileFlags.AllowPartiallyTrustedCallers) != 0) {
                if (_keyFile == null && _keyContainer == null) {
                    DumpError("1014", CompilerResources.invalid_aptca);
                    return false;
                }
                else if (_targetPhysicalDir == null) {
                    DumpError("1016", String.Format(CultureInfo.CurrentCulture, CompilerResources.flag_requires_target, "aptca"));
                    return false;
                }
            }

            return true;
        }

        static readonly char[] invalidVirtualPathChars = new char[] { '*', '?' };

        static bool IsValidVirtualPath(string virtualPath) {
            if (virtualPath == null) {
                return false;
            }

            if (virtualPath.IndexOfAny(invalidVirtualPathChars) >= 0) {
                return false;
            }

            return true;
        }

        static string GetFullPath(string path) {
            try {
                return Path.GetFullPath(path);
            }
            catch {
                DumpError("1010", String.Format(CultureInfo.CurrentCulture, CompilerResources.invalid_path, path));
                return null;
            }
        }

        static void DumpErrors(Exception exception) {

            // Check if one of the inner exceptions is more appropriate
            Exception e2 = GetFormattableException(exception);
            if (e2 != null)
                exception = e2;

            if (IsException(exception, "HttpCompileException")) {
                // NOTE: These are now handled by the callback
#if OLD
            HttpCompileException e = (HttpCompileException)exception;
            DumpCompileErrors(e.Results);
#endif
            }
            else if (IsException(exception, "HttpParseException")) {
                // NOTE: These are now handled by the callback
#if OLD
            HttpParseException e = (HttpParseException)exception;
            DumpMultiParserErrors(e);
#endif
            }
            else
                if (IsException(exception, "ConfigurationException")) {
                    // webForCore: the exception is from the precompilation's load context, read by name
                    Type t = exception.GetType();
                    DumpError((string)t.GetProperty("Filename").GetValue(exception), (int)t.GetProperty("Line").GetValue(exception),
                        false, "ASPCONFIG", (string)t.GetProperty("BareMessage").GetValue(exception));
                }
                else {
                    DumpError(null, 0, false, "ASPRUNTIME", exception.Message);
                }

            if (_showErrorStack)
                DumpExceptionStack(exception);
        }

        static Exception GetFormattableException(Exception e) {

            // If it's one of the know exception types, use it
            if (IsException(e, "HttpCompileException") || IsException(e, "HttpParseException") || IsException(e, "ConfigurationException")) {
                return e;
            }

            Exception inner = e.InnerException;

            // No inner exception: return null
            if (inner == null)
                return null;

            // Try recursively
            return GetFormattableException(inner);
        }

        static void DumpCompileError(CompilerError error) {
            DumpError(error.FileName, error.Line, error.IsWarning, error.ErrorNumber, error.ErrorText);
        }

        static void DumpExceptionStack(Exception e) {
            // webForCore: WebFormsForCore's MSBuild task takes the exception; Silent suppresses the console
            if (OnException != null) {
                OnException(e);
                return;
            }
            if (Silent)
                return;
            Exception subExcep = e.InnerException;
            if (subExcep != null)
                DumpExceptionStack(subExcep);

            string title = "[" + e.GetType().Name + "]";
            if (e.Message != null && e.Message.Length > 0)
                title += ": " + e.Message;
            Console.WriteLine();
            Console.WriteLine(title);
            if (e.StackTrace != null)
                Console.WriteLine(e.StackTrace);
        }

        static void DumpError(string errorNumber, string message) {
            DumpError(null, 0, false /*warning*/, errorNumber, message);
        }

        // Format an error message using the standard syntax (VSWhidbey 266446)
        static void DumpError(string filename, int line, bool warning, string errorNumber, string message) {
            // webForCore: WebFormsForCore records the error and hands it to the MSBuild task, if there is one
            HasErrors = true;
            if (OnError != null) {
                OnError(filename, line, warning, errorNumber, message);
                return;
            }
            if (Silent)
                return;

            if (filename != null) {
                Console.Write(filename);
                Console.Write("(" + line + "): ");
            }

            if (warning)
                Console.Write("warning ");
            else
                Console.Write("error ");
            Console.Write(errorNumber + ": ");
            Console.WriteLine(message);
        }

        class CBMCallback : ClientBuildManagerCallback {
            public override void ReportCompilerError(CompilerError error) {
                DumpCompileError(error);
            }

            public override void ReportParseError(ParserError error) {
                DumpError(error.VirtualPath, error.Line, false, "ASPPARSE", error.ErrorText);
            }

            public override void ReportProgress(string message) {
                // We could display these if we had a 'verbose' mode
                //Console.WriteLine(message);
            }

            public override Object InitializeLifetimeService() {
                return null;
                // never expire lease. The object will get released when the appDomain unloads -
                // in this case, when the process aspnet_compiler.exe ends.
            }
        }
    }
}
