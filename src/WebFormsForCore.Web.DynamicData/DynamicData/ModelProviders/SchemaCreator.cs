using System.Data.Linq;
#if NETFRAMEWORK // webForCore: Entity Framework 1-4 (System.Data.Entity's ObjectContext) isn't on .NET (docs/NAMESPACES.md).
using System.Data.Objects;
#endif
using System.Globalization;
using System.Web.Resources;

namespace System.Web.DynamicData.ModelProviders {
    internal class SchemaCreator {
        private static SchemaCreator s_instance = new SchemaCreator();

        public static SchemaCreator Instance {
            get {
                return s_instance;
            }
        }

        public virtual DataModelProvider CreateDataModel(object contextInstance, Func<object> contextFactory) {
            if (IsDataContext(contextInstance.GetType())) {
                return new DLinqDataModelProvider(contextInstance, contextFactory);
            }
#if NETFRAMEWORK
            if (IsObjectContext(contextInstance.GetType())) {
                return new EFDataModelProvider(contextInstance, contextFactory);
            }
#endif

            throw new InvalidOperationException(String.Format(CultureInfo.CurrentCulture, DynamicDataResources.SchemaCreator_UnknownModel, contextInstance.GetType().FullName));
        }

        public virtual bool ValidDataContextType(Type contextType) {
            // 
            return IsDataContext(contextType) || IsObjectContext(contextType);
        }

        internal static bool IsDataContext(Type contextType) {
            return IsValidType<DataContext>(contextType);
        }

        internal static bool IsObjectContext(Type contextType) {
        // webForCore: no EF 1-4 ObjectContext on .NET; RegisterContext takes a DataContext or a DataModelProvider.
#if NETFRAMEWORK
            return IsValidType<ObjectContext>(contextType);
#else
            return false;
#endif
        }

        private static bool IsValidType<T>(Type contextType) where T : class {
            return contextType != null && typeof(T).IsAssignableFrom(contextType);
        }
    }
}
