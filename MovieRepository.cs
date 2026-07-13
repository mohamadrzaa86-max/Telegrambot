using Npgsql;
using TelegramMovieBot.Database;
using TelegramMovieBot.Models;

namespace TelegramMovieBot.Repositories
{
    public class MovieRepository
    {
        private readonly DatabaseContext db = new DatabaseContext();

        public void AddView(string movieCode)
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand(
                "UPDATE \"Movies\" SET \"Views\" = \"Views\" + 1 WHERE \"MovieCode\" = @MovieCode",
                connection);

            command.Parameters.AddWithValue("@MovieCode", movieCode);

            command.ExecuteNonQuery();
        }

        public List<Movie> GetTopMovies()
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand(
                "SELECT * FROM \"Movies\" ORDER BY \"Views\" DESC LIMIT 10",
                connection);

            using var reader = command.ExecuteReader();

            List<Movie> movies = new();

            while (reader.Read())
            {
                movies.Add(new Movie
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    MovieCode = reader["MovieCode"].ToString()!,
                    Title = reader["Title"].ToString()!,
                    FileId = reader["FileId"].ToString()!,
                    Description = reader["Description"].ToString()!,
                    PhotoFileId = reader["PhotoFileId"] == DBNull.Value ? null : reader["PhotoFileId"].ToString(),
                    Views = Convert.ToInt32(reader["Views"])
                });
            }

            return movies;
        }

        public List<Movie> GetAll()
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand("SELECT * FROM \"Movies\"", connection);

            using var reader = command.ExecuteReader();

            var list = new List<Movie>();

            while (reader.Read())
            {
                list.Add(new Movie
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    MovieCode = reader["MovieCode"].ToString()!,
                    Title = reader["Title"].ToString()!,
                    FileId = reader["FileId"].ToString()!,
                    Description = reader["Description"].ToString()!,
                    PhotoFileId = reader["PhotoFileId"] == DBNull.Value
                        ? null
                        : reader["PhotoFileId"].ToString()
                });
            }

            return list;
        }

        public void UpdateMovie(Movie movie)
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand(@"
UPDATE ""Movies""
SET ""Title"" = @Title,
    ""Description"" = @Description,
    ""FileId"" = @FileId,
    ""PhotoFileId"" = @PhotoFileId
WHERE ""MovieCode"" = @MovieCode", connection);

            command.Parameters.AddWithValue("@Title", movie.Title);
            command.Parameters.AddWithValue("@Description", (object?)movie.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("@FileId", movie.FileId);
            command.Parameters.AddWithValue("@PhotoFileId", (object?)movie.PhotoFileId ?? DBNull.Value);
            command.Parameters.AddWithValue("@MovieCode", movie.MovieCode);

            command.ExecuteNonQuery();
        }

        public bool DeleteMovie(string movieCode)
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand(
                "DELETE FROM \"Movies\" WHERE \"MovieCode\" = @MovieCode",
                connection);

            command.Parameters.AddWithValue("@MovieCode", movieCode);

            return command.ExecuteNonQuery() > 0;
        }

        public void AddMovie(Movie movie)
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand(@"
INSERT INTO ""Movies""
(""MovieCode"", ""Title"", ""FileId"", ""PhotoFileId"", ""Description"")
VALUES
(@MovieCode, @Title, @FileId, @PhotoFileId, @Description)", connection);

            command.Parameters.AddWithValue("@PhotoFileId", (object?)movie.PhotoFileId ?? DBNull.Value);
            command.Parameters.AddWithValue("@MovieCode", movie.MovieCode);
            command.Parameters.AddWithValue("@Title", movie.Title);
            command.Parameters.AddWithValue("@FileId", movie.FileId);
            command.Parameters.AddWithValue("@Description", (object?)movie.Description ?? DBNull.Value);

            command.ExecuteNonQuery();
        }

        public Movie? GetByCode(string movieCode)
        {
            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand(
                "SELECT * FROM \"Movies\" WHERE \"MovieCode\" = @MovieCode LIMIT 1",
                connection);

            command.Parameters.AddWithValue("@MovieCode", movieCode);

            using var reader = command.ExecuteReader();

            if (!reader.Read())
                return null;

            return new Movie
            {
                Id = Convert.ToInt32(reader["Id"]),
                MovieCode = reader["MovieCode"].ToString()!,
                Title = reader["Title"].ToString()!,
                FileId = reader["FileId"].ToString()!,
                Description = reader["Description"].ToString()!,
                Views = Convert.ToInt32(reader["Views"]),
                PhotoFileId = reader["PhotoFileId"] == DBNull.Value
                    ? null
                    : reader["PhotoFileId"].ToString(),
            };
        }

        public List<Movie> SearchByTitle(string title)
        {
            List<Movie> movies = new();

            using var connection = db.GetConnection();
            connection.Open();

            var command = new NpgsqlCommand(
                "SELECT * FROM \"Movies\" WHERE \"Title\" ILIKE @Title",
                connection);

            command.Parameters.AddWithValue("@Title", "%" + title + "%");

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                movies.Add(new Movie
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    MovieCode = reader["MovieCode"].ToString()!,
                    Title = reader["Title"].ToString()!,
                    FileId = reader["FileId"].ToString()!,
                    Description = reader["Description"].ToString()!
                });
            }

            return movies;
        }
    }
}
