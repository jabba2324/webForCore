#if NETFRAMEWORK
using System.Data.Linq;
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
#if NETFRAMEWORK
            if (IsDataContext(contextInstance.GetType())) {
                return new DLinqDataModelProvider(contextInstance, contextFactory);
            }
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

        // webForCore: no LINQ to SQL DataContext or EF 1-4 ObjectContext on .NET; RegisterContext takes a DataModelProvider.
        internal static bool IsDataContext(Type contextType) {
#if NETFRAMEWORK
            return IsValidType<DataContext>(contextType);
#else
            return false;
#endif
        }

        internal static bool IsObjectContext(Type contextType) {
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
