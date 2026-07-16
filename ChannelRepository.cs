using Npgsql;
using TelegramMovieBot.Database;

namespace TelegramMovieBot.Repositories
{
    public class ChannelRepository
    {
        private readonly DatabaseContext db = new DatabaseContext();

        public List<(int Id, string Username)> GetAll()
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand("SELECT * FROM \"Channels\"", connection);
            using var reader = command.ExecuteReader();

            var list = new List<(int, string)>();

            while (reader.Read())
            {
                list.Add((
                    Convert.ToInt32(reader["Id"]),
                    reader["Username"].ToString()!
                ));
            }

            return list;
        }

        public void Add(string username)
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand(
                "INSERT INTO \"Channels\" (\"Username\") VALUES (@Username) ON CONFLICT (\"Username\") DO NOTHING",
                connection);

            command.Parameters.AddWithValue("@Username", username);
            command.ExecuteNonQuery();
        }

        public void Delete(int id)
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand(
                "DELETE FROM \"Channels\" WHERE \"Id\" = @Id",
                connection);

            command.Parameters.AddWithValue("@Id", id);
            command.ExecuteNonQuery();
        }
    }
}
