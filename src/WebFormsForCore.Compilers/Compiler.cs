using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WebFormsForCore.Compilers
{
	// webForCore: runs a `dotnet csc.dll` or `dotnet vbc.dll` command line in process, through Roslyn's public API
	// (CONTRIBUTING.md rule 6, #57). It does what csc and vbc do with the command lines System.Web's CodeDom providers
	// give: the arguments parsed by Roslyn's command-line parser (response files, references, /nowarn, /warnaserror,
	// /debug, /win32res, /res, VB's imports and options), the same compilation, and the diagnostics in csc's and vbc's
	// text, which CodeDom parses into CompilerErrors. A command line that asks for something else (analyzers, /doc,
	// /errorlog and the like), or that would make csc or vbc report an error of its own (a source file or reference
	// that isn't there, an output that can't be written), isn't compiled here: Run returns false and ExecutorPortable
	// runs it in a csc or vbc process, which reports it as csc and vbc do.
	public class Compiler
	{
		public static bool Run(string cmd, string args, string workingDirectory, TextWriter output, out int exitCode)
		{
			exitCode = 0;
			var arguments = CommandLineParser.SplitCommandLineIntoArguments(args, false)
				.Where(arg => !arg.StartsWith("/shared") && !arg.StartsWith("/keepalive"))
				.ToArray();
			var compilerPath = arguments.FirstOrDefault() ?? "";
			var file = Path.GetFileName(compilerPath);
			bool visualBasic;
			if (file.Equals("csc.dll", StringComparison.OrdinalIgnoreCase)) visualBasic = false;
			else if (file.Equals("vbc.dll", StringComparison.OrdinalIgnoreCase)) visualBasic = true;
			else return false;

			var compilerArguments = arguments.Skip(1).ToList();
			// csc and vbc read the csc.rsp or vbc.rsp beside them first, unless told not to.
			var defaultResponseFile = Path.Combine(Path.GetDirectoryName(compilerPath) ?? "", visualBasic ? "vbc.rsp" : "csc.rsp");
			if (!SuppressDefaultResponseFile(compilerArguments, visualBasic) && File.Exists(defaultResponseFile))
			{
				compilerArguments.Insert(0, "@" + defaultResponseFile);
			}

			var lib = Environment.GetEnvironmentVariable("LIB");
			CommandLineArguments parsed = visualBasic
				? VisualBasicCommandLineParser.Default.Parse(compilerArguments, workingDirectory, null, lib)
				: CSharpCommandLineParser.Default.Parse(compilerArguments, workingDirectory, null, lib);

			// The compiler's output is kept until it is known that the command line isn't left to csc or vbc.
			var console = new StringWriter();
			var result = new CommandLineCompilation(parsed, visualBasic, console).Run(compilerArguments);
			if (result == null)
			{
				return false;
			}

			output.Write(console.ToString());
			exitCode = result.Value;
			return true;
		}

		// The arguments for `dotnet` that run a csc.dll or vbc.dll command line in a process (when Run returns false).
		// Without /shared and /keepalive, as in process: an application's bin has no VBCSCompiler, and csc waits 20
		// seconds for the server it can't start before it compiles on its own. And the bin has csc.dll and vbc.dll but
		// not their runtimeconfig.json, without which `dotnet` can't run them, so the application's own is given: csc
		// and vbc need nothing more than the application's Microsoft.NETCore.App.
		public static string OutOfProcessArguments(string args)
		{
			var compilerPath = CommandLineParser.SplitCommandLineIntoArguments(args, false).FirstOrDefault() ?? "";
			var file = Path.GetFileName(compilerPath);
			if (!file.Equals("csc.dll", StringComparison.OrdinalIgnoreCase) && !file.Equals("vbc.dll", StringComparison.OrdinalIgnoreCase))
			{
				return args;
			}

			args = Regex.Replace(args, @"(?<=\s)/(shared|keepalive:(""[^""]*""|\S*))(\s|$)", "");
			if (File.Exists(Path.ChangeExtension(compilerPath, ".runtimeconfig.json")))
			{
				return args;
			}

			var entry = Assembly.GetEntryAssembly()?.GetName().Name;
			var runtimeConfig = entry == null ? null : Path.Combine(AppContext.BaseDirectory, entry + ".runtimeconfig.json");
			return runtimeConfig != null && File.Exists(runtimeConfig) ? $"exec --runtimeconfig \"{runtimeConfig}\" {args}" : args;
		}

		private static bool SuppressDefaultResponseFile(IEnumerable<string> args, bool visualBasic) =>
			args.Any(arg => arg.Equals("/noconfig", StringComparison.OrdinalIgnoreCase) ||
				arg.Equals("-noconfig", StringComparison.OrdinalIgnoreCase) ||
				visualBasic && (arg.Equals("/nostdlib", StringComparison.OrdinalIgnoreCase) ||
					arg.Equals("-nostdlib", StringComparison.OrdinalIgnoreCase)));

		// What Roslyn's CommonCompiler (csc and vbc) does with one command line. Run returns csc's exit code, or null
		// for a command line left to csc or vbc.
		private sealed class CommandLineCompilation
		{
			private const int Succeeded = 0, Failed = 1;

			private readonly CommandLineArguments _arguments;
			private readonly bool _visualBasic;
			private readonly TextWriter _console;
			private readonly HashSet<Diagnostic> _reported = new HashSet<Diagnostic>();
			private readonly object _fallbackEncodingLock = new object();
			private Encoding _fallbackEncoding;

			public CommandLineCompilation(CommandLineArguments arguments, bool visualBasic, TextWriter console)
			{
				_arguments = arguments;
				_visualBasic = visualBasic;
				_console = console;
			}

			public int? Run(IEnumerable<string> rawArguments)
			{
				if (!IsSupported(rawArguments))
				{
					return null;
				}

				if (_arguments.DisplayLogo)
				{
					PrintLogo();
				}

				// The command line's own errors and warnings (a bad option, a /sdkpath without System.dll).
				if (Report(_arguments.Errors))
				{
					return Failed;
				}

				var trees = ParseSourceFiles();
				if (trees == null)
				{
					return null;
				}

				var resolver = new ReferenceResolver(_arguments.ReferencePaths, _arguments.BaseDirectory);
				var references = _arguments.ResolveMetadataReferences(resolver).ToList();
				if (references.Any(reference => reference is UnresolvedMetadataReference))
				{
					return null;
				}

				var options = _arguments.CompilationOptions
					.WithMetadataReferenceResolver(resolver)
					.WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default)
					.WithXmlReferenceResolver(new XmlFileResolver(_arguments.BaseDirectory))
					.WithStrongNameProvider(new DesktopStrongNameProvider(_arguments.KeyFileSearchPaths, Path.GetTempPath()))
					.WithSourceReferenceResolver(new SourceFileResolver(ImmutableArray<string>.Empty, _arguments.BaseDirectory, _arguments.PathMap));
				Compilation compilation = _visualBasic
					? VisualBasicCompilation.Create(_arguments.CompilationName, trees, references, (VisualBasicCompilationOptions)options)
					: CSharpCompilation.Create(_arguments.CompilationName, trees, references, (CSharpCompilationOptions)options);

				try
				{
					return Compile(compilation);
				}
				catch (LeftToCompilerException)
				{
					return null;
				}
			}

			// The command lines left to csc and vbc: those with options this class doesn't implement, and those csc
			// or vbc reports errors on before compiling (other than the command line's own, which Run reports).
			private bool IsSupported(IEnumerable<string> rawArguments)
			{
				var a = _arguments;
				if (a.DisplayHelp || a.DisplayVersion || a.DisplayLangVersions ||
					!a.AnalyzerReferences.IsEmpty || !a.AnalyzerConfigPaths.IsEmpty || !a.AdditionalFiles.IsEmpty ||
					!a.EmbeddedFiles.IsEmpty || a.ErrorLogOptions != null || a.TouchedFilesPath != null ||
					a.AppConfigPath != null || a.SourceLink != null || a.DocumentationPath != null ||
					a.OutputRefFilePath != null || a.EmitOptions.EmitMetadataOnly || a.ReportAnalyzer ||
					a.ReportInternalsVisibleToAttributes || a.PreferredUILang != null || a.InteractiveMode ||
					!a.ScriptArguments.IsEmpty || !a.PathMap.IsEmpty || a.OutputFileName == null ||
					a.ParseOptions.Features.ContainsKey("debug-determinism") ||
					a.SourceFiles.Any(source => source.IsScript || source.IsInputRedirected))
				{
					return false;
				}

				if (!_visualBasic && !a.PrintFullPaths)
				{
					return false;
				}

				// csc's /errorendlocation (the end of the span too) and vbc's /verbose (the references listed) change
				// the output. Roslyn doesn't make them public, so they are looked for.
				var options = _visualBasic ? new[] { "verbose", "verbose+" } : new[] { "errorendlocation" };
				return !Flatten(rawArguments, a.BaseDirectory, 0).Any(arg =>
					arg.Length > 1 && (arg[0] == '/' || arg[0] == '-') && options.Contains(arg.Substring(1), StringComparer.OrdinalIgnoreCase));
			}

			// The arguments with the response files' arguments in their place.
			private static IEnumerable<string> Flatten(IEnumerable<string> args, string baseDirectory, int depth)
			{
				foreach (var arg in args)
				{
					if (!arg.StartsWith("@") || depth >= 8)
					{
						yield return arg;
						continue;
					}

					string[] lines;
					try
					{
						lines = File.ReadAllLines(Path.GetFullPath(arg.Substring(1).Trim('"'), baseDirectory));
					}
					catch (Exception)
					{
						// The parser has reported it, and the command line is left to vbc.
						continue;
					}

					foreach (var line in lines)
					{
						foreach (var inner in Flatten(CommandLineParser.SplitCommandLineIntoArguments(line, true), baseDirectory, depth + 1))
						{
							yield return inner;
						}
					}
				}
			}

			private void PrintLogo()
			{
				// csc's and vbc's logo (in English): the version is the compiler assembly's, with its commit hash.
				var assembly = (_visualBasic ? typeof(VisualBasicCompilation) : typeof(CSharpCompilation)).Assembly;
				var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
				var plus = version.IndexOf('+');
				var hash = plus >= 0 ? version.Substring(plus + 1) : "";
				if (plus >= 0)
				{
					version = version.Substring(0, plus);
				}

				_console.WriteLine("{0} version {1}",
					_visualBasic ? "Microsoft (R) Visual Basic Compiler" : "Microsoft (R) Visual C# Compiler",
					$"{version} ({hash})");
				_console.WriteLine("Copyright (C) Microsoft Corporation. All rights reserved.");
				_console.WriteLine();
			}

			// Reads and parses the source files as csc and vbc do: null if one can't be read, or (C#) is given twice.
			private SyntaxTree[] ParseSourceFiles()
			{
				var sourceFiles = _arguments.SourceFiles;
				var trees = new SyntaxTree[sourceFiles.Length];
				bool failed = false;
				void parse(int i)
				{
					var text = TryReadFile(sourceFiles[i].Path);
					if (text == null)
					{
						failed = true;
						return;
					}

					trees[i] = _visualBasic
						? VisualBasicSyntaxTree.ParseText(text, (VisualBasicParseOptions)_arguments.ParseOptions, sourceFiles[i].Path)
						: CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)_arguments.ParseOptions, sourceFiles[i].Path);
				}

				if (_arguments.CompilationOptions.ConcurrentBuild)
				{
					Parallel.For(0, sourceFiles.Length, parse);
				}
				else
				{
					for (int i = 0; i < sourceFiles.Length; i++)
					{
						parse(i);
					}
				}

				if (failed)
				{
					return null;
				}

				// csc warns about a file given twice (CS2002) and compiles it once.
				if (!_visualBasic &&
					sourceFiles.Select(source => Path.GetFullPath(source.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sourceFiles.Length)
				{
					return null;
				}

				return trees;
			}

			// csc's and vbc's reading of a source file: the encoding of its BOM, or of /codepage, or else UTF-8 and,
			// if it isn't valid UTF-8, the ANSI code page. Null if it can't be read, which csc reports (CS2001, CS1504).
			private SourceText TryReadFile(string path)
			{
				try
				{
					using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.None);
					var encoding = _arguments.Encoding;
					if (encoding == null)
					{
						try
						{
							return SourceText.From(stream, new UTF8Encoding(false, true), _arguments.ChecksumAlgorithm, throwIfBinaryDetected: false);
						}
						catch (DecoderFallbackException)
						{
							stream.Seek(0, SeekOrigin.Begin);
							encoding = FallbackEncoding();
						}
					}

					return SourceText.From(stream, encoding, _arguments.ChecksumAlgorithm, throwIfBinaryDetected: _arguments.Encoding == null);
				}
				catch (Exception)
				{
					return null;
				}
			}

			private Encoding FallbackEncoding()
			{
				lock (_fallbackEncodingLock)
				{
					if (_fallbackEncoding == null)
					{
						Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
						_fallbackEncoding = TryGetEncoding(0) ?? TryGetEncoding(1252) ?? Encoding.Latin1;
					}

					return _fallbackEncoding;
				}

				static Encoding TryGetEncoding(int codePage)
				{
					try
					{
						return Encoding.GetEncoding(codePage);
					}
					catch (Exception)
					{
						return null;
					}
				}
			}

			private int Compile(Compilation compilation)
			{
				// As in csc, an error that can't be suppressed in the parse diagnostics, and then in the declaration
				// diagnostics, ends the compile.
				var parse = compilation.GetParseDiagnostics();
				bool errors = Report(parse);
				if (parse.Any(IsUnsuppressableError))
				{
					return Failed;
				}

				var declaration = compilation.GetDeclarationDiagnostics();
				errors |= Report(declaration);
				if (declaration.Any(IsUnsuppressableError))
				{
					return Failed;
				}

				var outputName = _arguments.OutputFileName;
				var peFilePath = _arguments.GetOutputFilePath(outputName);
				var pdbFilePath = _arguments.GetPdbFilePath(outputName);
				var emitOptions = _arguments.EmitOptions
					.WithOutputNameOverride(outputName)
					.WithPdbFilePath(pdbFilePath);
				if (_arguments.ParseOptions.Features.ContainsKey("pdb-path-determinism") && !string.IsNullOrEmpty(emitOptions.PdbFilePath))
				{
					emitOptions = emitOptions.WithPdbFilePath(Path.GetFileName(emitOptions.PdbFilePath));
				}

				using var pe = new MemoryStream();
				using var pdb = _arguments.EmitPdbFile ? new MemoryStream() : null;
				using var win32Resources = Win32Resources(compilation);
				var result = compilation.Emit(pe, pdb, win32Resources: win32Resources, manifestResources: _arguments.ManifestResources, options: emitOptions);
				errors |= Report(result.Diagnostics);
				if (result.Success == errors)
				{
					// An emit that failed without an error to show, or succeeded with one.
					throw new LeftToCompilerException();
				}

				if (result.Success)
				{
					// Like csc, only a compile that succeeds writes its files.
					WriteFile(peFilePath, pe);
					if (pdb != null)
					{
						WriteFile(pdbFilePath, pdb);
					}
				}

				return errors ? Failed : Succeeded;
			}

			// csc's Win32 resources: the /win32res file, or the default ones (the version resource, the manifest and
			// /win32icon).
			private Stream Win32Resources(Compilation compilation)
			{
				try
				{
					if (_arguments.Win32ResourceFile != null)
					{
						return OpenRead(_arguments.Win32ResourceFile);
					}

					using var manifest = compilation.Options.OutputKind == OutputKind.NetModule || _arguments.Win32Manifest == null
						? null
						: OpenRead(_arguments.Win32Manifest);
					using var icon = _arguments.Win32Icon == null ? null : OpenRead(_arguments.Win32Icon);
					return compilation.CreateDefaultWin32Resources(true, _arguments.NoWin32Manifest, manifest, icon);
				}
				catch (Exception)
				{
					// csc reports it (CS1566, CS7065, CS1926 and the like).
					throw new LeftToCompilerException();
				}
			}

			private Stream OpenRead(string path) =>
				new FileStream(Path.GetFullPath(path, _arguments.BaseDirectory), FileMode.Open, FileAccess.Read, FileShare.Read);

			private static void WriteFile(string path, MemoryStream contents)
			{
				try
				{
					using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
					contents.WriteTo(file);
				}
				catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
				{
					// csc deletes a file another process has open and tries again, or reports CS2012.
					throw new LeftToCompilerException();
				}
			}

			private static bool IsUnsuppressableError(Diagnostic diagnostic) =>
				diagnostic.DefaultSeverity == DiagnosticSeverity.Error &&
				diagnostic.Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable);

			// csc's ReportDiagnostics: each diagnostic once, without the hidden and the suppressed ones. True if one
			// was an error.
			private bool Report(IEnumerable<Diagnostic> diagnostics)
			{
				bool errors = false;
				foreach (var diagnostic in diagnostics)
				{
					if (_reported.Contains(diagnostic) || diagnostic.Severity == DiagnosticSeverity.Hidden)
					{
						continue;
					}

					if (diagnostic.IsSuppressed)
					{
						continue;
					}

					errors |= diagnostic.Severity == DiagnosticSeverity.Error;
					_console.WriteLine(_visualBasic ? FormatVisualBasic(diagnostic) : FormatCSharp(diagnostic));
					_reported.Add(diagnostic);
				}

				return errors;
			}

			// csc's text with /fullpaths: CSharpDiagnosticFormatter's, with the file's normalized full path.
			private string FormatCSharp(Diagnostic diagnostic) =>
				NormalizePath(diagnostic, CSharpDiagnosticFormatter.Instance.Format(diagnostic, null));

			// vbc's text: VisualBasicDiagnosticFormatter's, with the file's normalized full path, then the source
			// line with the span underlined; "vbc : " before a diagnostic without a location.
			private string FormatVisualBasic(Diagnostic diagnostic)
			{
				var location = diagnostic.Location;
				if (location.Kind == LocationKind.None || location.Kind == LocationKind.MetadataFile)
				{
					return "vbc : " + VisualBasicDiagnosticFormatter.Instance.Format(diagnostic, null);
				}

				var text = location.IsInSource ? location.SourceTree.GetText() : null;
				var span = location.SourceSpan;
				if (text == null || text.Length < span.End || !location.GetLineSpan().IsValid || !location.GetMappedLineSpan().IsValid)
				{
					// vbc writes these without their location.
					throw new LeftToCompilerException();
				}

				var sb = new StringBuilder();
				sb.AppendLine(NormalizePath(diagnostic, VisualBasicDiagnosticFormatter.Instance.Format(diagnostic, null)));

				int lineNumber = text.Lines.IndexOf(span.Start);
				var line = text.Lines[lineNumber];
				if (span.IsEmpty && line.Start == span.End && lineNumber > 0)
				{
					// An empty span at the start of a line is something missing at the end of the line before.
					lineNumber--;
					line = text.Lines[lineNumber];
				}

				while (line.Start < span.End)
				{
					sb.AppendLine();
					sb.AppendLine(line.ToString().Replace("\t", "    "));
					for (int position = Math.Min(span.Start, line.Start); position < Math.Min(line.End, span.Start); position++)
					{
						sb.Append(text[position] == '\t' ? "    " : " ");
					}

					if (span.IsEmpty)
					{
						sb.Append('~');
					}
					else
					{
						for (int position = Math.Max(span.Start, line.Start); position <= Math.Min(span.End - 1, line.End - 1); position++)
						{
							sb.Append(text[position] == '\t' ? "~~~~" : "~");
						}
					}

					for (int position = Math.Min(span.End, line.End); position < line.End; position++)
					{
						sb.Append(text[position] == '\t' ? "    " : " ");
					}

					lineNumber++;
					if (lineNumber >= text.Lines.Count)
					{
						break;
					}

					line = text.Lines[lineNumber];
				}

				return sb.ToString();
			}

			// The formatters write a file's path as it is; csc and vbc resolve it against the base directory (or the
			// file a #line is in) and normalize it.
			private string NormalizePath(Diagnostic diagnostic, string formatted)
			{
				var location = diagnostic.Location;
				if (location.Kind != LocationKind.SourceFile && location.Kind != LocationKind.XmlFile && location.Kind != LocationKind.ExternalFile)
				{
					return formatted;
				}

				var span = location.GetLineSpan();
				var mapped = location.GetMappedLineSpan();
				if (!span.IsValid || !mapped.IsValid)
				{
					return formatted;
				}

				var path = mapped.HasMappedPath ? mapped.Path : span.Path;
				var basePath = mapped.HasMappedPath ? span.Path : null;
				var normalized = NormalizeRelativePath(path, basePath, _arguments.BaseDirectory);
				return normalized == null || !formatted.StartsWith(path, StringComparison.Ordinal)
					? formatted
					: normalized + formatted.Substring(path.Length);
			}

			// Roslyn's FileUtilities.NormalizeRelativePath, for full paths and paths relative to a directory.
			private static string NormalizeRelativePath(string path, string basePath, string baseDirectory)
			{
				if (string.IsNullOrEmpty(path) || path.Contains("://") || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
				{
					return null;
				}

				string resolved;
				if (Path.IsPathFullyQualified(path))
				{
					resolved = path;
				}
				else if (!Path.IsPathRooted(path))
				{
					var directory = baseDirectory;
					if (!string.IsNullOrEmpty(basePath) && !Path.IsPathFullyQualified(basePath) && Path.IsPathRooted(basePath))
					{
						throw new LeftToCompilerException();
					}

					if (!string.IsNullOrEmpty(basePath) && (Path.IsPathFullyQualified(basePath) || baseDirectory != null))
					{
						directory = Path.GetDirectoryName(Path.IsPathFullyQualified(basePath) ? basePath : Path.Combine(baseDirectory, basePath));
					}

					if (directory == null)
					{
						return null;
					}

					resolved = Path.Combine(directory, path);
				}
				else
				{
					// Relative to the current drive's root, or to a drive's current directory (Windows).
					throw new LeftToCompilerException();
				}

				try
				{
					return Path.IsPathFullyQualified(resolved) ? Path.GetFullPath(resolved) : null;
				}
				catch (Exception)
				{
					return null;
				}
			}
		}

		// csc's resolution of /reference: a full path, or a path relative to the base directory and then to each
		// /lib directory. A reference that isn't found is left to csc, which reports it.
		private sealed class ReferenceResolver : MetadataReferenceResolver
		{
			private readonly ImmutableArray<string> _searchPaths;
			private readonly string _baseDirectory;

			public ReferenceResolver(ImmutableArray<string> searchPaths, string baseDirectory)
			{
				_searchPaths = searchPaths;
				_baseDirectory = baseDirectory;
			}

			public override ImmutableArray<PortableExecutableReference> ResolveReference(string reference, string baseFilePath, MetadataReferenceProperties properties)
			{
				var path = ResolvePath(reference, baseFilePath);
				return path == null
					? ImmutableArray<PortableExecutableReference>.Empty
					: ImmutableArray.Create(MetadataReference.CreateFromFile(path, properties));
			}

			private string ResolvePath(string reference, string baseFilePath)
			{
				if (string.IsNullOrEmpty(reference))
				{
					return null;
				}

				var candidates = new List<string>();
				if (Path.IsPathFullyQualified(reference))
				{
					candidates.Add(reference);
				}
				else if (!Path.IsPathRooted(reference))
				{
					var directory = baseFilePath != null && Path.IsPathFullyQualified(baseFilePath) ? Path.GetDirectoryName(baseFilePath) : _baseDirectory;
					if (directory != null)
					{
						candidates.Add(Path.Combine(directory, reference));
					}

					// Only a plain relative path (not .\x or ..\x) is looked for in the /lib directories.
					if (!IsDotRelative(reference))
					{
						candidates.AddRange(_searchPaths.Select(searchPath => Path.Combine(searchPath, reference)));
					}
				}

				try
				{
					var found = candidates.FirstOrDefault(candidate => Path.IsPathFullyQualified(candidate) && File.Exists(candidate));
					return found == null ? null : Path.GetFullPath(found);
				}
				catch (Exception)
				{
					return null;
				}
			}

			private static bool IsDotRelative(string path) =>
				path == "." || path == ".." ||
				path.Length > 1 && path[0] == '.' && IsSeparator(path[1]) ||
				path.Length > 2 && path[0] == '.' && path[1] == '.' && IsSeparator(path[2]);

			private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

			public override bool Equals(object other) =>
				other is ReferenceResolver resolver && resolver._baseDirectory == _baseDirectory && resolver._searchPaths.SequenceEqual(_searchPaths);

			public override int GetHashCode() => HashCode.Combine(_baseDirectory, _searchPaths.Length);
		}

		private sealed class LeftToCompilerException : Exception
		{
		}
	}
}
