// webForCore (MIT): System.Design's System.Data.Design.TypedDataSetGenerator, which isn't open source, for
// XsdBuildProvider (.xsd files in App_Code). The DataSet, table and row classes come from the Reference Source's
// System.Data.TypedDataSetGenerator (ReferenceSource/datacache.cs), adjusted to what Visual Studio's DataSet designer
// generates; the TableAdapters from the designer's annotation come from TableAdapterGenerator.
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using CodeDomProvider = WebFormsForCore.CodeDom.Compiler.CodeDomProvider;

namespace System.Data.Design
{
    internal static class TypedDataSetGenerator
    {
        [Flags]
        internal enum GenerateOption
        {
            None = 0,
            HierarchicalUpdate = 1,
            LinqOverTypedDatasets = 2,
        }

        // The assemblies the generated code needs, after Generate.
        [ThreadStatic]
        private static List<Assembly> referencedAssemblies;

        internal static ICollection<Assembly> ReferencedAssemblies => referencedAssemblies;

        internal static string Generate(string inputFileContent, CodeCompileUnit compileUnit, CodeNamespace mainNamespace,
            CodeDomProvider codeProvider) =>
            Generate(inputFileContent, compileUnit, mainNamespace, codeProvider, null, GenerateOption.None);

        internal static string Generate(string inputFileContent, CodeCompileUnit compileUnit, CodeNamespace mainNamespace,
            CodeDomProvider codeProvider, Hashtable customDBProviders, GenerateOption option)
        {
            var references = new List<Assembly>
            {
                typeof(DataSet).Assembly,                                // System.Data.Common
                typeof(System.ComponentModel.Component).Assembly,         // System.ComponentModel.Primitives
                typeof(System.ComponentModel.DataObjectAttribute).Assembly,
                typeof(XmlReader).Assembly,                               // System.Private.Xml
                typeof(System.Xml.Serialization.XmlRootAttribute).Assembly,
                typeof(System.Runtime.Serialization.SerializationInfo).Assembly,
                typeof(object).Assembly,
            };
            referencedAssemblies = references;

            var dataSet = new DataSet();
            using (var reader = new StringReader(inputFileContent))
                dataSet.ReadXmlSchema(reader);
            UseDesignerNames(dataSet);

#pragma warning disable CS0618 // ICodeGenerator: the Reference Source generator's interface
            ICodeGenerator generator = codeProvider.CreateGenerator();
#pragma warning restore CS0618
            CodeTypeDeclaration dataSetClass = new System.Data.TypedDataSetGenerator().GenerateCode(dataSet, mainNamespace, generator);
            Dictionary<string, (string Table, string Row)> tableClasses = AsDesignerGenerates(dataSetClass,
                (option & GenerateOption.LinqOverTypedDatasets) != 0);

            var document = new XmlDocument();
            document.LoadXml(inputFileContent);
            CodeNamespace adapters = new TableAdapterGenerator(document, dataSet, dataSetClass.Name, mainNamespace.Name,
                tableClasses, codeProvider, customDBProviders, references).Generate((option & GenerateOption.HierarchicalUpdate) != 0);
            if (adapters != null)
                compileUnit.Namespaces.Add(adapters);

            CodeGenerator.ValidateIdentifiers(mainNamespace);
            if (adapters != null)
                CodeGenerator.ValidateIdentifiers(adapters);
            return dataSetClass.Name;
        }

        // The designer records its names (msprop:Generator_*, read into ExtendedProperties); the Reference Source
        // generator takes them from its own codegen annotations.
        private static void UseDesignerNames(DataSet dataSet)
        {
            foreach (DataTable table in dataSet.Tables)
            {
                foreach (DataColumn column in table.Columns)
                    if (column.ExtendedProperties["Generator_ColumnPropNameInRow"] is string name && name.Length != 0
                        && column.ExtendedProperties["typedName"] == null)
                        column.ExtendedProperties["typedName"] = name;
            }
            foreach (DataRelation relation in dataSet.Relations)
            {
                if (relation.ExtendedProperties["Generator_ChildPropName"] is string children && children.Length != 0
                    && relation.ExtendedProperties["typedChildren"] == null)
                    relation.ExtendedProperties["typedChildren"] = children;
                if (relation.ExtendedProperties["Generator_ParentPropName"] is string parent && parent.Length != 0
                    && relation.ExtendedProperties["typedParent"] == null)
                    relation.ExtendedProperties["typedParent"] = parent;
            }
        }

        // What the designer generates and the .NET 1.x generator doesn't: partial classes (applications extend them),
        // public table constructors and column properties (pages compile into other assemblies than App_Code), and
        // tables deriving from TypedTableBase<row> (typed foreach and LINQ). Returns each table's class and row class.
        private static Dictionary<string, (string Table, string Row)> AsDesignerGenerates(CodeTypeDeclaration dataSetClass, bool linq)
        {
            var tables = new Dictionary<string, (string, string)>();
            dataSetClass.IsPartial = true;
            foreach (CodeTypeMember member in dataSetClass.Members)
            {
                if (member is not CodeTypeDeclaration type || !type.IsClass)
                    continue;
                type.IsPartial = true;
                if (type.BaseTypes.Count == 0 || type.BaseTypes[0].BaseType != "System.Data.DataTable")
                    continue;

                string tableName = null;
                CodeTypeReference rowType = null;
                CodeTypeMember enumerator = null;
                foreach (CodeTypeMember tableMember in type.Members)
                {
                    switch (tableMember)
                    {
                        case CodeConstructor constructor when constructor.Parameters.Count == 0:
                            constructor.Attributes = (constructor.Attributes & ~MemberAttributes.AccessMask) | MemberAttributes.Public;
                            tableName = (string)((CodePrimitiveExpression)constructor.BaseConstructorArgs[0]).Value;
                            if (linq)
                                MoveTableNameToBody(constructor);
                            break;
                        case CodeConstructor constructor when constructor.Parameters.Count == 1 && linq:
                            MoveTableNameToBody(constructor);
                            break;
                        case CodeMemberProperty property when property.Name == "Item":
                            rowType = property.Type;
                            break;
                        case CodeMemberProperty property when property.Type.BaseType == "System.Data.DataColumn":
                            property.Attributes = (property.Attributes & ~MemberAttributes.AccessMask) | MemberAttributes.Public;
                            break;
                        case CodeMemberMethod method when method.Name == "GetEnumerator":
                            enumerator = method;
                            break;
                    }
                }
                tables[tableName] = (type.Name, rowType.BaseType);
                if (linq)
                {
                    // TypedTableBase<T> is the DataTable and IEnumerable<T>.
                    type.BaseTypes.Clear();
                    type.BaseTypes.Add(new CodeTypeReference("System.Data.TypedTableBase", rowType));
                    if (enumerator != null)
                        type.Members.Remove(enumerator);
                }
            }
            return tables;
        }

        // TypedTableBase<T> has no (string tableName) constructor: base(name) becomes this.TableName = name.
        private static void MoveTableNameToBody(CodeConstructor constructor)
        {
            if (constructor.BaseConstructorArgs.Count != 1)
                return;
            CodeExpression name = constructor.BaseConstructorArgs[0];
            constructor.BaseConstructorArgs.Clear();
            constructor.Statements.Insert(0, new CodeAssignStatement(
                new CodePropertyReferenceExpression(new CodeThisReferenceExpression(), "TableName"), name));
        }
    }
}
