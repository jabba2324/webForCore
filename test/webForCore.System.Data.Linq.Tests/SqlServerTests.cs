// webForCore's tests (MIT), #142: LINQ to SQL against SQL Server, as an application uses it (attribute mapping,
// associations, change tracking, query translation, stored procedures and functions, transactions, XML mapping,
// inheritance). The server is CI's (127.0.0.1,14333, as for Identity.Test: tools/ci/sqlserver.sh locally, a service
// container on Linux and SQL Server Express on Windows), or the one whose connection string, without a database, is in
// WEBFORCORE_LINQ_SQLSERVER. Each run creates its own database with DataContext.CreateDatabase and drops it afterwards.
// The fixture adds six customers, "Customer A" to "Customer F", with 1 to 6 orders in 2024; tests that change data
// add rows of their own, named "Temp ...", so that the tests don't depend on each other.

using System.Collections.Generic;
using System.Data.Linq.Mapping;
using System.Data.Linq.SqlClient;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Transactions;
using Xunit;

namespace System.Data.Linq.Tests
{
    public sealed class SqlServerFixture : IDisposable
    {
        private readonly string _server;

        public SqlServerFixture()
        {
            _server = Environment.GetEnvironmentVariable("WEBFORCORE_LINQ_SQLSERVER")
                ?? "Server=127.0.0.1,14333;User Id=sa;Password=webForCore-Test1!;TrustServerCertificate=True";
            DatabaseName = "WebForCoreLinq_" + Guid.NewGuid().ToString("N")[..8];
            ConnectionString = new SqlConnectionStringBuilder(_server) { InitialCatalog = DatabaseName }.ConnectionString;

            using (var shop = new Shop(ConnectionString))
            {
                ExistedBefore = shop.DatabaseExists();
                shop.CreateDatabase();
                ExistsAfter = shop.DatabaseExists();
            }
            using (var connection = new SqlConnection(ConnectionString))
            {
                connection.Open();
                new SqlCommand("CREATE PROCEDURE dbo.CustomersInCity @city nvarchar(50) AS SELECT Id, Name, City, Photo, Version FROM Customers WHERE City = @city ORDER BY Name", connection).ExecuteNonQuery();
                new SqlCommand("CREATE FUNCTION dbo.OrderTotal(@customerId int) RETURNS decimal(18,2) AS BEGIN RETURN (SELECT ISNULL(SUM(Amount), 0) FROM Orders WHERE CustomerId = @customerId) END", connection).ExecuteNonQuery();
            }

            // Customer A (London) has 1 order of 10, B (Paris) 2 (10, 20), ... F (London) 6 (10 to 60); 21 orders.
            using (var shop = new Shop(ConnectionString))
            {
                string[] cities = ["London", "Paris", "London", "Berlin", "Paris", "London"];
                for (int i = 0; i < 6; i++)
                {
                    var customer = new Customer { Name = "Customer " + (char)('A' + i), City = cities[i], Photo = new Binary([(byte)i, 1, 2, 3]) };
                    for (int j = 0; j <= i; j++)
                        customer.Orders.Add(new Order { Amount = 10m * (j + 1), OrderDate = new DateTime(2024, 1, 1).AddDays(i * 10 + j) });
                    shop.Customers.InsertOnSubmit(customer);
                }
                shop.SubmitChanges();
            }
        }

        public string DatabaseName { get; }

        public string ConnectionString { get; }

        public bool ExistedBefore { get; }

        public bool ExistsAfter { get; }

        public void Dispose()
        {
            SqlConnection.ClearAllPools();
            using var connection = new SqlConnection(_server);
            connection.Open();
            new SqlCommand($"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}];", connection).ExecuteNonQuery();
        }
    }

    public class SqlServerTests : IClassFixture<SqlServerFixture>
    {
        private readonly SqlServerFixture _fixture;

        public SqlServerTests(SqlServerFixture fixture)
        {
            _fixture = fixture;
        }

        private Shop NewShop() => new Shop(_fixture.ConnectionString);

        // The fixture's six customers.
        private static IQueryable<Customer> Seeded(Shop shop) => shop.Customers.Where(c => c.Name.StartsWith("Customer "));

        private Customer AddTemp(string name, string city, int orders = 0)
        {
            using var shop = NewShop();
            var customer = new Customer { Name = name, City = city };
            for (int i = 0; i < orders; i++)
                customer.Orders.Add(new Order { Amount = 1m, OrderDate = new DateTime(2030, 1, 1) });
            shop.Customers.InsertOnSubmit(customer);
            shop.SubmitChanges();
            return customer;
        }

        [Fact]
        public void CreateDatabaseCreatesTheMappedDatabase()
        {
            Assert.False(_fixture.ExistedBefore);
            Assert.True(_fixture.ExistsAfter);
            using var shop = NewShop();
            Assert.Equal(6, Seeded(shop).Count());
            Assert.Equal(21, shop.Orders.Count(o => o.OrderDate.Year == 2024));
        }

        [Fact]
        public void InsertBackFillsIdentityAndRowVersion()
        {
            using var shop = NewShop();
            var customer = new Customer { Name = "Temp Insert", City = "Temp" };
            customer.Orders.Add(new Order { Amount = 5m, OrderDate = new DateTime(2030, 1, 1) });
            customer.Orders.Add(new Order { Amount = 7m, OrderDate = new DateTime(2030, 1, 2) });
            shop.Customers.InsertOnSubmit(customer);

            shop.SubmitChanges();

            Assert.True(customer.Id > 0);
            Assert.NotNull(customer.Version);
            Assert.All(customer.Orders, o => Assert.Equal(customer.Id, o.CustomerId));
            Assert.Equal(12m, NewShop().Orders.Where(o => o.CustomerId == customer.Id).Sum(o => o.Amount));
        }

        [Fact]
        public void WhereAndOrderByTranslateStringMethodsToLike()
        {
            using var shop = NewShop();
            var log = new StringWriter();
            shop.Log = log;

            var names = Seeded(shop).Where(c => c.City == "London" && c.Name.StartsWith("Cust") && c.Name.Contains("ome"))
                .OrderByDescending(c => c.Name).Select(c => c.Name).ToList();

            Assert.Equal(["Customer F", "Customer C", "Customer A"], names);
            Assert.Contains("LIKE", log.ToString());
            // SqlProvider's log names its build: ThisAssembly.InformationalVersion, the file version.
            Assert.Contains("Build: " + ThisAssembly.InformationalVersion, log.ToString());
        }

        [Fact]
        public void JoinProjectsIntoAnAnonymousType()
        {
            using var shop = NewShop();

            var rows = (from c in shop.Customers
                        join o in shop.Orders on c.Id equals o.CustomerId
                        where c.City == "Berlin"
                        orderby o.Amount
                        select new { c.Name, o.Amount }).ToList();

            Assert.Equal(4, rows.Count);
            Assert.Equal("Customer D", rows[0].Name);
            Assert.Equal(40m, rows[3].Amount);
        }

        [Fact]
        public void GroupByCountsAndSumsThroughAnAssociation()
        {
            using var shop = NewShop();

            var groups = (from o in shop.Orders
                          where o.Customer.Name.StartsWith("Customer ")
                          group o by o.Customer.City into g
                          orderby g.Key
                          select new { City = g.Key, Count = g.Count(), Total = g.Sum(x => x.Amount) }).ToList();

            Assert.Equal(["Berlin", "London", "Paris"], groups.Select(g => g.City));
            Assert.Equal([4, 1 + 3 + 6, 2 + 5], groups.Select(g => g.Count));
            Assert.Equal([100m, 10m + 60m + 210m, 30m + 150m], groups.Select(g => g.Total));
        }

        [Fact]
        public void SkipAndTakePageWithRowNumber()
        {
            using var shop = NewShop();
            var log = new StringWriter();
            shop.Log = log;

            var page = shop.Orders.Where(o => o.OrderDate.Year == 2024).OrderBy(o => o.OrderDate).Skip(5).Take(3).Select(o => o.OrderDate).ToList();

            // The dates are 1 January plus 0; 10, 11; 20, 21, 22; 30 ...: the sixth is 23 January.
            Assert.Equal([new DateTime(2024, 1, 23), new DateTime(2024, 1, 31), new DateTime(2024, 2, 1)], page);
            Assert.Contains("ROW_NUMBER", log.ToString());
        }

        [Fact]
        public void EntitySetAndEntityRefLoadWhenUsed()
        {
            using var shop = NewShop();

            var d = shop.Customers.Single(c => c.Name == "Customer D");

            Assert.Equal(4, d.Orders.Count);
            Assert.Same(d, d.Orders[0].Customer);
        }

        [Fact]
        public void LoadWithLoadsAssociationsInTheSameQuery()
        {
            using var shop = NewShop();
            var options = new DataLoadOptions();
            options.LoadWith<Customer>(c => c.Orders);
            shop.LoadOptions = options;
            var log = new StringWriter();
            shop.Log = log;

            var customers = Seeded(shop).OrderBy(c => c.Name).ToList();
            int queries = log.ToString().Split("SELECT").Length;

            Assert.Equal(21, customers.Sum(c => c.Orders.Count));
            Assert.Equal(queries, log.ToString().Split("SELECT").Length);
        }

        [Fact]
        public void UpdateWritesChanges()
        {
            var temp = AddTemp("Temp Update", "Temp");
            using (var shop = NewShop())
            {
                shop.Customers.Single(c => c.Id == temp.Id).City = "Madrid";
                shop.SubmitChanges();
            }

            Assert.Equal("Madrid", NewShop().Customers.Single(c => c.Id == temp.Id).City);
        }

        [Fact]
        public void ConcurrencyConflictIsReportedAndResolved()
        {
            var temp = AddTemp("Temp Conflict", "Temp");
            using var first = NewShop();
            using var second = NewShop();
            var one = first.Customers.Single(c => c.Id == temp.Id);
            var two = second.Customers.Single(c => c.Id == temp.Id);
            one.City = "Rome";
            first.SubmitChanges();
            two.City = "Oslo";

            var conflict = Assert.Throws<ChangeConflictException>(() => second.SubmitChanges());

            Assert.Equal("Row not found or changed.", conflict.Message);
            Assert.Single(second.ChangeConflicts);
            second.ChangeConflicts.ResolveAll(RefreshMode.KeepCurrentValues);
            second.SubmitChanges();
            Assert.Equal("Oslo", NewShop().Customers.Single(c => c.Id == temp.Id).City);
        }

        [Fact]
        public void DeleteOnSubmitDeletesRows()
        {
            var temp = AddTemp("Temp Delete", "Temp", orders: 3);
            using (var shop = NewShop())
            {
                var customer = shop.Customers.Single(c => c.Id == temp.Id);
                shop.Orders.DeleteAllOnSubmit(customer.Orders);
                shop.Customers.DeleteOnSubmit(customer);
                shop.SubmitChanges();
            }

            using var after = NewShop();
            Assert.False(after.Customers.Any(c => c.Id == temp.Id));
            Assert.False(after.Orders.Any(o => o.CustomerId == temp.Id));
        }

        private static readonly Func<Shop, string, IQueryable<Customer>> ByCity =
            CompiledQuery.Compile((Shop shop, string city) => shop.Customers.Where(c => c.City == city && c.Name.StartsWith("Customer ")).OrderBy(c => c.Name));

        [Fact]
        public void CompiledQueryRunsWithEachParameter()
        {
            using var shop = NewShop();

            Assert.Equal(["Customer A", "Customer C", "Customer F"], ByCity(shop, "London").Select(c => c.Name));
            Assert.Equal(["Customer B", "Customer E"], ByCity(shop, "Paris").Select(c => c.Name));
        }

        [Fact]
        public void AggregatesAndLocalCollectionsTranslate()
        {
            using var shop = NewShop();
            var cities = new List<string> { "Paris", "Berlin" };

            Assert.Equal(3, Seeded(shop).Count(c => cities.Contains(c.City)));
            Assert.True(Seeded(shop).Any(c => c.Orders.Any(o => o.Amount > 50m)));
            Assert.True(Seeded(shop).All(c => c.Orders.Count() > 0));
            Assert.Equal(60m, shop.Orders.Where(o => o.OrderDate.Year == 2024).Max(o => o.Amount));
            Assert.Null(shop.Customers.FirstOrDefault(c => c.City == "Nowhere"));
        }

        [Fact]
        public void DateTimeMembersAndSqlMethodsLikeTranslate()
        {
            using var shop = NewShop();

            Assert.Equal(21, shop.Orders.Count(o => o.OrderDate.Year == 2024 && o.Customer.Name.StartsWith("Customer ")));
            Assert.Equal(3, Seeded(shop).Count(c => SqlMethods.Like(c.City, "L%")));
        }

        [Fact]
        public void BinaryColumnsRoundTrip()
        {
            using var shop = NewShop();

            var e = shop.Customers.Single(c => c.Name == "Customer E");

            Assert.Equal("04010203", Convert.ToHexString(e.Photo.ToArray()));
            Assert.Equal(8, e.Version.Length);
        }

        [Fact]
        public void ExecuteQueryAndExecuteCommandRunSql()
        {
            var temp = AddTemp("Temp Command", "Temp");
            using var shop = NewShop();

            var berlin = shop.ExecuteQuery<Customer>("SELECT Id, Name, City, Photo, Version FROM Customers WHERE City = {0}", "Berlin").ToList();
            int updated = shop.ExecuteCommand("UPDATE Customers SET City = {0} WHERE Id = {1}", "Lisbon", temp.Id);

            Assert.Equal(["Customer D"], berlin.Select(c => c.Name));
            Assert.Equal(1, updated);
            Assert.Equal("Lisbon", NewShop().Customers.Single(c => c.Id == temp.Id).City);
        }

        [Fact]
        public void StoredProcedureReturnsASingleResult()
        {
            using var shop = NewShop();

            Assert.Equal(["Customer D"], shop.CustomersInCity("Berlin").Select(c => c.Name));
        }

        [Fact]
        public void ComposableFunctionTranslatesInAQuery()
        {
            using var shop = NewShop();

            // Totals: A 10, B 30, C 60, D 100, E 150, F 210.
            var names = Seeded(shop).Where(c => shop.OrderTotal(c.Id) > 100m).Select(c => c.Name).OrderBy(n => n).ToList();

            Assert.Equal(["Customer E", "Customer F"], names);
        }

        [Fact]
        public void TransactionScopeRollsBack()
        {
            using (new TransactionScope())
            using (var shop = NewShop())
            {
                shop.Customers.InsertOnSubmit(new Customer { Name = "Temp Rolled back", City = "Temp" });
                shop.SubmitChanges();
            }

            Assert.False(NewShop().Customers.Any(c => c.Name == "Temp Rolled back"));
        }

        [Fact]
        public void AttachAsModifiedUpdatesADetachedEntity()
        {
            var temp = AddTemp("Temp Attach", "Temp");
            Customer detached;
            using (var shop = NewShop())
            {
                shop.ObjectTrackingEnabled = false;
                detached = shop.Customers.Single(c => c.Id == temp.Id);
            }
            using (var shop = NewShop())
            {
                var copy = new Customer { Id = detached.Id, Name = detached.Name, City = "Vienna", Photo = detached.Photo, Version = detached.Version };
                shop.Customers.Attach(copy, true);
                Assert.Single(shop.GetChangeSet().Updates);
                shop.SubmitChanges();
            }

            Assert.Equal("Vienna", NewShop().Customers.Single(c => c.Id == temp.Id).City);
        }

        [Fact]
        public void XmlMappingSourceMapsPlainClasses()
        {
            string xml = $"""
                <Database Name="{_fixture.DatabaseName}" xmlns="http://schemas.microsoft.com/linqtosql/mapping/2007">
                  <Table Name="dbo.Customers" Member="PlainCustomers">
                    <Type Name="{typeof(PlainCustomer).FullName}">
                      <Column Name="Id" Member="Id" IsPrimaryKey="true" IsDbGenerated="true" />
                      <Column Name="Name" Member="Name" />
                    </Type>
                  </Table>
                </Database>
                """;
            using var context = new DataContext(_fixture.ConnectionString, XmlMappingSource.FromXml(xml));

            var names = context.GetTable<PlainCustomer>().Where(c => c.Name.StartsWith("Customer ")).OrderBy(c => c.Name).Select(c => c.Name).ToList();

            Assert.Equal(6, names.Count);
            Assert.Equal("Customer A", names[0]);
        }

        [Fact]
        public void InheritanceMappingUsesTheDiscriminator()
        {
            using (var shop = NewShop())
            {
                shop.Animals.InsertOnSubmit(new Dog { Name = "Rex" });
                shop.Animals.InsertOnSubmit(new Cat { Name = "Tom" });
                shop.SubmitChanges();
            }

            using var after = NewShop();
            Assert.Equal("Rex", after.Animals.OfType<Dog>().Single(d => d.Name == "Rex").Name);
            Assert.IsType<Cat>(after.Animals.Single(a => a.Name == "Tom"));
            Assert.Equal("Cat", after.Animals.Single(a => a.Name == "Tom").Kind);
        }

        // .NET Framework's error for a method SQL can't express: a NotSupportedException with its message.
        [Fact]
        public void UntranslatableMethodThrowsFrameworksNotSupportedException()
        {
            using var shop = NewShop();

            var error = Assert.Throws<NotSupportedException>(() => shop.Customers.Where(c => c.Name.GetHashCode() == 0).ToList());

            Assert.Equal("Method 'Int32 GetHashCode()' has no supported translation to SQL.", error.Message);
        }

        // DBConvert converts values to and from Binary with BinaryFormatter, webForCore's port on .NET.
        [Fact]
        public void DBConvertSerializesThroughBinary()
        {
            var value = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(2));

            var binary = (Binary)DBConvert.ChangeType(value, typeof(Binary));

            Assert.Equal(value, (DateTimeOffset)DBConvert.ChangeType(binary, typeof(DateTimeOffset)));
        }
    }

    [Database]
    public class Shop : DataContext
    {
        public Shop(string connection) : base(connection)
        {
        }

        public Table<Customer> Customers => GetTable<Customer>();

        public Table<Order> Orders => GetTable<Order>();

        public Table<Animal> Animals => GetTable<Animal>();

        [Function(Name = "dbo.CustomersInCity")]
        public ISingleResult<Customer> CustomersInCity([Parameter(DbType = "NVarChar(50)")] string city) =>
            (ISingleResult<Customer>)ExecuteMethodCall(this, (MethodInfo)MethodBase.GetCurrentMethod(), city).ReturnValue;

        [Function(Name = "dbo.OrderTotal", IsComposable = true)]
        [return: Parameter(DbType = "Decimal(18,2)")]
        public decimal? OrderTotal([Parameter(DbType = "Int")] int? customerId) =>
            (decimal?)ExecuteMethodCall(this, (MethodInfo)MethodBase.GetCurrentMethod(), customerId).ReturnValue;
    }

    [Table(Name = "Customers")]
    public class Customer
    {
        private readonly EntitySet<Order> _orders;

        public Customer()
        {
            _orders = new EntitySet<Order>(o => o.Customer = this, o => o.Customer = null);
        }

        [Column(IsPrimaryKey = true, IsDbGenerated = true, DbType = "Int NOT NULL IDENTITY", UpdateCheck = UpdateCheck.Never)]
        public int Id { get; set; }

        [Column(DbType = "NVarChar(50) NOT NULL", CanBeNull = false, UpdateCheck = UpdateCheck.Never)]
        public string Name { get; set; }

        [Column(DbType = "NVarChar(50)", UpdateCheck = UpdateCheck.Never)]
        public string City { get; set; }

        [Column(DbType = "VarBinary(100)", UpdateCheck = UpdateCheck.Never)]
        public Binary Photo { get; set; }

        [Column(IsVersion = true, IsDbGenerated = true, DbType = "rowversion NOT NULL", CanBeNull = false, AutoSync = AutoSync.Always)]
        public Binary Version { get; set; }

        [Association(Storage = "_orders", OtherKey = "CustomerId", ThisKey = "Id")]
        public EntitySet<Order> Orders
        {
            get => _orders;
            set => _orders.Assign(value);
        }
    }

    [Table(Name = "Orders")]
    public class Order
    {
        private EntityRef<Customer> _customer;
        private int _customerId;

        [Column(IsPrimaryKey = true, IsDbGenerated = true, DbType = "Int NOT NULL IDENTITY")]
        public int Id { get; set; }

        [Column(Storage = "_customerId", DbType = "Int NOT NULL")]
        public int CustomerId
        {
            get => _customerId;
            set => _customerId = value;
        }

        [Column(DbType = "Decimal(18,2) NOT NULL")]
        public decimal Amount { get; set; }

        [Column(DbType = "DateTime NOT NULL")]
        public DateTime OrderDate { get; set; }

        [Association(Storage = "_customer", ThisKey = "CustomerId", OtherKey = "Id", IsForeignKey = true)]
        public Customer Customer
        {
            get => _customer.Entity;
            set
            {
                Customer previous = _customer.Entity;
                if (previous == value && _customer.HasLoadedOrAssignedValue)
                    return;
                if (previous != null)
                {
                    _customer.Entity = null;
                    previous.Orders.Remove(this);
                }
                _customer.Entity = value;
                if (value != null)
                {
                    if (!value.Orders.Contains(this))
                        value.Orders.Add(this);
                    _customerId = value.Id;
                }
                else
                {
                    _customerId = default;
                }
            }
        }
    }

    public class PlainCustomer
    {
        public int Id { get; set; }

        public string Name { get; set; }
    }

    [Table(Name = "Animals")]
    [InheritanceMapping(Code = "Dog", Type = typeof(Dog), IsDefault = true)]
    [InheritanceMapping(Code = "Cat", Type = typeof(Cat))]
    public abstract class Animal
    {
        [Column(IsPrimaryKey = true, IsDbGenerated = true)]
        public int Id { get; set; }

        [Column(IsDiscriminator = true)]
        public string Kind { get; set; }

        [Column]
        public string Name { get; set; }
    }

    public class Dog : Animal
    {
    }

    public class Cat : Animal
    {
    }
}
