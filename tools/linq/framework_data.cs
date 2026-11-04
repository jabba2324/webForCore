// Reads .NET Framework 4.8's System.Data.Linq.dll as data, for webForCore's LINQ to SQL (#142): the string resources
// (names and messages), and, for each method of the generated Error classes and each member of the generated Strings
// classes, its name, parameters, the exception type it creates (with the constructor's parameter types) and the
// resource its message comes from. Referencesource doesn't publish those generated classes; webForCore's generator
// (tools/linq/generate.cs) writes its own from this data, so that each error has .NET Framework's exception type and
// message. Only names, messages and types are written: no IL and no code.
//
// Usage: dotnet run tools/linq/framework_data.cs -- <System.Data.Linq.dll> <output.json> [<committed.json>]
// With a third argument, the output is compared with that file (the committed data), and any difference fails, apart
// from the file version and MVID, which a servicing update of .NET Framework changes.
// The CI job linq-framework-data runs it on Windows with .NET Framework 4.8's System.Data.Linq.dll and the committed
// src/webForCore.System.Data.Linq/FrameworkData/System.Data.Linq.json.
//
// It reads metadata with System.Reflection.Metadata, and simulates the evaluation stack of each method's body to find
// the `newobj`'s type and arguments; a body it doesn't understand is reported, and the tool fails.
#:property NuGetAudit=false
#:property PublishAot=false
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: dotnet run tools/linq/framework_data.cs -- <System.Data.Linq.dll> <output.json> [<committed.json>]");
    return 2;
}

string dllPath = Path.GetFullPath(args[0]);
using var stream = File.OpenRead(dllPath);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
var reader = new FrameworkDataReader(pe, md);

var problems = new List<string>();
var root = new JsonObject
{
    ["comment"] = "Read from .NET Framework's System.Data.Linq.dll by tools/linq/framework_data.cs (#142): names, messages and exception types only.",
    ["assembly"] = md.GetAssemblyDefinition().GetAssemblyName().FullName,
    ["fileVersion"] = FileVersionInfo.GetVersionInfo(dllPath).FileVersion,
    ["mvid"] = md.GetGuid(md.GetModuleDefinition().Mvid).ToString(),
    ["resources"] = reader.ReadResources(),
};

var classes = new JsonArray();
foreach (var handle in md.TypeDefinitions)
{
    var type = md.GetTypeDefinition(handle);
    string name = md.GetString(type.Name);
    if (!type.GetDeclaringType().IsNil || (name != "Error" && name != "Strings" && name != "SR"))
        continue;
    classes.Add(reader.ReadClass(type, problems));
}
root["classes"] = classes;

var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
string outputPath = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, root.ToJsonString(options).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
Console.WriteLine($"{dllPath} ({root["fileVersion"]}): {classes.Count} classes, written to {outputPath}");

// The output is written either way, with each member the tool didn't understand marked "problem".
foreach (var problem in problems)
    Console.Error.WriteLine(problem);
if (problems.Count > 0)
    return 1;

if (args.Length == 3)
{
    var committed = JsonNode.Parse(File.ReadAllText(args[2]))!.AsObject();
    foreach (var key in new[] { "fileVersion", "mvid" })
    {
        if (committed[key]?.ToString() != root[key]?.ToString())
            Console.WriteLine($"Note: {key} is {root[key]}, the committed data's is {committed[key]}");
        committed.Remove(key);
        root.Remove(key);
    }
    if (!JsonNode.DeepEquals(committed, root))
    {
        Console.Error.WriteLine($"{args[2]} isn't what {dllPath} has: compare it with {outputPath}");
        return 1;
    }
    Console.WriteLine($"{args[2]} matches.");
}
return 0;

sealed class FrameworkDataReader(PEReader pe, MetadataReader md)
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    private readonly TypeNameProvider _names = new();

    // Each manifest resource that is a .resources file, with its strings by name.
    public JsonArray ReadResources()
    {
        var result = new JsonArray();
        var directory = pe.PEHeaders.CorHeader!.ResourcesDirectory;
        foreach (var handle in md.ManifestResources)
        {
            var resource = md.GetManifestResource(handle);
            string name = md.GetString(resource.Name);
            if (!resource.Implementation.IsNil || !name.EndsWith(".resources", StringComparison.Ordinal))
                continue;
            var data = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader();
            data.Offset = checked((int)resource.Offset);
            int length = data.ReadInt32();
            byte[] bytes = data.ReadBytes(length);
            var strings = new SortedDictionary<string, string>(StringComparer.Ordinal);
            using (var resources = new ResourceReader(new MemoryStream(bytes)))
            {
                var e = resources.GetEnumerator();
                while (e.MoveNext())
                    strings[(string)e.Key] = e.Value as string ?? throw new InvalidDataException($"{name}: {e.Key} isn't a string");
            }
            var values = new JsonObject();
            foreach (var (key, value) in strings)
                values[key] = value;
            result.Add(new JsonObject { ["name"] = name, ["strings"] = values });
        }
        return result;
    }

    public JsonObject ReadClass(TypeDefinition type, List<string> problems)
    {
        string ns = md.GetString(type.Namespace);
        string typeName = ns.Length == 0 ? md.GetString(type.Name) : $"{ns}.{md.GetString(type.Name)}";
        var result = new JsonObject { ["type"] = typeName };

        // SR: its string constants (the resource names) and the resource base name its ResourceManager uses.
        var constants = new JsonArray();
        foreach (var fieldHandle in type.GetFields())
        {
            var field = md.GetFieldDefinition(fieldHandle);
            var constant = field.GetDefaultValue();
            if (constant.IsNil)
                continue;
            var value = md.GetConstant(constant);
            if (value.TypeCode != ConstantTypeCode.String)
                continue;
            constants.Add(new JsonObject { ["name"] = md.GetString(field.Name), ["value"] = md.GetBlobReader(value.Value).ReadUTF16(md.GetBlobReader(value.Value).Length) });
        }
        if (constants.Count > 0)
            result["constants"] = constants;

        var members = new JsonArray();
        var literals = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var methodHandle in type.GetMethods())
        {
            var method = md.GetMethodDefinition(methodHandle);
            string name = md.GetString(method.Name);
            if (md.GetString(type.Name) == "SR")
            {
                // Only the string literals of SR's methods: the resource base name is one of them.
                foreach (var literal in StringLiterals(method))
                    literals.Add(literal);
                continue;
            }
            if (name == ".cctor" || name == ".ctor")
                continue;
            members.Add(ReadMember(typeName, method, problems));
        }
        if (literals.Count > 0)
            result["literals"] = new JsonArray(literals.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray());
        if (members.Count > 0)
            result["members"] = members;
        return result;
    }

    private JsonObject ReadMember(string typeName, MethodDefinition method, List<string> problems)
    {
        string name = md.GetString(method.Name);
        var signature = method.DecodeSignature(_names, null);
        var parameterNames = new string[signature.ParameterTypes.Length];
        foreach (var p in method.GetParameters())
        {
            var parameter = md.GetParameter(p);
            if (parameter.SequenceNumber > 0)
                parameterNames[parameter.SequenceNumber - 1] = md.GetString(parameter.Name);
        }
        var parameters = new JsonArray();
        for (int i = 0; i < parameterNames.Length; i++)
            parameters.Add(new JsonObject { ["name"] = parameterNames[i], ["type"] = signature.ParameterTypes[i] });

        var member = new JsonObject
        {
            ["name"] = name.StartsWith("get_", StringComparison.Ordinal) && (method.Attributes & MethodAttributes.SpecialName) != 0 ? name[4..] : name,
            ["kind"] = name.StartsWith("get_", StringComparison.Ordinal) && (method.Attributes & MethodAttributes.SpecialName) != 0 ? "property" : "method",
            ["access"] = (method.Attributes & MethodAttributes.MemberAccessMask) switch
            {
                MethodAttributes.Public => "public",
                MethodAttributes.Assembly => "internal",
                MethodAttributes.Private => "private",
                var other => other.ToString(),
            },
            ["parameters"] = parameters,
            ["returns"] = signature.ReturnType,
        };
        var (value, problem) = Evaluate(method, parameterNames);
        if (problem != null)
        {
            problems.Add($"{typeName}.{name}: {problem}");
            member["problem"] = problem;
        }
        else
            member["value"] = value;
        return member;
    }

    private IEnumerable<string> StringLiterals(MethodDefinition method)
    {
        if (method.RelativeVirtualAddress == 0)
            yield break;
        var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
        while (il.RemainingBytes > 0)
        {
            var opCode = ReadOpCode(ref il);
            if (opCode == OpCodes.Ldstr)
                yield return md.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32()));
            else
                SkipOperand(ref il, opCode);
        }
    }

    // What the method returns, from a simulation of its evaluation stack: arguments, string and integer constants,
    // object arrays, calls and constructors. Anything else (branches, fields) is a problem to report.
    private (JsonNode? Value, string? Problem) Evaluate(MethodDefinition method, string[] parameterNames)
    {
        var body = pe.GetMethodBody(method.RelativeVirtualAddress);
        var il = body.GetILReader();
        var stack = new Stack<Node>();
        var locals = new Dictionary<int, Node>();
        Node? returned = null;
        while (il.RemainingBytes > 0)
        {
            var opCode = ReadOpCode(ref il);
            switch (opCode.Name)
            {
                case "nop":
                    break;
                case "ldarg.0": case "ldarg.1": case "ldarg.2": case "ldarg.3":
                    stack.Push(Argument(opCode.Name[^1] - '0', parameterNames));
                    break;
                case "ldarg.s":
                    stack.Push(Argument(il.ReadByte(), parameterNames));
                    break;
                case "ldstr":
                    stack.Push(new Node { Json = new JsonObject { ["literal"] = md.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32())) } });
                    break;
                case "ldnull":
                    stack.Push(new Node { Json = new JsonObject { ["null"] = true } });
                    break;
                case "ldc.i4.s":
                    stack.Push(Integer(il.ReadSByte()));
                    break;
                case "ldc.i4":
                    stack.Push(Integer(il.ReadInt32()));
                    break;
                case "ldc.i4.m1":
                    stack.Push(Integer(-1));
                    break;
                case { } ldc when ldc.StartsWith("ldc.i4.", StringComparison.Ordinal):
                    stack.Push(Integer(ldc[^1] - '0'));
                    break;
                case "box":
                    il.ReadInt32();
                    break;
                case "newarr":
                {
                    string elementType = TypeName(MetadataTokens.EntityHandle(il.ReadInt32()));
                    int count = stack.Pop().Integer ?? -1;
                    if (count < 0)
                        return (null, "an array whose length isn't a constant");
                    stack.Push(new Node { Items = new Node?[count], ElementType = elementType });
                    break;
                }
                case "dup":
                    stack.Push(stack.Peek());
                    break;
                case "stelem.ref":
                {
                    var item = stack.Pop();
                    int? index = stack.Pop().Integer;
                    var array = stack.Pop();
                    if (array.Items == null || index == null)
                        return (null, "an array store that isn't into a constant index of a new array");
                    array.Items[index.Value] = item;
                    break;
                }
                case "stloc.0": case "stloc.1": case "stloc.2": case "stloc.3":
                    locals[opCode.Name[^1] - '0'] = stack.Pop();
                    break;
                case "ldloc.0": case "ldloc.1": case "ldloc.2": case "ldloc.3":
                    stack.Push(locals[opCode.Name[^1] - '0']);
                    break;
                case "br.s":
                {
                    // A debug build's jump to the return that follows.
                    if (il.ReadSByte() != 0)
                        return (null, "a branch");
                    break;
                }
                case "call": case "callvirt": case "newobj":
                {
                    var (declaringType, methodName, signature) = Method(MetadataTokens.EntityHandle(il.ReadInt32()));
                    var arguments = new Node[signature.ParameterTypes.Length];
                    for (int i = arguments.Length - 1; i >= 0; i--)
                        arguments[i] = stack.Pop();
                    if (opCode.Name != "newobj" && signature.Header.IsInstance)
                        return (null, $"an instance call to {declaringType}.{methodName}");
                    var json = new JsonObject
                    {
                        [opCode.Name == "newobj" ? "new" : "call"] = opCode.Name == "newobj" ? declaringType : $"{declaringType}.{methodName}",
                        ["parameterTypes"] = new JsonArray(signature.ParameterTypes.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray()),
                        ["arguments"] = new JsonArray(arguments.Select(a => a.ToJson()).ToArray()),
                    };
                    if (signature.ReturnType != "System.Void" || opCode.Name == "newobj")
                        stack.Push(new Node { Json = json });
                    else
                        return (null, $"a call to {declaringType}.{methodName} that returns nothing");
                    break;
                }
                case "ret":
                    returned = stack.Count > 0 ? stack.Pop() : null;
                    if (stack.Count > 0)
                        return (null, "values left on the stack");
                    break;
                default:
                    return (null, $"the instruction {opCode.Name}");
            }
        }
        return (returned?.ToJson(), returned == null ? "no value returned" : null);
    }

    private static Node Argument(int index, string[] parameterNames) =>
        new() { Json = new JsonObject { ["parameter"] = parameterNames[index] } };

    private static Node Integer(int value) => new() { Integer = value, Json = new JsonObject { ["integer"] = value } };

    private (string Type, string Name, MethodSignature<string> Signature) Method(EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var method = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                return (TypeName(method.GetDeclaringType()), md.GetString(method.Name), method.DecodeSignature(_names, null));
            }
            case HandleKind.MemberReference:
            {
                var member = md.GetMemberReference((MemberReferenceHandle)handle);
                return (TypeName(member.Parent), md.GetString(member.Name), member.DecodeMethodSignature(_names, null));
            }
            default:
                throw new InvalidDataException($"A call to a {handle.Kind}");
        }
    }

    private string TypeName(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => _names.GetTypeFromDefinition(md, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => _names.GetTypeFromReference(md, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(_names, null),
        _ => throw new InvalidDataException($"A type that is a {handle.Kind}"),
    };

    private static OpCode ReadOpCode(ref BlobReader il)
    {
        byte first = il.ReadByte();
        short value = first == 0xFE ? (short)(0xFE00 | il.ReadByte()) : first;
        return OpCodesByValue[value];
    }

    private static void SkipOperand(ref BlobReader il, OpCode opCode)
    {
        switch (opCode.OperandType)
        {
            case OperandType.InlineNone:
                break;
            case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar:
                il.Offset += 1;
                break;
            case OperandType.InlineVar:
                il.Offset += 2;
                break;
            case OperandType.InlineI8: case OperandType.InlineR:
                il.Offset += 8;
                break;
            case OperandType.InlineSwitch:
                il.Offset += 4 * il.ReadInt32();
                break;
            default:
                il.Offset += 4;
                break;
        }
    }

    // A value on the simulated stack: JSON, or a new array (whose items are filled in later), or an integer constant.
    private sealed class Node
    {
        public JsonObject? Json;
        public int? Integer;
        public Node?[]? Items;
        public string? ElementType;

        public JsonNode ToJson() => Items == null
            ? Json!.DeepClone()
            : new JsonObject { ["array"] = ElementType, ["items"] = new JsonArray(Items.Select(i => i?.ToJson() ?? new JsonObject { ["null"] = true }).ToArray()) };
    }
}

// Type names as C# would write them in full: System.String, System.Object[], System.Collections.Generic.List`1<System.Int32>.
sealed class TypeNameProvider : ISignatureTypeProvider<string, object?>
{
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Void => "System.Void",
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        _ => typeCode.ToString(),
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        if (handle.IsNil)
            return "";
        var type = reader.GetTypeDefinition(handle);
        var declaring = type.GetDeclaringType();
        string name = reader.GetString(type.Name);
        return declaring.IsNil ? Qualify(reader.GetString(type.Namespace), name) : $"{GetTypeFromDefinition(reader, declaring, 0)}+{name}";
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? $"{GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, 0)}+{name}"
            : Qualify(reader.GetString(type.Namespace), name);
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, context);

    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPinnedType(string elementType) => elementType;
    public string GetGenericInstantiation(string genericType, System.Collections.Immutable.ImmutableArray<string> typeArguments) =>
        $"{genericType}<{string.Join(",", typeArguments)}>";
    public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? context, int index) => "!" + index;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "method";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    private static string Qualify(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
}
