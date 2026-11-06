using System.Configuration;
using System.Data.Linq;
using System.Data.Linq.Mapping;

// A LINQ to SQL model as the O/R designer (.dbml) generates it, for the LinqDataSource pages in linq/: a DataContext
// with a parameterless constructor that takes its connection string from Web.config, and attribute-mapped entities.
// Products has a rowversion column, which LINQ to SQL maps to System.Data.Linq.Binary and checks on update and delete.
[Database(Name = "WebForCoreLinqGate")]
public class LinqCatalogDataContext : DataContext
{
    private static readonly MappingSource mappingSource = new AttributeMappingSource();

    public LinqCatalogDataContext()
        : base(ConfigurationManager.ConnectionStrings["LinqCatalog"].ConnectionString, mappingSource)
    {
    }

    public Table<LinqProduct> Products
    {
        get { return GetTable<LinqProduct>(); }
    }

    public Table<LinqCategory> Categories
    {
        get { return GetTable<LinqCategory>(); }
    }
}

[Table(Name = "dbo.Categories")]
public class LinqCategory
{
    [Column(Storage = "_CategoryID", AutoSync = AutoSync.OnInsert, DbType = "Int NOT NULL IDENTITY", IsPrimaryKey = true, IsDbGenerated = true)]
    public int CategoryID { get { return _CategoryID; } set { _CategoryID = value; } }
    private int _CategoryID;

    [Column(Storage = "_CategoryName", DbType = "NVarChar(50) NOT NULL", CanBeNull = false)]
    public string CategoryName { get { return _CategoryName; } set { _CategoryName = value; } }
    private string _CategoryName;
}

[Table(Name = "dbo.Products")]
public class LinqProduct
{
    [Column(Storage = "_ProductID", AutoSync = AutoSync.OnInsert, DbType = "Int NOT NULL IDENTITY", IsPrimaryKey = true, IsDbGenerated = true)]
    public int ProductID { get { return _ProductID; } set { _ProductID = value; } }
    private int _ProductID;

    [Column(Storage = "_ProductName", DbType = "NVarChar(50) NOT NULL", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
    public string ProductName { get { return _ProductName; } set { _ProductName = value; } }
    private string _ProductName;

    [Column(Storage = "_CategoryID", DbType = "Int NOT NULL", UpdateCheck = UpdateCheck.Never)]
    public int CategoryID { get { return _CategoryID; } set { _CategoryID = value; } }
    private int _CategoryID;

    [Column(Storage = "_UnitPrice", DbType = "Money NOT NULL", UpdateCheck = UpdateCheck.Never)]
    public decimal UnitPrice { get { return _UnitPrice; } set { _UnitPrice = value; } }
    private decimal _UnitPrice;

    [Column(Storage = "_Discontinued", DbType = "Bit NOT NULL", UpdateCheck = UpdateCheck.Never)]
    public bool Discontinued { get { return _Discontinued; } set { _Discontinued = value; } }
    private bool _Discontinued;

    [Column(Storage = "_Version", AutoSync = AutoSync.Always, DbType = "rowversion NOT NULL", CanBeNull = false, IsDbGenerated = true, IsVersion = true, UpdateCheck = UpdateCheck.Never)]
    public Binary Version { get { return _Version; } set { _Version = value; } }
    private Binary _Version;
}
