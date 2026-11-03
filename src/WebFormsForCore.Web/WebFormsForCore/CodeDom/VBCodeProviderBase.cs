using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

using W = WebFormsForCore.CodeDom.Compiler;

namespace WebFormsForCore.CodeDom.Compiler;

//
// Summary:
//     Provides access to instances of the Visual Basic code generator and code compiler.
//
// webForCore: on .NET Framework, Microsoft's VBCodeProvider derives from Microsoft.VisualBasic.VBCodeProvider.
// Here it derives from this class, which takes the generator, the converters and member generation from System.CodeDom's
// public Microsoft.VisualBasic.VBCodeProvider instead of creating its internal types by reflection (#57).
public class VBCodeProviderBase : W.CodeDomProvider
{
	private readonly global::Microsoft.VisualBasic.VBCodeProvider _provider;

	//
	// Summary:
	//     Gets the file name extension to use when creating source code files.
	//
	// Returns:
	//     The file name extension to use for generated source code files.
	public override string FileExtension => "vb";

	//
	// Summary:
	//     Gets a language features identifier.
	//
	// Returns:
	//     A System.CodeDom.Compiler.LanguageOptions that indicates special features of
	//     the language.
	public override LanguageOptions LanguageOptions => LanguageOptions.CaseInsensitive;

	//
	// Summary:
	//     Initializes a new instance of the Microsoft.VisualBasic.VBCodeProvider class.
	public VBCodeProviderBase(): base()
	{
		_provider = new global::Microsoft.VisualBasic.VBCodeProvider();
	}

	//
	// Summary:
	//     Initializes a new instance of the Microsoft.VisualBasic.VBCodeProvider class
	//     by using the specified provider options.
	//
	// Parameters:
	//   providerOptions:
	//     A System.Collections.Generic.IDictionary`2 object that contains the provider
	//     options.
	//
	// Exceptions:
	//   T:System.ArgumentNullException:
	//     providerOptions is null.
	public VBCodeProviderBase(IDictionary<string, string> providerOptions): base()
	{
		if (providerOptions == null)
		{
			throw new ArgumentNullException("providerOptions");
		}

		_provider = new global::Microsoft.VisualBasic.VBCodeProvider(providerOptions);
	}

	//
	// Summary:
	//     Gets an instance of the Visual Basic code generator.
	//
	// Returns:
	//     An instance of the Visual Basic System.CodeDom.Compiler.ICodeGenerator implementation.
	[Obsolete("ICodeGenerator has been deprecated. Use the methods directly on the CodeDomProvider class instead.")]
	public override ICodeGenerator CreateGenerator()
	{
		return _provider.CreateGenerator();
	}

	//
	// Summary:
	//     Gets an instance of the Visual Basic code compiler.
	//
	// Returns:
	//     An instance of the Visual Basic System.CodeDom.Compiler.ICodeCompiler implementation.
	[Obsolete("ICodeCompiler has been deprecated. Use the methods directly on the CodeDomProvider class instead.")]
	public override ICodeCompiler CreateCompiler()
	{
		return _provider.CreateCompiler();
	}

	//
	// Summary:
	//     Gets a System.ComponentModel.TypeConverter for the specified type of object.
	//
	//
	// Parameters:
	//   type:
	//     The type of object to retrieve a type converter for.
	//
	// Returns:
	//     A System.ComponentModel.TypeConverter for the specified type.
	public override TypeConverter GetConverter(Type type)
	{
		if (type == typeof(MemberAttributes) || type == typeof(System.Reflection.TypeAttributes))
		{
			return _provider.GetConverter(type);
		}

		return base.GetConverter(type);
	}

	//
	// Summary:
	//     Generates code for the specified class member using the specified text writer
	//     and code generator options.
	//
	// Parameters:
	//   member:
	//     A System.CodeDom.CodeTypeMember to generate code for.
	//
	//   writer:
	//     The System.IO.TextWriter to write to.
	//
	//   options:
	//     The System.CodeDom.Compiler.CodeGeneratorOptions to use when generating the code.
	public override void GenerateCodeFromMember(CodeTypeMember member, TextWriter writer, CodeGeneratorOptions options)
	{
		_provider.GenerateCodeFromMember(member, writer, options);
	}
}
