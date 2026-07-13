using Npgsql;
using TelegramMovieBot.Database;
using TelegramMovieBot.Models;

namespace TelegramMovieBot.Repositories
{
    public class UserRepository
    {
        private readonly DatabaseContext db = new DatabaseContext();

        public List<long> GetAllTelegramIds()
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand("SELECT \"TelegramId\" FROM \"Users\"", connection);

            using var reader = command.ExecuteReader();

            var list = new List<long>();

            while (reader.Read())
            {
                list.Add(Convert.ToInt64(reader["TelegramId"]));
            }

            return list;
        }

        public List<User> GetAll()
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand("SELECT * FROM \"Users\"", connection);

            using var reader = command.ExecuteReader();

            var list = new List<User>();

            while (reader.Read())
            {
                list.Add(new User
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    TelegramId = Convert.ToInt64(reader["TelegramId"]),
                    Username = reader["Username"] == DBNull.Value ? null : reader["Username"].ToString(),
                    FirstName = reader["FirstName"] == DBNull.Value ? null : reader["FirstName"].ToString()
                });
            }

            return list;
        }

        public void AddUser(User user)
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand(@"
INSERT INTO ""Users"" (""TelegramId"", ""Username"", ""FirstName"")
SELECT @TelegramId, @Username, @FirstName
WHERE NOT EXISTS (
    SELECT 1 FROM ""Users"" WHERE ""TelegramId"" = @TelegramId
)", connection);

            command.Parameters.AddWithValue("@TelegramId", user.TelegramId);
            command.Parameters.AddWithValue("@Username", (object?)user.Username ?? DBNull.Value);
            command.Parameters.AddWithValue("@FirstName", (object?)user.FirstName ?? DBNull.Value);

            command.ExecuteNonQuery();
        }

        public bool Exists(long telegramId)
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand(
                "SELECT COUNT(1) FROM \"Users\" WHERE \"TelegramId\" = @TelegramId",
                connection
            );

            command.Parameters.AddWithValue("@TelegramId", telegramId);

            long count = (long)command.ExecuteScalar()!;

            return count > 0;
        }
    }
}
