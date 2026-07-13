using System;

namespace TelegramMovieBot.Config
{
    public static class BotConfig
    {
        public static string Token =
            Environment.GetEnvironmentVariable("BOT_TOKEN")
            ?? throw new Exception("BOT_TOKEN تنظیم نشده است");

        public static long AdminId =
            long.Parse(Environment.GetEnvironmentVariable("ADMIN_ID") ?? "0");

        public static string ConnectionString =
            Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? throw new Exception("DATABASE_URL تنظیم نشده است");

        public const string ChannelUsername = "@mo13861386mo";
        public const string ChannelLink = "https://t.me/mo13861386mo";
    }
}
