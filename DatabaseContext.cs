using Npgsql;
using TelegramMovieBot.Config;

namespace TelegramMovieBot.Database
{
    public class DatabaseContext
    {
        public NpgsqlConnection GetConnection()
        {
            return new NpgsqlConnection(BotConfig.ConnectionString);
        }
    }
}
