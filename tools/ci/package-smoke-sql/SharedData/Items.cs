using System.Data.SqlClient;

namespace SharedData
{
    public static class Items
    {
        public static int Count(string connectionString)
        {
            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand("SELECT COUNT(*) FROM Items", connection))
            {
                connection.Open();
                return (int)command.ExecuteScalar();
            }
        }
    }
}
