using System;
using Npgsql;
using TelegramMovieBot.Config;

namespace TelegramMovieBot.Database
{
    public class DatabaseContext
    {
        private static readonly string ConvertedConnectionString = Convert(BotConfig.ConnectionString);

        public NpgsqlConnection GetConnection()
        {
            return new NpgsqlConnection(ConvertedConnectionString);
        }

        private static string Convert(string uriString)
        {
            // اگه از قبل فرمت key=value بود (نه uri)، همون رو برگردون
            if (!uriString.StartsWith("postgres://") && !uriString.StartsWith("postgresql://"))
                return uriString;

            var uri = new Uri(uriString);
            var userInfo = uri.UserInfo.Split(':', 2);

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Username = userInfo[0],
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
                Database = uri.AbsolutePath.TrimStart('/'),
                SslMode = SslMode.Require,
                TrustServerCertificate = true
            };

            return builder.ConnectionString;
        }
    }
}
