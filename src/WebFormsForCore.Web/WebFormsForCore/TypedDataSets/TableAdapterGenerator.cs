// webForCore (MIT): the TableAdapters of a Visual Studio DataSet (.xsd), which System.Design generated and which aren't
// open source. Reads the designer's annotation (urn:schemas-microsoft-com:xml-msdatasource) and generates what the
// designer does: a TableAdapter per table (Fill/Get queries, DB-direct Insert/Update/Delete, Update of tables and rows,
// scalar and non-query queries), QueriesTableAdapter for the DataSet's own queries, and TableAdapterManager
// (UpdateAll in one transaction). CodeDOM only, so C# and VB sites both work.
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml;
using CodeDomProvider = WebFormsForCore.CodeDom.Compiler.CodeDomProvider;

namespace System.Data.Design
{
    internal sealed class TableAdapterGenerator
    {
        private const string SourceNamespace = "urn:schemas-microsoft-com:xml-msdatasource";

        private readonly XmlElement dataSource;
        private readonly XmlNamespaceManager names;
        private readonly DataSet dataSet;
        private readonly string dataSetType;
        private readonly string adaptersNamespace;
        private readonly Dictionary<string, (string Table, string Row)> tableClasses;
        private readonly ICodeGenerator identifiers;
        private readonly Hashtable customDBProviders;
        private readonly List<Assembly> references;
        private readonly Dictionary<string, Connection> connections = new Dictionary<string, Connection>();

        internal TableAdapterGenerator(XmlDocument document, DataSet dataSet, string dataSetClass, string ns,
            Dictionary<string, (string, string)> tableClasses, CodeDomProvider codeProvider, Hashtable customDBProviders,
            List<Assembly> references)
        {
            names = new XmlNamespaceManager(document.NameTable);
            names.AddNamespace("ds", SourceNamespace);
            dataSource = (XmlElement)document.SelectSingleNode("//ds:DataSource", names);
            this.dataSet = dataSet;
            dataSetType = string.IsNullOrEmpty(ns) ? dataSetClass : ns + "." + dataSetClass;
            adaptersNamespace = dataSetType + "TableAdapters";
            this.tableClasses = tableClasses;
#pragma warning disable CS0618 // ICodeGenerator: the Reference Source generator's interface
            identifiers = codeProvider.CreateGenerator();
#pragma warning restore CS0618
            this.customDBProviders = customDBProviders;
            this.references = references;
        }

        internal CodeNamespace Generate(bool manager)
        {
            if (dataSource == null)
                return null;
            foreach (XmlElement element in dataSource.SelectNodes("ds:Connections/ds:Connection", names))
                connections[element.GetAttribute("Name")] = new Connection(element, this);

            var ns = new CodeNamespace(adaptersNamespace);
            var adapters = new List<Adapter>();
            foreach (XmlElement element in dataSource.SelectNodes("ds:Tables/ds:TableAdapter", names))
            {
                var adapter = new Adapter(element, this);
                adapters.Add(adapter);
                ns.Types.Add(adapter.Generate());
            }

            var functions = new List<Source>();
            foreach (XmlElement element in dataSource.SelectNodes("ds:Sources/ds:DbSource", names))
                functions.Add(new Source(element, this, null));
            if (functions.Count != 0)
                ns.Types.Add(QueriesTableAdapter(functions));

            if (manager && adapters.Count != 0
                && string.Equals(dataSet.ExtendedProperties["EnableTableAdapterManager"] as string, "true", StringComparison.OrdinalIgnoreCase))
                ns.Types.Add(TableAdapterManager(adapters));

            if (ns.Types.Count == 0)
                return null;
            if (!references.Contains(typeof(System.Configuration.ConfigurationManager).Assembly))
                references.Add(typeof(System.Configuration.ConfigurationManager).Assembly);
            return ns;
        }

        private CodeTypeReference DataSetType => new CodeTypeReference(dataSetType, CodeTypeReferenceOptions.GlobalReference);

        private CodeTypeReference TableType(string table) =>
            new CodeTypeReference(dataSetType + "." + TableClass(table).Table, CodeTypeReferenceOptions.GlobalReference);

        private (string Table, string Row) TableClass(string table) =>
            tableClasses.TryGetValue(table, out var classes) ? classes
                : throw new InvalidOperationException($"The DataSet has no table '{table}' for its TableAdapter.");

        private string Identifier(string name) => System.Data.TypedDataSetGenerator.GenerateIdName(name, identifiers);

        // A provider (<Connection Provider>), with the classes its factory creates; the generated code uses them, as
        // the designer's does (SqlConnection, SqlCommand, ...).
        private sealed class Provider
        {
            internal Type Connection, Command, Adapter, Parameter, Transaction;
            internal PropertyInfo SpecificType;     // SqlParameter.SqlDbType, ...

            internal Provider(string invariantName, TableAdapterGenerator generator)
            {
                DbProviderFactory factory = generator.customDBProviders?[invariantName] as DbProviderFactory;
                if (factory == null && !DbProviderFactories.TryGetFactory(invariantName, out factory))
                    throw new InvalidOperationException($"The DataSet's provider '{invariantName}' isn't registered. Register it in <system.data><DbProviderFactories> or with DbProviderFactories.RegisterFactory.");
                Connection = factory.CreateConnection().GetType();
                Command = factory.CreateCommand().GetType();
                Adapter = factory.CreateDataAdapter()?.GetType()
                    ?? throw new InvalidOperationException($"The DataSet's provider '{invariantName}' has no DataAdapter.");
                Parameter = factory.CreateParameter().GetType();
                Transaction = Connection.GetMethod("BeginTransaction", Type.EmptyTypes)?.ReturnType ?? typeof(DbTransaction);
                SpecificType = Parameter.GetProperties().FirstOrDefault(property =>
                    property.GetCustomAttribute<DbProviderSpecificTypePropertyAttribute>()?.IsProviderSpecificTypeProperty == true);
                foreach (Type type in new[] { Connection, Command, Adapter, Parameter, Transaction })
                    if (!generator.references.Contains(type.Assembly))
                        generator.references.Add(type.Assembly);
            }
        }

        private sealed class Connection
        {
            internal readonly Provider Provider;
            internal readonly CodeExpression ConnectionString;
            internal readonly MemberAttributes Modifier;

            internal Connection(XmlElement element, TableAdapterGenerator generator)
            {
                Provider = new Provider(element.GetAttribute("Provider"), generator);
                Modifier = Access(element.GetAttribute("Modifier"), MemberAttributes.Assembly);
                ConnectionString = ConnectionStringExpression(element);
            }

            // Web.config's <connectionStrings>, as the designer writes it for a web site.
            private static CodeExpression ConnectionStringExpression(XmlElement element)
            {
                string name = null;
                string reference = element.GetAttribute("PropertyReference");
                if (element.GetAttribute("IsAppSettingsProperty") == "true" && element.GetAttribute("AppSettingsPropertyName").Length != 0)
                    name = element.GetAttribute("AppSettingsPropertyName");
                else if (element.GetAttribute("ConnectionStringObject").Length != 0)
                    return Prim(element.GetAttribute("ConnectionStringObject"));
                else if (reference.Contains(".ConnectionStrings."))
                {
                    int start = reference.IndexOf(".ConnectionStrings.", StringComparison.Ordinal) + ".ConnectionStrings.".Length;
                    int end = reference.LastIndexOf(".ConnectionString", StringComparison.Ordinal);
                    name = end > start ? reference.Substring(start, end - start) : reference.Substring(start);
                }
                else if (reference.StartsWith("ApplicationSettings.", StringComparison.Ordinal))
                    // Settings.Default.X is stored in <connectionStrings> as "<namespace>.Properties.Settings.X".
                    name = reference.Substring("ApplicationSettings.".Length).Replace(".GlobalReference.Default.", ".").Replace(".Default.", ".");
                else
                    name = element.GetAttribute("Name");
                return P(Index(P(new CodeTypeReferenceExpression("System.Configuration.ConfigurationManager"), "ConnectionStrings"),
                    Prim(name)), "ConnectionString");
            }
        }

        private sealed class Parameter
        {
            internal string Name;                   // @CategoryID
            internal string Argument;               // CategoryID: the method's parameter
            internal DbType DbType;
            internal string ProviderType;
            internal ParameterDirection Direction;
            internal bool AllowDbNull, NullMapping;
            internal int Size;
            internal byte Precision, Scale;
            internal string SourceColumn;
            internal DataRowVersion SourceVersion;
            internal Type ClrType;

            internal bool IsInput => Direction == ParameterDirection.Input || Direction == ParameterDirection.InputOutput;
            internal bool IsOutput => Direction == ParameterDirection.Output || Direction == ParameterDirection.InputOutput;
            internal bool IsMethodParameter => Direction != ParameterDirection.ReturnValue && !NullMapping;
            internal bool IsNullable => ClrType.IsValueType && AllowDbNull;

            internal CodeTypeReference ArgumentType =>
                IsNullable ? new CodeTypeReference(typeof(Nullable<>).MakeGenericType(ClrType)) : new CodeTypeReference(ClrType);

            internal Parameter(XmlElement element)
            {
                Name = element.GetAttribute("ParameterName");
                DbType = Enum.TryParse(element.GetAttribute("DbType"), out DbType dbType) ? dbType : DbType.Object;
                ProviderType = element.GetAttribute("ProviderType");
                Direction = Enum.TryParse(element.GetAttribute("Direction"), out ParameterDirection direction) ? direction : ParameterDirection.Input;
                AllowDbNull = element.GetAttribute("AllowDbNull") == "true";
                NullMapping = element.GetAttribute("SourceColumnNullMapping") == "true";
                Size = int.TryParse(element.GetAttribute("Size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) ? size : 0;
                Precision = byte.TryParse(element.GetAttribute("Precision"), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte precision) ? precision : (byte)0;
                Scale = byte.TryParse(element.GetAttribute("Scale"), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte scale) ? scale : (byte)0;
                SourceColumn = element.GetAttribute("SourceColumn");
                SourceVersion = Enum.TryParse(element.GetAttribute("SourceVersion"), out DataRowVersion version) ? version : DataRowVersion.Current;
                ClrType = ClrTypeOf(DbType);
            }

            private static Type ClrTypeOf(DbType type) => type switch
            {
                DbType.AnsiString or DbType.AnsiStringFixedLength or DbType.String or DbType.StringFixedLength or DbType.Xml => typeof(string),
                DbType.Binary => typeof(byte[]),
                DbType.Boolean => typeof(bool),
                DbType.Byte => typeof(byte),
                DbType.SByte => typeof(sbyte),
                DbType.Currency or DbType.Decimal or DbType.VarNumeric => typeof(decimal),
                DbType.Date or DbType.DateTime or DbType.DateTime2 => typeof(DateTime),
                DbType.DateTimeOffset => typeof(DateTimeOffset),
                DbType.Time => typeof(TimeSpan),
                DbType.Double => typeof(double),
                DbType.Single => typeof(float),
                DbType.Guid => typeof(Guid),
                DbType.Int16 => typeof(short),
                DbType.Int32 => typeof(int),
                DbType.Int64 => typeof(long),
                DbType.UInt16 => typeof(ushort),
                DbType.UInt32 => typeof(uint),
                DbType.UInt64 => typeof(ulong),
                _ => typeof(object),
            };
        }

        private sealed class Command
        {
            internal string Text;
            internal CommandType Type;
            internal List<Parameter> Parameters = new List<Parameter>();

            internal Command(XmlElement element, TableAdapterGenerator generator)
            {
                XmlElement command = (XmlElement)element.SelectSingleNode("ds:DbCommand", generator.names);
                Text = command.SelectSingleNode("ds:CommandText", generator.names)?.InnerText ?? "";
                Type = Enum.TryParse(command.GetAttribute("CommandType"), out CommandType type) ? type : CommandType.Text;
                foreach (XmlElement parameter in command.SelectNodes("ds:Parameters/ds:Parameter", generator.names))
                    Parameters.Add(new Parameter(parameter));
                // Method parameter names: the parameter's name without its prefix (@, :, ?), unique.
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < Parameters.Count; i++)
                {
                    Parameter parameter = Parameters[i];
                    string name = parameter.Name.TrimStart('@', ':', '?');
                    if (name.Length == 0)
                        name = parameter.SourceColumn.Length != 0 ? parameter.SourceColumn : "Param" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    name = generator.Identifier(name);
                    string unique = name;
                    for (int n = 1; parameter.IsMethodParameter && !used.Add(unique); n++)
                        unique = name + n.ToString(CultureInfo.InvariantCulture);
                    parameter.Argument = unique;
                }
            }
        }

        // A query: <MainSource>'s or <Sources>' <DbSource>.
        private sealed class Source
        {
            internal readonly XmlElement Element;
            internal readonly Connection Connection;
            internal readonly string QueryType;
            internal readonly Command Select, Insert, Update, Delete;

            internal Source(XmlElement element, TableAdapterGenerator generator, Connection fallback)
            {
                Element = element;
                string reference = element.GetAttribute("ConnectionRef");
                Connection = reference.Length != 0 && generator.connections.TryGetValue(reference, out Connection connection) ? connection
                    : fallback ?? generator.connections.Values.FirstOrDefault()
                    ?? throw new InvalidOperationException("The DataSet has no connection for its queries.");
                QueryType = element.GetAttribute("QueryType") is { Length: > 0 } queryType ? queryType : "Rowset";
                Select = CommandOf("ds:SelectCommand", generator);
                Insert = CommandOf("ds:InsertCommand", generator);
                Update = CommandOf("ds:UpdateCommand", generator);
                Delete = CommandOf("ds:DeleteCommand", generator);
            }

            private Command CommandOf(string path, TableAdapterGenerator generator) =>
                Element.SelectSingleNode(path, generator.names) is XmlElement element ? new Command(element, generator) : null;

            // A scalar or non-query query has one command, whichever kind it is.
            internal Command Single => Select ?? Insert ?? Update ?? Delete;

            internal string Attribute(string name, string fallback = null) =>
                Element.GetAttribute(name) is { Length: > 0 } value ? value : fallback;

            internal string MethodName => Attribute("GeneratorSourceName") ?? Attribute("Name") ?? Attribute("UserSourceName");

            internal Type ScalarType
            {
                get
                {
                    string type = Attribute("ScalarCallRetval", "System.Object");
                    int comma = type.IndexOf(',');
                    return Type.GetType(comma < 0 ? type : type.Substring(0, comma)) ?? typeof(object);
                }
            }
        }

        // ---------------------------------------------------------------------------------------- TableAdapter

        private sealed class Adapter
        {
            private readonly TableAdapterGenerator generator;
            internal readonly string ClassName, TableName;
            private readonly string baseClass;
            private readonly TypeAttributes access;
            private readonly Source main;
            private readonly List<Source> sources = new List<Source>();
            private readonly List<(string Source, string DataSet)> mappings = new List<(string, string)>();
            internal Provider Provider => main.Connection.Provider;

            internal Adapter(XmlElement element, TableAdapterGenerator generator)
            {
                this.generator = generator;
                TableName = element.GetAttribute("Name");
                ClassName = generator.Identifier(element.GetAttribute("GeneratorDataComponentClassName") is { Length: > 0 } name ? name
                    : element.GetAttribute("DataAccessorName") is { Length: > 0 } accessor ? accessor : TableName + "TableAdapter");
                baseClass = element.GetAttribute("BaseClass") is { Length: > 0 } baseType ? baseType : "System.ComponentModel.Component";
                access = element.GetAttribute("DataAccessorModifier").Contains("NotPublic") ? TypeAttributes.NotPublic : TypeAttributes.Public;
                main = new Source((XmlElement)element.SelectSingleNode("ds:MainSource/ds:DbSource", generator.names), generator, null);
                foreach (XmlElement source in element.SelectNodes("ds:Sources/ds:DbSource", generator.names))
                    sources.Add(new Source(source, generator, main.Connection));
                foreach (XmlElement mapping in element.SelectNodes("ds:Mappings/ds:Mapping", generator.names))
                    mappings.Add((mapping.GetAttribute("SourceColumn"), mapping.GetAttribute("DataSetColumn")));
            }

            private CodeTypeReference AdapterType => new CodeTypeReference(Provider.Adapter);
            private CodeTypeReference CommandType => new CodeTypeReference(Provider.Command);
            private CodeTypeReference ConnectionType => new CodeTypeReference(Provider.Connection);
            private CodeTypeReference TransactionType => new CodeTypeReference(Provider.Transaction);
            private CodeTypeReference TableType => generator.TableType(TableName);
            private static CodeExpression AdapterProperty => P("Adapter");
            private static CodeExpression Commands => P("CommandCollection");

            // The commands in CommandCollection: the main query first, then each of <Sources>.
            private IEnumerable<(Source Source, Command Command)> CollectionCommands =>
                new[] { (main, main.Select) }.Concat(sources.Select(source => (source, source.Single)));

            internal CodeTypeDeclaration Generate()
            {
                var type = Class(ClassName, access);
                type.BaseTypes.Add(new CodeTypeReference(baseClass, CodeTypeReferenceOptions.GlobalReference));
                type.CustomAttributes.Add(Attribute(typeof(DesignerCategoryAttribute), Prim("code")));
                type.CustomAttributes.Add(Attribute(typeof(ToolboxItemAttribute), Prim(true)));
                type.CustomAttributes.Add(Attribute(typeof(DataObjectAttribute), Prim(true)));

                type.Members.Add(Field(AdapterType, "_adapter"));
                type.Members.Add(Field(ConnectionType, "_connection"));
                type.Members.Add(Field(TransactionType, "_transaction"));
                type.Members.Add(Field(new CodeTypeReference(CommandType, 1), "_commandCollection"));
                type.Members.Add(Field(new CodeTypeReference(typeof(bool)), "_clearBeforeFill"));

                var constructor = new CodeConstructor { Attributes = MemberAttributes.Public };
                constructor.Statements.Add(Assign(P("ClearBeforeFill"), Prim(true)));
                type.Members.Add(constructor);

                type.Members.Add(LazyProperty(AdapterType, "Adapter", "_adapter", "InitAdapter", MemberAttributes.FamilyOrAssembly | MemberAttributes.Final));
                type.Members.Add(ConnectionProperty());
                type.Members.Add(TransactionProperty());
                type.Members.Add(LazyProperty(new CodeTypeReference(CommandType, 1), "CommandCollection", "_commandCollection",
                    "InitCommandCollection", MemberAttributes.Family | MemberAttributes.Final));
                var clear = Property(new CodeTypeReference(typeof(bool)), "ClearBeforeFill", MemberAttributes.Public | MemberAttributes.Final);
                clear.GetStatements.Add(Return(F("_clearBeforeFill")));
                clear.SetStatements.Add(Assign(F("_clearBeforeFill"), new CodePropertySetValueReferenceExpression()));
                type.Members.Add(clear);

                type.Members.Add(InitAdapter());
                type.Members.Add(InitConnection());
                type.Members.Add(InitCommandCollection());

                int index = 0;
                foreach ((Source source, Command command) in CollectionCommands)
                {
                    bool isMain = index == 0;
                    switch (source.QueryType)
                    {
                        case "Rowset":
                            string methods = source.Attribute("GenerateMethods", "Both");
                            if (methods != "Get")
                                type.Members.Add(Fill(source, command, index, isMain));
                            if (methods != "Fill")
                                type.Members.Add(GetData(source, command, index, isMain));
                            break;
                        case "Scalar":
                        case "NoData":
                            type.Members.Add(generator.Execute(source, command, Cast(CommandType, ArrayIndex(Commands, Prim(index))), CommandType));
                            break;
                    }
                    index++;
                }

                if (main.Insert != null || main.Update != null || main.Delete != null)
                {
                    type.Members.Add(UpdateBatch(TableType, "dataTable", null));
                    type.Members.Add(UpdateBatch(generator.DataSetType, "dataSet", TableName));
                    type.Members.Add(UpdateBatch(new CodeTypeReference(typeof(DataRow)), "dataRow", null));
                    type.Members.Add(UpdateBatch(new CodeTypeReference(new CodeTypeReference(typeof(DataRow)), 1), "dataRows", null));
                }
                if (main.Attribute("GenerateShortCommands") == "true")
                    foreach (CodeTypeMember member in DbDirect())
                        type.Members.Add(member);
                return type;
            }

            private CodeMemberProperty ConnectionProperty()
            {
                var property = LazyProperty(ConnectionType, "Connection", "_connection", "InitConnection", main.Connection.Modifier | MemberAttributes.Final);
                CodeExpression value = new CodePropertySetValueReferenceExpression();
                property.SetStatements.Add(Assign(F("_connection"), value));
                foreach (string command in new[] { "InsertCommand", "DeleteCommand", "UpdateCommand" })
                    property.SetStatements.Add(If(Op(P(AdapterProperty, command), CodeBinaryOperatorType.IdentityInequality, Null),
                        Assign(P(P(AdapterProperty, command), "Connection"), value)));
                property.SetStatements.Add(ForEachCommand(i => If(Op(ArrayIndex(Commands, i), CodeBinaryOperatorType.IdentityInequality, Null),
                    Assign(P(Cast(CommandType, ArrayIndex(Commands, i)), "Connection"), value))));
                return property;
            }

            private CodeMemberProperty TransactionProperty()
            {
                var property = Property(TransactionType, "Transaction", MemberAttributes.Assembly | MemberAttributes.Final);
                property.GetStatements.Add(Return(F("_transaction")));
                property.SetStatements.Add(Assign(F("_transaction"), new CodePropertySetValueReferenceExpression()));
                property.SetStatements.Add(ForEachCommand(i => Assign(P(ArrayIndex(Commands, i), "Transaction"), F("_transaction"))));
                foreach (string command in new[] { "DeleteCommand", "InsertCommand", "UpdateCommand" })
                    property.SetStatements.Add(If(Op(Op(AdapterProperty, CodeBinaryOperatorType.IdentityInequality, Null),
                            CodeBinaryOperatorType.BooleanAnd, Op(P(AdapterProperty, command), CodeBinaryOperatorType.IdentityInequality, Null)),
                        Assign(P(P(AdapterProperty, command), "Transaction"), F("_transaction"))));
                return property;
            }

            // for (int i = 0; i < CommandCollection.Length; i++) <body>
            private static CodeStatement ForEachCommand(Func<CodeExpression, CodeStatement> body) =>
                new CodeIterationStatement(
                    new CodeVariableDeclarationStatement(typeof(int), "i", Prim(0)),
                    Op(V("i"), CodeBinaryOperatorType.LessThan, P(Commands, "Length")),
                    Assign(V("i"), Op(V("i"), CodeBinaryOperatorType.Add, Prim(1))),
                    body(V("i")));

            private CodeMemberMethod InitAdapter()
            {
                var method = Method(MemberAttributes.Private | MemberAttributes.Final, null, "InitAdapter");
                method.Statements.Add(Assign(F("_adapter"), New(AdapterType)));
                method.Statements.Add(Var(new CodeTypeReference(typeof(System.Data.Common.DataTableMapping)), "tableMapping",
                    New(new CodeTypeReference(typeof(System.Data.Common.DataTableMapping)))));
                method.Statements.Add(Assign(P(V("tableMapping"), "SourceTable"), Prim("Table")));
                method.Statements.Add(Assign(P(V("tableMapping"), "DataSetTable"), Prim(TableName)));
                foreach ((string source, string column) in mappings)
                    method.Statements.Add(Stmt(Call(P(V("tableMapping"), "ColumnMappings"), "Add", Prim(source), Prim(column))));
                method.Statements.Add(Stmt(Call(P(F("_adapter"), "TableMappings"), "Add", V("tableMapping"))));
                var variables = new Dictionary<Type, string>();
                foreach ((string name, Command command) in new[] { ("DeleteCommand", main.Delete), ("InsertCommand", main.Insert), ("UpdateCommand", main.Update) })
                    if (command != null)
                        generator.InitCommand(method.Statements, P(F("_adapter"), name), null, command, P("Connection"), Provider, variables);
                return method;
            }

            private CodeMemberMethod InitConnection()
            {
                var method = Method(MemberAttributes.Private | MemberAttributes.Final, null, "InitConnection");
                method.Statements.Add(Assign(F("_connection"), New(ConnectionType)));
                method.Statements.Add(Assign(P(F("_connection"), "ConnectionString"), main.Connection.ConnectionString));
                return method;
            }

            private CodeMemberMethod InitCommandCollection()
            {
                var method = Method(MemberAttributes.Private | MemberAttributes.Final, null, "InitCommandCollection");
                var commands = CollectionCommands.ToList();
                method.Statements.Add(Assign(F("_commandCollection"), new CodeArrayCreateExpression(CommandType, commands.Count)));
                var variables = new Dictionary<Type, string>();
                for (int i = 0; i < commands.Count; i++)
                    generator.InitCommand(method.Statements, ArrayIndex(F("_commandCollection"), Prim(i)), null, commands[i].Command,
                        P("Connection"), Provider, variables);
                return method;
            }

            private CodeMemberMethod Fill(Source source, Command command, int index, bool isMain)
            {
                var method = Method(Access(source.Attribute("FillMethodModifier"), MemberAttributes.Public), new CodeTypeReference(typeof(int)),
                    generator.Identifier(source.Attribute("FillMethodName", "Fill")));
                method.CustomAttributes.Add(DataObjectMethod(DataObjectMethodType.Fill, isMain));
                method.Parameters.Add(new CodeParameterDeclarationExpression(TableType, "dataTable"));
                AddArguments(method, command);
                method.Statements.Add(Assign(P(AdapterProperty, "SelectCommand"), ArrayIndex(Commands, Prim(index))));
                CodeExpression parameters = P(P(AdapterProperty, "SelectCommand"), "Parameters");
                SetParameters(method.Statements, command, parameters);
                method.Statements.Add(If(Op(P("ClearBeforeFill"), CodeBinaryOperatorType.ValueEquality, Prim(true)),
                    Stmt(Call(A("dataTable"), "Clear"))));
                method.Statements.Add(Var(new CodeTypeReference(typeof(int)), "returnValue", Call(AdapterProperty, "Fill", A("dataTable"))));
                GetOutputs(method.Statements, command, parameters);
                method.Statements.Add(Return(V("returnValue")));
                return method;
            }

            private CodeMemberMethod GetData(Source source, Command command, int index, bool isMain)
            {
                var method = Method(Access(source.Attribute("GetMethodModifier"), MemberAttributes.Public), TableType,
                    generator.Identifier(source.Attribute("GeneratorGetMethodName") ?? source.Attribute("GetMethodName", "GetData")));
                method.CustomAttributes.Add(DataObjectMethod(DataObjectMethodType.Select, isMain));
                AddArguments(method, command);
                method.Statements.Add(Assign(P(AdapterProperty, "SelectCommand"), ArrayIndex(Commands, Prim(index))));
                CodeExpression parameters = P(P(AdapterProperty, "SelectCommand"), "Parameters");
                SetParameters(method.Statements, command, parameters);
                method.Statements.Add(Var(TableType, "dataTable", New(TableType)));
                method.Statements.Add(Stmt(Call(AdapterProperty, "Fill", V("dataTable"))));
                GetOutputs(method.Statements, command, parameters);
                method.Statements.Add(Return(V("dataTable")));
                return method;
            }

            // Update(table), Update(dataSet), Update(row), Update(rows): the adapter's InsertCommand, UpdateCommand,
            // DeleteCommand for each changed row.
            private CodeMemberMethod UpdateBatch(CodeTypeReference type, string name, string table)
            {
                var method = Method(MemberAttributes.Public, new CodeTypeReference(typeof(int)), "Update");
                method.Parameters.Add(new CodeParameterDeclarationExpression(type, name));
                CodeExpression[] arguments =
                    table != null ? new[] { A(name), Prim(table) }
                    : name == "dataRow" ? new CodeExpression[] { new CodeArrayCreateExpression(typeof(DataRow), new[] { A(name) }) }
                    : new[] { A(name) };
                method.Statements.Add(Return(Call(AdapterProperty, "Update", arguments)));
                return method;
            }

            // Insert(...), Update(...), Delete(...) with the column values as arguments (GenerateDBDirectMethods), and an
            // Update without the arguments that only refresh a read-only key (... WHERE ProductID = @ProductID).
            private IEnumerable<CodeTypeMember> DbDirect()
            {
                if (main.Delete != null)
                    yield return DbDirectMethod("Delete", main.Delete, DataObjectMethodType.Delete);
                if (main.Insert != null)
                    yield return DbDirectMethod("Insert", main.Insert, DataObjectMethodType.Insert);
                if (main.Update == null)
                    yield break;
                yield return DbDirectMethod("Update", main.Update, DataObjectMethodType.Update);

                DataTable table = generator.dataSet.Tables[TableName];
                var arguments = new List<Parameter>();
                var replaced = new Dictionary<Parameter, Parameter>();
                List<Parameter> parameters = main.Update.Parameters.Where(p => p.IsMethodParameter).ToList();
                foreach (Parameter parameter in parameters)
                {
                    DataColumn column = table?.Columns[parameter.SourceColumn];
                    Parameter original = parameter.SourceVersion == DataRowVersion.Current && column != null
                        && (column.ReadOnly || column.AutoIncrement)
                        ? parameters.FirstOrDefault(p => p.SourceVersion == DataRowVersion.Original && p.SourceColumn == parameter.SourceColumn)
                        : null;
                    if (original != null)
                        replaced[parameter] = original;
                    else
                        arguments.Add(parameter);
                }
                if (replaced.Count == 0)
                    yield break;
                var method = Method(MemberAttributes.Public, new CodeTypeReference(typeof(int)), "Update");
                method.CustomAttributes.Add(DataObjectMethod(DataObjectMethodType.Update, true));
                foreach (Parameter parameter in arguments)
                    method.Parameters.Add(Argument(parameter));
                method.Statements.Add(Return(Call(This, "Update", parameters.Select(p =>
                    (CodeExpression)A(replaced.TryGetValue(p, out Parameter original) ? original.Argument : p.Argument)).ToArray())));
                yield return method;
            }

            private CodeMemberMethod DbDirectMethod(string name, Command command, DataObjectMethodType kind)
            {
                CodeMemberMethod method = generator.Execute(main, command, P(AdapterProperty, name + "Command"), CommandType, name);
                method.CustomAttributes.Add(DataObjectMethod(kind, true));
                return method;
            }
        }

        // ----------------------------------------------------------------------------------- shared generation

        // <target> = new Command { Connection, CommandText, CommandType, Parameters }.
        private void InitCommand(CodeStatementCollection statements, CodeExpression target, CodeExpression typedTarget, Command command,
            CodeExpression connection, Provider provider, Dictionary<Type, string> parameterVariables)
        {
            var commandType = new CodeTypeReference(provider.Command);
            typedTarget ??= target;
            statements.Add(Assign(target, New(commandType)));
            statements.Add(Assign(P(typedTarget, "Connection"), connection));
            statements.Add(Assign(P(typedTarget, "CommandText"), Prim(command.Text)));
            statements.Add(Assign(P(typedTarget, "CommandType"), EnumValue(typeof(CommandType), command.Type.ToString())));
            if (command.Parameters.Count == 0)
                return;
            // One variable per parameter class (a DataSet's queries may use several providers).
            var parameterType = new CodeTypeReference(provider.Parameter);
            if (!parameterVariables.TryGetValue(provider.Parameter, out string variable))
            {
                variable = "parameter" + (parameterVariables.Count == 0 ? "" : parameterVariables.Count.ToString(CultureInfo.InvariantCulture));
                parameterVariables[provider.Parameter] = variable;
                statements.Add(new CodeVariableDeclarationStatement(parameterType, variable));
            }
            foreach (Parameter parameter in command.Parameters)
            {
                CodeExpression p = V(variable);
                statements.Add(Assign(p, New(parameterType)));
                statements.Add(Assign(P(p, "ParameterName"), Prim(parameter.Name)));
                if (provider.SpecificType != null && TryParseEnum(provider.SpecificType.PropertyType, parameter.ProviderType, out string specific))
                    statements.Add(Assign(P(p, provider.SpecificType.Name), EnumValue(provider.SpecificType.PropertyType, specific)));
                else
                    statements.Add(Assign(P(p, "DbType"), EnumValue(typeof(DbType), parameter.DbType.ToString())));
                if (parameter.Size != 0)
                    statements.Add(Assign(P(p, "Size"), Prim(parameter.Size)));
                if (parameter.Direction != ParameterDirection.Input)
                    statements.Add(Assign(P(p, "Direction"), EnumValue(typeof(ParameterDirection), parameter.Direction.ToString())));
                if (parameter.Precision != 0)
                    statements.Add(Assign(P(p, "Precision"), Prim(parameter.Precision)));
                if (parameter.Scale != 0)
                    statements.Add(Assign(P(p, "Scale"), Prim(parameter.Scale)));
                if (parameter.AllowDbNull)
                    statements.Add(Assign(P(p, "IsNullable"), Prim(true)));
                if (!string.IsNullOrEmpty(parameter.SourceColumn))
                    statements.Add(Assign(P(p, "SourceColumn"), Prim(parameter.SourceColumn)));
                if (parameter.SourceVersion != DataRowVersion.Current)
                    statements.Add(Assign(P(p, "SourceVersion"), EnumValue(typeof(DataRowVersion), parameter.SourceVersion.ToString())));
                if (parameter.NullMapping)
                    statements.Add(Assign(P(p, "SourceColumnNullMapping"), Prim(true)));
                statements.Add(Stmt(Call(P(typedTarget, "Parameters"), "Add", p)));
            }
        }

        private static bool TryParseEnum(Type type, string value, out string name)
        {
            name = Enum.GetNames(type).FirstOrDefault(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase));
            return name != null;
        }

        // A method that runs one command: ExecuteScalar for a scalar query, ExecuteNonQuery otherwise.
        private CodeMemberMethod Execute(Source source, Command command, CodeExpression commandExpression, CodeTypeReference commandType,
            string name = null)
        {
            bool scalar = name == null && source.QueryType == "Scalar";
            Type scalarType = source.ScalarType;
            CodeTypeReference returnType = !scalar ? new CodeTypeReference(typeof(int))
                : scalarType.IsValueType ? new CodeTypeReference(typeof(Nullable<>).MakeGenericType(scalarType))
                : new CodeTypeReference(scalarType);
            var method = Method(Access(source.Attribute("Modifier"), MemberAttributes.Public), returnType,
                name ?? Identifier(source.MethodName));
            AddArguments(method, command);

            method.Statements.Add(Var(commandType, "command", commandExpression));
            CodeExpression parameters = P(V("command"), "Parameters");
            SetParameters(method.Statements, command, parameters);
            CodeExpression state = P(P(V("command"), "Connection"), "State");
            CodeExpression open = EnumValue(typeof(ConnectionState), "Open");
            method.Statements.Add(Var(new CodeTypeReference(typeof(ConnectionState)), "previousConnectionState", state));
            method.Statements.Add(If(Not(Op(Op(state, CodeBinaryOperatorType.BitwiseAnd, open), CodeBinaryOperatorType.ValueEquality, open)),
                Stmt(Call(P(V("command"), "Connection"), "Open"))));
            method.Statements.Add(new CodeVariableDeclarationStatement(scalar ? typeof(object) : typeof(int), "returnValue"));
            method.Statements.Add(new CodeTryCatchFinallyStatement(
                new[] { Assign(V("returnValue"), Call(V("command"), scalar ? "ExecuteScalar" : "ExecuteNonQuery")) },
                Array.Empty<CodeCatchClause>(),
                new[] { If(Op(V("previousConnectionState"), CodeBinaryOperatorType.ValueEquality, EnumValue(typeof(ConnectionState), "Closed")),
                    Stmt(Call(P(V("command"), "Connection"), "Close"))) }));
            GetOutputs(method.Statements, command, parameters);
            if (!scalar)
                method.Statements.Add(Return(V("returnValue")));
            else
            {
                CodeExpression isNull = IsNullOrDBNull(V("returnValue"));
                CodeExpression value = scalarType == typeof(object) ? V("returnValue")
                    : scalarType.IsValueType ? New(returnType, Cast(new CodeTypeReference(scalarType), V("returnValue")))
                    : Cast(new CodeTypeReference(scalarType), V("returnValue"));
                method.Statements.Add(If(isNull,
                    new[] { Return(scalarType.IsValueType ? New(returnType) : Null) },
                    new[] { Return(value) }));
            }
            return method;
        }

        private static void AddArguments(CodeMemberMethod method, Command command)
        {
            foreach (Parameter parameter in command.Parameters)
                if (parameter.IsMethodParameter)
                    method.Parameters.Add(Argument(parameter));
        }

        private static CodeParameterDeclarationExpression Argument(Parameter parameter) =>
            new CodeParameterDeclarationExpression(parameter.ArgumentType, parameter.Argument)
            {
                Direction = parameter.IsOutput ? FieldDirection.Ref : FieldDirection.In,
            };

        // The arguments' values into the command's parameters: DBNull for null, and IsNull_X (SourceColumnNullMapping)
        // 1 when its column's argument is null.
        private static void SetParameters(CodeStatementCollection statements, Command command, CodeExpression parameters)
        {
            for (int i = 0; i < command.Parameters.Count; i++)
            {
                Parameter parameter = command.Parameters[i];
                CodeExpression value = P(Index(parameters, Prim(i)), "Value");
                if (parameter.NullMapping)
                {
                    Parameter partner = command.Parameters.FirstOrDefault(p => p.IsMethodParameter
                        && p.SourceColumn == parameter.SourceColumn && p.SourceVersion == parameter.SourceVersion);
                    CodeExpression partnerIsNull = partner == null ? null : IsNull(partner);
                    statements.Add(partnerIsNull == null
                        ? Assign(value, Cast(new CodeTypeReference(typeof(object)), Prim(0)))
                        : If(partnerIsNull,
                            new[] { Assign(value, Cast(new CodeTypeReference(typeof(object)), Prim(1))) },
                            new[] { Assign(value, Cast(new CodeTypeReference(typeof(object)), Prim(0))) }));
                    continue;
                }
                if (!parameter.IsMethodParameter || parameter.Direction == ParameterDirection.Output)
                    continue;
                CodeExpression argument = A(parameter.Argument);
                var clrType = new CodeTypeReference(parameter.ClrType);
                if (parameter.IsNullable)
                    statements.Add(If(P(argument, "HasValue"),
                        new[] { Assign(value, Cast(clrType, P(argument, "Value"))) },
                        new[] { Assign(value, DBNullValue) }));
                else if (!parameter.ClrType.IsValueType)
                    statements.Add(If(Op(argument, CodeBinaryOperatorType.IdentityEquality, Null),
                        new[] { parameter.AllowDbNull ? Assign(value, DBNullValue)
                            : new CodeThrowExceptionStatement(New(new CodeTypeReference(typeof(ArgumentNullException)), Prim(parameter.Argument))) },
                        new[] { Assign(value, Cast(clrType, argument)) }));
                else
                    statements.Add(Assign(value, Cast(clrType, argument)));
            }
        }

        // Output and InputOutput parameters' values back into their ref arguments.
        private static void GetOutputs(CodeStatementCollection statements, Command command, CodeExpression parameters)
        {
            for (int i = 0; i < command.Parameters.Count; i++)
            {
                Parameter parameter = command.Parameters[i];
                if (!parameter.IsMethodParameter || !parameter.IsOutput)
                    continue;
                CodeExpression value = P(Index(parameters, Prim(i)), "Value");
                CodeExpression argument = A(parameter.Argument);
                var clrType = new CodeTypeReference(parameter.ClrType);
                if (parameter.IsNullable)
                    statements.Add(If(IsNullOrDBNull(value),
                        new[] { Assign(argument, New(parameter.ArgumentType)) },
                        new[] { Assign(argument, New(parameter.ArgumentType, Cast(clrType, value))) }));
                else if (!parameter.ClrType.IsValueType)
                    statements.Add(If(IsNullOrDBNull(value),
                        new[] { Assign(argument, Null) },
                        new[] { Assign(argument, Cast(clrType, value)) }));
                else
                    statements.Add(Assign(argument, Cast(clrType, value)));
            }
        }

        // The argument is null (null for a value type that can't be: no expression).
        private static CodeExpression IsNull(Parameter parameter) =>
            parameter.IsNullable ? Not(P(A(parameter.Argument), "HasValue"))
            : !parameter.ClrType.IsValueType ? Op(A(parameter.Argument), CodeBinaryOperatorType.IdentityEquality, Null)
            : null;

        private static CodeExpression IsNullOrDBNull(CodeExpression value) =>
            Op(Op(value, CodeBinaryOperatorType.IdentityEquality, Null), CodeBinaryOperatorType.BooleanOr,
                Op(Call(value, "GetType"), CodeBinaryOperatorType.IdentityEquality, new CodeTypeOfExpression(typeof(DBNull))));

        private static CodeExpression DBNullValue => new CodeFieldReferenceExpression(new CodeTypeReferenceExpression(typeof(DBNull)), "Value");

        // ----------------------------------------------------------------------------------- QueriesTableAdapter

        // The DataSet's own queries (<DataSource><Sources>), each with its own connection.
        private CodeTypeDeclaration QueriesTableAdapter(List<Source> functions)
        {
            string name = Identifier(dataSource.GetAttribute("FunctionsComponentName") is { Length: > 0 } n ? n : "QueriesTableAdapter");
            var type = Class(name, dataSource.GetAttribute("Modifier").Contains("NotPublic") ? TypeAttributes.NotPublic : TypeAttributes.Public);
            type.BaseTypes.Add(new CodeTypeReference(typeof(Component)));
            type.CustomAttributes.Add(Attribute(typeof(DesignerCategoryAttribute), Prim("code")));
            type.CustomAttributes.Add(Attribute(typeof(ToolboxItemAttribute), Prim(true)));
            var commandsType = new CodeTypeReference(new CodeTypeReference(typeof(IDbCommand)), 1);
            type.Members.Add(Field(commandsType, "_commandCollection"));
            type.Members.Add(LazyProperty(commandsType, "CommandCollection", "_commandCollection", "InitCommandCollection",
                MemberAttributes.Family | MemberAttributes.Final));

            var init = Method(MemberAttributes.Private | MemberAttributes.Final, null, "InitCommandCollection");
            init.Statements.Add(Assign(F("_commandCollection"), new CodeArrayCreateExpression(typeof(IDbCommand), functions.Count)));
            var variables = new Dictionary<Type, string>();
            for (int i = 0; i < functions.Count; i++)
            {
                Source source = functions[i];
                Provider provider = source.Connection.Provider;
                var commandType = new CodeTypeReference(provider.Command);
                InitCommand(init.Statements, ArrayIndex(F("_commandCollection"), Prim(i)), Cast(commandType, ArrayIndex(F("_commandCollection"), Prim(i))),
                    source.Single, New(new CodeTypeReference(provider.Connection), source.Connection.ConnectionString), provider, variables);
                type.Members.Add(Execute(source, source.Single, Cast(commandType, ArrayIndex(P("CommandCollection"), Prim(i))), commandType));
            }
            type.Members.Add(init);
            return type;
        }

        // ----------------------------------------------------------------------------------- TableAdapterManager

        // UpdateAll(dataSet): every table's changes in one transaction, parent tables' inserts and updates before their
        // children's, children's deletes before their parents'; the rows' changes are accepted after the commit.
        private CodeTypeDeclaration TableAdapterManager(List<Adapter> adapters)
        {
            var type = Class("TableAdapterManager", TypeAttributes.Public);
            type.BaseTypes.Add(new CodeTypeReference(typeof(Component)));
            type.CustomAttributes.Add(Attribute(typeof(DesignerCategoryAttribute), Prim("code")));
            type.CustomAttributes.Add(Attribute(typeof(ToolboxItemAttribute), Prim(true)));

            var order = new CodeTypeDeclaration("UpdateOrderOption") { IsEnum = true };
            order.Members.Add(new CodeMemberField { Name = "InsertUpdateDelete", InitExpression = Prim(0) });
            order.Members.Add(new CodeMemberField { Name = "UpdateInsertDelete", InitExpression = Prim(1) });
            type.Members.Add(order);
            var orderType = new CodeTypeReference(adaptersNamespace + ".TableAdapterManager.UpdateOrderOption", CodeTypeReferenceOptions.GlobalReference);

            type.Members.Add(Field(orderType, "_updateOrder"));
            type.Members.Add(SimpleProperty(orderType, "UpdateOrder", "_updateOrder"));
            foreach (Adapter adapter in adapters)
            {
                var adapterType = new CodeTypeReference(adaptersNamespace + "." + adapter.ClassName, CodeTypeReferenceOptions.GlobalReference);
                type.Members.Add(Field(adapterType, AdapterField(adapter)));
                type.Members.Add(SimpleProperty(adapterType, adapter.ClassName, AdapterField(adapter)));
            }
            type.Members.Add(Field(new CodeTypeReference(typeof(bool)), "_backupDataSetBeforeUpdate"));
            type.Members.Add(SimpleProperty(new CodeTypeReference(typeof(bool)), "BackupDataSetBeforeUpdate", "_backupDataSetBeforeUpdate"));
            type.Members.Add(Field(new CodeTypeReference(typeof(IDbConnection)), "_connection"));

            // Connection: the one set, or the first TableAdapter's.
            var connection = Property(new CodeTypeReference(typeof(IDbConnection)), "Connection", MemberAttributes.Public | MemberAttributes.Final);
            connection.GetStatements.Add(If(Op(F("_connection"), CodeBinaryOperatorType.IdentityInequality, Null), Return(F("_connection"))));
            foreach (Adapter adapter in adapters)
                connection.GetStatements.Add(If(Op(Op(F(AdapterField(adapter)), CodeBinaryOperatorType.IdentityInequality, Null),
                        CodeBinaryOperatorType.BooleanAnd, Op(P(F(AdapterField(adapter)), "Connection"), CodeBinaryOperatorType.IdentityInequality, Null)),
                    Return(P(F(AdapterField(adapter)), "Connection"))));
            connection.GetStatements.Add(Return(Null));
            connection.SetStatements.Add(Assign(F("_connection"), new CodePropertySetValueReferenceExpression()));
            type.Members.Add(connection);

            var count = Property(new CodeTypeReference(typeof(int)), "TableAdapterInstanceCount", MemberAttributes.Public | MemberAttributes.Final);
            count.GetStatements.Add(Var(new CodeTypeReference(typeof(int)), "count", Prim(0)));
            foreach (Adapter adapter in adapters)
                count.GetStatements.Add(If(Op(F(AdapterField(adapter)), CodeBinaryOperatorType.IdentityInequality, Null),
                    Assign(V("count"), Op(V("count"), CodeBinaryOperatorType.Add, Prim(1)))));
            count.GetStatements.Add(Return(V("count")));
            type.Members.Add(count);

            List<Adapter> parentsFirst = ParentsFirst(adapters);
            type.Members.Add(UpdateRows("UpdateUpdatedRows", parentsFirst, "ModifiedCurrent", "allChangedRows"));
            type.Members.Add(UpdateRows("UpdateInsertedRows", parentsFirst, "Added", "allAddedRows"));
            type.Members.Add(UpdateRows("UpdateDeletedRows", Enumerable.Reverse(parentsFirst).ToList(), "Deleted", "allChangedRows"));
            type.Members.Add(GetRealUpdatedRows());
            type.Members.Add(MatchTableAdapterConnection());
            type.Members.Add(UpdateAll(adapters));
            return type;
        }

        private static string AdapterField(Adapter adapter) =>
            "_" + char.ToLowerInvariant(adapter.ClassName[0]) + adapter.ClassName.Substring(1);

        private List<Adapter> ParentsFirst(List<Adapter> adapters)
        {
            var result = new List<Adapter>();
            var visiting = new HashSet<string>();
            void Visit(Adapter adapter)
            {
                if (result.Contains(adapter) || !visiting.Add(adapter.TableName))
                    return;
                DataTable table = dataSet.Tables[adapter.TableName];
                if (table != null)
                    foreach (DataRelation relation in table.ParentRelations)
                        if (relation.ParentTable != table && adapters.FirstOrDefault(a => a.TableName == relation.ParentTable.TableName) is Adapter parent)
                            Visit(parent);
                result.Add(adapter);
            }
            foreach (Adapter adapter in adapters)
                Visit(adapter);
            return result;
        }

        // Update<state>Rows(dataSet, allChangedRows, allAddedRows): the rows in that state, collected in <collected>.
        private CodeMemberMethod UpdateRows(string name, List<Adapter> adapters, string state, string collected)
        {
            var method = Method(MemberAttributes.Private | MemberAttributes.Final, new CodeTypeReference(typeof(int)), name);
            method.Parameters.Add(new CodeParameterDeclarationExpression(DataSetType, "dataSet"));
            var rowList = new CodeTypeReference(typeof(List<DataRow>));
            method.Parameters.Add(new CodeParameterDeclarationExpression(rowList, "allChangedRows"));
            method.Parameters.Add(new CodeParameterDeclarationExpression(rowList, "allAddedRows"));
            method.Statements.Add(Var(new CodeTypeReference(typeof(int)), "result", Prim(0)));
            var rows = new CodeTypeReference(new CodeTypeReference(typeof(DataRow)), 1);
            method.Statements.Add(new CodeVariableDeclarationStatement(rows, "changedRows"));
            foreach (Adapter adapter in adapters)
            {
                var statements = new List<CodeStatement>
                {
                    Assign(V("changedRows"), Call(P(A("dataSet"), TablePropertyName(adapter.TableName)), "Select", Null, Null,
                        EnumValue(typeof(DataViewRowState), state))),
                };
                // Inserted rows refreshed from the database (... SCOPE_IDENTITY()) look modified.
                if (state == "ModifiedCurrent")
                    statements.Add(Assign(V("changedRows"), Call(This, "GetRealUpdatedRows", V("changedRows"), A("allAddedRows"))));
                statements.Add(If(Op(Op(V("changedRows"), CodeBinaryOperatorType.IdentityInequality, Null), CodeBinaryOperatorType.BooleanAnd,
                        Op(P(V("changedRows"), "Length"), CodeBinaryOperatorType.GreaterThan, Prim(0))), new CodeStatement[]
                    {
                        Assign(V("result"), Op(V("result"), CodeBinaryOperatorType.Add, Call(F(AdapterField(adapter)), "Update", V("changedRows")))),
                        Stmt(Call(A(collected), "AddRange", V("changedRows"))),
                    }));
                method.Statements.Add(If(Op(F(AdapterField(adapter)), CodeBinaryOperatorType.IdentityInequality, Null), statements.ToArray()));
            }
            method.Statements.Add(Return(V("result")));
            return method;
        }

        // The DataSet's property for a table (the Reference Source generator's TablePropertyName).
        private string TablePropertyName(string table)
        {
            DataTable dataTable = dataSet.Tables[table];
            string typedName = dataTable?.ExtendedProperties["typedPlural"] as string;
            if (string.IsNullOrEmpty(typedName))
                typedName = dataTable?.ExtendedProperties["typedName"] is string name && name.Length != 0 ? name + "Table" : Identifier(table);
            return typedName;
        }

        private static CodeMemberMethod GetRealUpdatedRows()
        {
            var rows = new CodeTypeReference(new CodeTypeReference(typeof(DataRow)), 1);
            var rowList = new CodeTypeReference(typeof(List<DataRow>));
            var method = Method(MemberAttributes.Private | MemberAttributes.Final, rows, "GetRealUpdatedRows");
            method.Parameters.Add(new CodeParameterDeclarationExpression(rows, "updatedRows"));
            method.Parameters.Add(new CodeParameterDeclarationExpression(rowList, "allAddedRows"));
            method.Statements.Add(If(Op(Op(A("updatedRows"), CodeBinaryOperatorType.IdentityEquality, Null), CodeBinaryOperatorType.BooleanOr,
                Op(P(A("allAddedRows"), "Count"), CodeBinaryOperatorType.ValueEquality, Prim(0))), Return(A("updatedRows"))));
            method.Statements.Add(Var(rowList, "realUpdatedRows", New(rowList)));
            method.Statements.Add(new CodeIterationStatement(
                new CodeVariableDeclarationStatement(typeof(int), "i", Prim(0)),
                Op(V("i"), CodeBinaryOperatorType.LessThan, P(A("updatedRows"), "Length")),
                Assign(V("i"), Op(V("i"), CodeBinaryOperatorType.Add, Prim(1))),
                If(Not(Call(A("allAddedRows"), "Contains", ArrayIndex(A("updatedRows"), V("i")))),
                    Stmt(Call(V("realUpdatedRows"), "Add", ArrayIndex(A("updatedRows"), V("i")))))));
            method.Statements.Add(Return(Call(V("realUpdatedRows"), "ToArray")));
            return method;
        }

        private static CodeMemberMethod MatchTableAdapterConnection()
        {
            var method = Method(MemberAttributes.Family, new CodeTypeReference(typeof(bool)), "MatchTableAdapterConnection");
            method.Parameters.Add(new CodeParameterDeclarationExpression(typeof(IDbConnection), "inputConnection"));
            method.Statements.Add(If(Op(F("_connection"), CodeBinaryOperatorType.IdentityInequality, Null), Return(Prim(true))));
            method.Statements.Add(If(Op(Op(P("Connection"), CodeBinaryOperatorType.IdentityEquality, Null), CodeBinaryOperatorType.BooleanOr,
                Op(A("inputConnection"), CodeBinaryOperatorType.IdentityEquality, Null)), Return(Prim(true))));
            method.Statements.Add(Return(Call(new CodeTypeReferenceExpression(typeof(string)), "Equals",
                P(P("Connection"), "ConnectionString"), P(A("inputConnection"), "ConnectionString"), EnumValue(typeof(StringComparison), "Ordinal"))));
            return method;
        }

        private CodeMemberMethod UpdateAll(List<Adapter> adapters)
        {
            var method = Method(MemberAttributes.Public, new CodeTypeReference(typeof(int)), "UpdateAll");
            method.Parameters.Add(new CodeParameterDeclarationExpression(DataSetType, "dataSet"));
            var s = method.Statements;
            s.Add(If(Op(A("dataSet"), CodeBinaryOperatorType.IdentityEquality, Null),
                new CodeThrowExceptionStatement(New(new CodeTypeReference(typeof(ArgumentNullException)), Prim("dataSet")))));
            s.Add(If(Op(Call(A("dataSet"), "HasChanges"), CodeBinaryOperatorType.ValueEquality, Prim(false)), Return(Prim(0))));
            foreach (Adapter adapter in adapters)
                s.Add(If(Op(Op(F(AdapterField(adapter)), CodeBinaryOperatorType.IdentityInequality, Null), CodeBinaryOperatorType.BooleanAnd,
                        Op(Call(This, "MatchTableAdapterConnection", P(F(AdapterField(adapter)), "Connection")), CodeBinaryOperatorType.ValueEquality, Prim(false))),
                    new CodeThrowExceptionStatement(New(new CodeTypeReference(typeof(ArgumentException)),
                        Prim("All TableAdapters managed by a TableAdapterManager must use the same connection string.")))));
            s.Add(Var(new CodeTypeReference(typeof(IDbConnection)), "workConnection", P("Connection")));
            s.Add(If(Op(V("workConnection"), CodeBinaryOperatorType.IdentityEquality, Null),
                new CodeThrowExceptionStatement(New(new CodeTypeReference(typeof(ApplicationException)),
                    Prim("TableAdapterManager contains no connection information. Set each TableAdapterManager TableAdapter property to a valid TableAdapter instance.")))));
            s.Add(Var(new CodeTypeReference(typeof(bool)), "workConnOpened", Prim(false)));
            CodeExpression state = P(V("workConnection"), "State");
            s.Add(If(Op(Op(state, CodeBinaryOperatorType.BitwiseAnd, EnumValue(typeof(ConnectionState), "Broken")), CodeBinaryOperatorType.ValueEquality,
                EnumValue(typeof(ConnectionState), "Broken")), Stmt(Call(V("workConnection"), "Close"))));
            s.Add(If(Op(state, CodeBinaryOperatorType.ValueEquality, EnumValue(typeof(ConnectionState), "Closed")), new[]
            {
                Stmt(Call(V("workConnection"), "Open")),
                Assign(V("workConnOpened"), Prim(true)),
            }));
            s.Add(Var(new CodeTypeReference(typeof(IDbTransaction)), "workTransaction", Call(V("workConnection"), "BeginTransaction")));
            var rowList = new CodeTypeReference(typeof(List<DataRow>));
            s.Add(Var(rowList, "allChangedRows", New(rowList)));
            s.Add(Var(rowList, "allAddedRows", New(rowList)));
            var adapterList = new CodeTypeReference(typeof(List<DataAdapter>));
            s.Add(Var(adapterList, "adaptersWithAcceptChangesDuringUpdate", New(adapterList)));
            s.Add(new CodeVariableDeclarationStatement(typeof(DataSet), "backupDataSet", Null));
            s.Add(If(P("BackupDataSetBeforeUpdate"), new[]
            {
                Assign(V("backupDataSet"), New(new CodeTypeReference(typeof(DataSet)))),
                Stmt(Call(V("backupDataSet"), "Merge", A("dataSet"))),
            }));
            foreach (Adapter adapter in adapters)
                s.Add(new CodeVariableDeclarationStatement(new CodeTypeReference(adapter.Provider.Connection), "revert" + adapter.ClassName + "Connection", Null));
            s.Add(Var(new CodeTypeReference(typeof(int)), "result", Prim(0)));

            var body = new CodeStatementCollection();
            foreach (Adapter adapter in adapters)
            {
                CodeExpression field = F(AdapterField(adapter));
                CodeExpression adapterProperty = P(field, "Adapter");
                body.Add(If(Op(field, CodeBinaryOperatorType.IdentityInequality, Null), new[]
                {
                    Assign(V("revert" + adapter.ClassName + "Connection"), P(field, "Connection")),
                    Assign(P(field, "Connection"), Cast(new CodeTypeReference(adapter.Provider.Connection), V("workConnection"))),
                    Assign(P(field, "Transaction"), Cast(new CodeTypeReference(adapter.Provider.Transaction), V("workTransaction"))),
                    If(P(adapterProperty, "AcceptChangesDuringUpdate"), new[]
                    {
                        Assign(P(adapterProperty, "AcceptChangesDuringUpdate"), Prim(false)),
                        Stmt(Call(V("adaptersWithAcceptChangesDuringUpdate"), "Add", adapterProperty)),
                    }),
                }));
            }
            CodeStatement Add(string rows) => Assign(V("result"), Op(V("result"), CodeBinaryOperatorType.Add, Call(This, rows, A("dataSet"), V("allChangedRows"), V("allAddedRows"))));
            body.Add(If(Op(P("UpdateOrder"), CodeBinaryOperatorType.ValueEquality,
                    new CodeFieldReferenceExpression(new CodeTypeReferenceExpression(
                        new CodeTypeReference(adaptersNamespace + ".TableAdapterManager.UpdateOrderOption", CodeTypeReferenceOptions.GlobalReference)), "UpdateInsertDelete")),
                new[] { Add("UpdateUpdatedRows"), Add("UpdateInsertedRows") },
                new[] { Add("UpdateInsertedRows"), Add("UpdateUpdatedRows") }));
            body.Add(Add("UpdateDeletedRows"));
            body.Add(Stmt(Call(V("workTransaction"), "Commit")));
            // Distinct loop variables: VB declares them in the enclosing block.
            foreach ((string rows, string i) in new[] { ("allAddedRows", "i"), ("allChangedRows", "j") })
                body.Add(new CodeIterationStatement(
                    new CodeVariableDeclarationStatement(typeof(int), i, Prim(0)),
                    Op(V(i), CodeBinaryOperatorType.LessThan, P(V(rows), "Count")),
                    Assign(V(i), Op(V(i), CodeBinaryOperatorType.Add, Prim(1))),
                    Stmt(Call(Index(V(rows), V(i)), "AcceptChanges"))));

            var rollback = new CodeCatchClause("ex", new CodeTypeReference(typeof(Exception)),
                Stmt(Call(V("workTransaction"), "Rollback")),
                If(P("BackupDataSetBeforeUpdate"), new[]
                {
                    Stmt(Call(A("dataSet"), "Clear")),
                    Stmt(Call(A("dataSet"), "Merge", V("backupDataSet"))),
                }),
                new CodeThrowExceptionStatement());

            var restore = new CodeStatementCollection();
            restore.Add(If(V("workConnOpened"), Stmt(Call(V("workConnection"), "Close"))));
            foreach (Adapter adapter in adapters)
            {
                CodeExpression field = F(AdapterField(adapter));
                restore.Add(If(Op(field, CodeBinaryOperatorType.IdentityInequality, Null), new[]
                {
                    Assign(P(field, "Connection"), V("revert" + adapter.ClassName + "Connection")),
                    Assign(P(field, "Transaction"), Null),
                }));
            }
            restore.Add(new CodeIterationStatement(
                new CodeVariableDeclarationStatement(typeof(int), "i", Prim(0)),
                Op(V("i"), CodeBinaryOperatorType.LessThan, P(V("adaptersWithAcceptChangesDuringUpdate"), "Count")),
                Assign(V("i"), Op(V("i"), CodeBinaryOperatorType.Add, Prim(1))),
                Assign(P(Index(V("adaptersWithAcceptChangesDuringUpdate"), V("i")), "AcceptChangesDuringUpdate"), Prim(true))));

            var tryStatement = new CodeTryCatchFinallyStatement();
            tryStatement.TryStatements.AddRange(body);
            tryStatement.CatchClauses.Add(rollback);
            tryStatement.FinallyStatements.AddRange(restore);
            s.Add(tryStatement);
            s.Add(Return(V("result")));
            return method;
        }

        // ----------------------------------------------------------------------------------- CodeDOM helpers

        private static MemberAttributes Access(string modifier, MemberAttributes fallback) => modifier switch
        {
            "Public" => MemberAttributes.Public,
            "Assembly" => MemberAttributes.Assembly,
            "Family" => MemberAttributes.Family,
            "FamilyOrAssembly" => MemberAttributes.FamilyOrAssembly,
            "Private" => MemberAttributes.Private,
            _ => fallback,
        };

        private static CodeTypeDeclaration Class(string name, TypeAttributes access) =>
            new CodeTypeDeclaration(name) { IsClass = true, IsPartial = true, TypeAttributes = access | TypeAttributes.Class };

        private static CodeAttributeDeclaration Attribute(Type type, params CodeExpression[] arguments) =>
            new CodeAttributeDeclaration(new CodeTypeReference(type), arguments.Select(a => new CodeAttributeArgument(a)).ToArray());

        private static CodeAttributeDeclaration DataObjectMethod(DataObjectMethodType type, bool isDefault) =>
            Attribute(typeof(DataObjectMethodAttribute), EnumValue(typeof(DataObjectMethodType), type.ToString()), Prim(isDefault));

        private static CodeMemberField Field(CodeTypeReference type, string name) =>
            new CodeMemberField(type, name) { Attributes = MemberAttributes.Private };

        private static CodeMemberProperty Property(CodeTypeReference type, string name, MemberAttributes attributes) =>
            new CodeMemberProperty { Type = type, Name = name, Attributes = attributes };

        // get { if (field == null) Init(); return field; }
        private static CodeMemberProperty LazyProperty(CodeTypeReference type, string name, string field, string init, MemberAttributes attributes)
        {
            var property = Property(type, name, attributes);
            property.GetStatements.Add(If(Op(F(field), CodeBinaryOperatorType.IdentityEquality, Null), Stmt(Call(This, init))));
            property.GetStatements.Add(Return(F(field)));
            return property;
        }

        private static CodeMemberProperty SimpleProperty(CodeTypeReference type, string name, string field)
        {
            var property = Property(type, name, MemberAttributes.Public | MemberAttributes.Final);
            property.GetStatements.Add(Return(F(field)));
            property.SetStatements.Add(Assign(F(field), new CodePropertySetValueReferenceExpression()));
            return property;
        }

        private static CodeMemberMethod Method(MemberAttributes attributes, CodeTypeReference returnType, string name) =>
            new CodeMemberMethod { Attributes = attributes, ReturnType = returnType ?? new CodeTypeReference(typeof(void)), Name = name };

        private static CodeExpression This => new CodeThisReferenceExpression();
        private static CodeExpression Null => new CodePrimitiveExpression(null);
        private static CodeExpression F(string name) => new CodeFieldReferenceExpression(This, name);
        private static CodeExpression P(string name) => new CodePropertyReferenceExpression(This, name);
        private static CodeExpression P(CodeExpression target, string name) => new CodePropertyReferenceExpression(target, name);
        private static CodeExpression V(string name) => new CodeVariableReferenceExpression(name);
        private static CodeExpression A(string name) => new CodeArgumentReferenceExpression(name);
        private static CodeExpression Prim(object value) => new CodePrimitiveExpression(value);
        private static CodeExpression Call(CodeExpression target, string method, params CodeExpression[] arguments) =>
            new CodeMethodInvokeExpression(target, method, arguments);
        private static CodeExpression New(CodeTypeReference type, params CodeExpression[] arguments) => new CodeObjectCreateExpression(type, arguments);
        private static CodeExpression Cast(CodeTypeReference type, CodeExpression expression) => new CodeCastExpression(type, expression);
        private static CodeExpression Index(CodeExpression target, CodeExpression index) => new CodeIndexerExpression(target, index);
        private static CodeExpression ArrayIndex(CodeExpression target, CodeExpression index) => new CodeArrayIndexerExpression(target, index);
        private static CodeExpression Op(CodeExpression left, CodeBinaryOperatorType op, CodeExpression right) =>
            new CodeBinaryOperatorExpression(left, op, right);
        private static CodeExpression Not(CodeExpression condition) => Op(condition, CodeBinaryOperatorType.ValueEquality, Prim(false));
        private static CodeExpression EnumValue(Type type, string name) =>
            new CodeFieldReferenceExpression(new CodeTypeReferenceExpression(type), name);
        private static CodeStatement Assign(CodeExpression left, CodeExpression right) => new CodeAssignStatement(left, right);
        private static CodeStatement Stmt(CodeExpression expression) => new CodeExpressionStatement(expression);
        private static CodeStatement Return(CodeExpression expression) => new CodeMethodReturnStatement(expression);
        private static CodeStatement Var(CodeTypeReference type, string name, CodeExpression value) =>
            new CodeVariableDeclarationStatement(type, name, value);
        private static CodeStatement If(CodeExpression condition, params CodeStatement[] statements) =>
            new CodeConditionStatement(condition, statements);
        private static CodeStatement If(CodeExpression condition, CodeStatement[] statements, CodeStatement[] otherwise) =>
            new CodeConditionStatement(condition, statements, otherwise);
    }
}
