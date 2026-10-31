// webForCore (MIT): what the Reference Source's TypedDataSetGenerator (ReferenceSource/datacache.cs) took from System.Data's
// internals, from public members instead (tools/lift/lift_strb.py applies the replacements).
using System.Collections;
using System.Globalization;
using System.Text;
using System.Xml;

namespace System.Data
{
    internal static class CodeGenSupport
    {
        // System.Data's resource names and texts (Reference Source System.Data/system.data.txt).
        internal const string CodeGen_InvalidIdentifier = "Cannot generate identifier for name '{0}'.";
        internal const string CodeGen_DuplicateTableName = "There is more than one table with the same name '{0}' (even if namespace is different).";
        internal const string CodeGen_TypeCantBeNull = "Column '{0}': Type '{1}' cannot be null.";
        internal const string CodeGen_NoCtor0 = "Column '{0}': Type '{1}' does not have parameterless constructor.";
        internal const string CodeGen_NoCtor1 = "Column '{0}': Type '{1}' does not have constructor with string argument.";

        internal static string GetString(string name, params object[] args)
        {
            string text = name switch
            {
                "StrongTyping_CananotAccessDBNull" => "Cannot get value because it is DBNull.",
                "StrongTyping_CannotRemoveColumn" => "Cannot remove column since it is built in to this dataSet.",
                "StrongTyping_CananotRemoveRelation" => "Cannot remove relation since it is built in to this dataSet.",
                _ => name,
            };
            return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
        }

        // DataTableCollection.InternalIndexOf(name) == -3: tables of that name in more than one namespace.
        internal static bool HasNamesake(DataTable table)
        {
            int count = 0;
            foreach (DataTable other in table.DataSet.Tables)
                if (other.TableName == table.TableName)
                    count++;
            return count > 1;
        }

        // The schema set these rather than inheriting them from the table or the DataSet (DataColumn._columnUri,
        // DataTable.tableNamespace, ShouldSerializeCaseSensitive, ShouldSerializeLocale).
        internal static bool HasOwnNamespace(DataColumn column) =>
            column.Table != null && column.Namespace != column.Table.Namespace;

        internal static bool HasOwnNamespace(DataTable table) =>
            table.Namespace != (table.DataSet?.Namespace ?? string.Empty);

        internal static bool HasOwnCaseSensitive(DataTable table) =>
            table.DataSet == null || table.CaseSensitive != table.DataSet.CaseSensitive;

        internal static bool HasOwnLocale(DataTable table) =>
            table.DataSet == null || !table.Locale.Equals(table.DataSet.Locale);

        // DataColumn.FindParentRelation: the relation whose single child column is this column.
        internal static DataRelation FindParentRelation(DataColumn column)
        {
            foreach (DataRelation relation in column.Table.ParentRelations)
            {
                DataColumn[] columns = relation.ChildColumns;
                if (columns.Length == 1 && columns[0] == column)
                    return relation;
            }
            return null;
        }

        // DataColumn.ConvertXmlToObject for the column types a codegen:nullValue may have.
        internal static object ConvertXmlToObject(DataColumn column, string value)
        {
            Type type = column.DataType;
            if (type == typeof(string)) return value;
            if (type == typeof(char)) return XmlConvert.ToChar(value);
            if (type == typeof(bool)) return XmlConvert.ToBoolean(value);
            if (type == typeof(decimal)) return XmlConvert.ToDecimal(value);
            if (type == typeof(float)) return XmlConvert.ToSingle(value);
            if (type == typeof(double)) return XmlConvert.ToDouble(value);
            if (type == typeof(sbyte)) return XmlConvert.ToSByte(value);
            if (type == typeof(byte)) return XmlConvert.ToByte(value);
            if (type == typeof(short)) return XmlConvert.ToInt16(value);
            if (type == typeof(ushort)) return XmlConvert.ToUInt16(value);
            if (type == typeof(int)) return XmlConvert.ToInt32(value);
            if (type == typeof(uint)) return XmlConvert.ToUInt32(value);
            if (type == typeof(long)) return XmlConvert.ToInt64(value);
            if (type == typeof(ulong)) return XmlConvert.ToUInt64(value);
            if (type == typeof(DateTime)) return XmlConvert.ToDateTime(value, XmlDateTimeSerializationMode.RoundtripKind);
            if (type == typeof(TimeSpan)) return XmlConvert.ToTimeSpan(value);
            if (type == typeof(Guid)) return XmlConvert.ToGuid(value);
            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }

        // System.Data's TypedDataSetGeneratorException, which .NET doesn't have.
        internal static DataException GeneratorException(ArrayList errors)
        {
            var message = new StringBuilder();
            foreach (object error in errors)
                message.Append(message.Length == 0 ? "" : " ").Append(error);
            return new DataException(message.ToString());
        }
    }
}
