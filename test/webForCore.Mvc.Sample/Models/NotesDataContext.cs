using System.Configuration;
using System.Data.Linq;
using System.Data.Linq.Mapping;
using System.Web.Mvc;

namespace MvcSample.Models
{
    // A LINQ to SQL model as the O/R designer generates it, with a rowversion column: LINQ to SQL maps it to
    // System.Data.Linq.Binary, which MVC writes into the edit form as base64 and LinqBinaryModelBinder binds back (#142).
    public class NotesDataContext : DataContext
    {
        private static readonly MappingSource mappingSource = new AttributeMappingSource();

        public NotesDataContext()
            : base(ConfigurationManager.ConnectionStrings["Notes"].ConnectionString, mappingSource)
        {
        }

        public Table<Note> Notes
        {
            get { return GetTable<Note>(); }
        }
    }

    [Table(Name = "dbo.Notes")]
    public class Note
    {
        [Column(DbType = "Int NOT NULL", IsPrimaryKey = true)]
        public int Id { get; set; }

        [Column(DbType = "NVarChar(100) NOT NULL", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string Text { get; set; }

        // As the designer's partial classes mark it, so that the editor template writes it as a hidden input.
        [HiddenInput(DisplayValue = false)]
        [Column(AutoSync = AutoSync.Always, DbType = "rowversion NOT NULL", CanBeNull = false, IsDbGenerated = true, IsVersion = true, UpdateCheck = UpdateCheck.Never)]
        public Binary Version { get; set; }
    }
}
