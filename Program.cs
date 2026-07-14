using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramMovieBot.Config;
using TelegramMovieBot.Models;
using TelegramMovieBot.Repositories;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

AdminState? adminState = null;

var botClient = new TelegramBotClient(BotConfig.Token);
var userRepo = new UserRepository();
var movieRepo = new MovieRepository();

app.MapPost("/webhook", async (HttpContext context) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();

        var update = JsonSerializer.Deserialize<Update>(body);

        if (update != null)
        {
            await HandleUpdate(update);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Webhook error: " + ex.Message);
    }

    return Results.Ok();
});

app.MapGet("/", () => "Bot is running!");

app.MapGet("/set-webhook", async () =>
{
    var webhookUrl = Environment.GetEnvironmentVariable("WEBHOOK_URL");

    if (string.IsNullOrEmpty(webhookUrl))
        return Results.BadRequest("WEBHOOK_URL تنظیم نشده است");

    await botClient.SetWebhook(webhookUrl);
    return Results.Ok($"Webhook set to: {webhookUrl}");
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Run($"http://0.0.0.0:{port}");


async Task HandleUpdate(Update update)
{
    Console.WriteLine(update.Type);

    if (update.CallbackQuery != null)
    {
        var data = update.CallbackQuery.Data!;
        var chatId = update.CallbackQuery.Message!.Chat.Id;

        if (data.StartsWith("edit_") && adminState?.EditingMovie == null)
        {
            await botClient.AnswerCallbackQuery(update.CallbackQuery.Id);
            return;
        }

        if (data == "edit_title")
        {
            adminState!.Mode = "title";
            await botClient.SendMessage(chatId, "📝 عنوان جدید را ارسال کنید:");
            await botClient.AnswerCallbackQuery(update.CallbackQuery.Id);
            return;
        }
        else if (data == "edit_desc")
        {
            adminState!.Mode = "desc";
            await botClient.SendMessage(chatId, "📄 توضیحات جدید را ارسال کنید:");
            await botClient.AnswerCallbackQuery(update.CallbackQuery.Id);
            return;
        }
        else if (data == "edit_photo")
        {
            adminState!.Mode = "photo";
            await botClient.SendMessage(chatId, "🖼 کاور جدید را ارسال کنید:");
            await botClient.AnswerCallbackQuery(update.CallbackQuery.Id);
            return;
        }
        else if (data == "edit_video")
        {
            adminState!.Mode = "video";
            await botClient.SendMessage(chatId, "🎥 فیلم جدید را ارسال کنید:");
            await botClient.AnswerCallbackQuery(update.CallbackQuery.Id);
            return;
        }

        if (data.StartsWith("movie_"))
        {
            var code = data.Replace("movie_", "");
            var movie = movieRepo.GetByCode(code);

            if (movie == null)
            {
                await botClient.AnswerCallbackQuery(update.CallbackQuery.Id, "فیلم پیدا نشد");
                return;
            }

            if (!string.IsNullOrEmpty(movie.PhotoFileId))
            {
                await botClient.SendPhoto(chatId: chatId, photo: movie.PhotoFileId, caption: movie.Title);
            }

            await botClient.SendVideo(
                chatId: chatId,
                video: movie.FileId,
                caption: $"{movie.Title}\n\n{movie.Description}"
            );

            await botClient.AnswerCallbackQuery(update.CallbackQuery.Id);
            return;
        }

        await botClient.AnswerCallbackQuery(update.CallbackQuery.Id);
        return;
    }

    var message = update.Message;
    if (message == null)
        return;

    var text = message.Text;

    if (text == "📊 آمار" && message.From!.Id == BotConfig.AdminId)
    {
        var userCount = userRepo.GetAll().Count();
        var movieCount = movieRepo.GetAll().Count();

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: $"📊 آمار ربات:\n\n👤 کاربران: {userCount}\n🎬 فیلم‌ها: {movieCount}"
        );
        return;
    }

    if (message.Video != null && message.From!.Id == BotConfig.AdminId)
    {
        adminState = new AdminState { FileId = message.Video.FileId };
        await botClient.SendMessage(chatId: message.Chat.Id, text: "🖼️ حالا عکس کاور فیلم رو بفرست");
        return;
    }

    if (message.Photo != null &&
        message.From!.Id == BotConfig.AdminId &&
        adminState != null &&
        adminState.FileId != null &&
        adminState.PhotoFileId == null)
    {
        adminState.PhotoFileId = message.Photo.Last().FileId;
        await botClient.SendMessage(chatId: message.Chat.Id, text: "✍️ حالا اسم فیلم را بفرست.");
        return;
    }

    if (text == null) return;

    if (adminState != null && adminState.PhotoFileId != null && adminState.Title == null)
    {
        adminState.Title = text;
        await botClient.SendMessage(chatId: message.Chat.Id, text: "📝 حالا توضیحات رو بفرست");
        return;
    }

    if (adminState != null &&
        adminState.PhotoFileId != null &&
        adminState.Title != null &&
        adminState.Description == null)
    {
        adminState.Description = text;

        var movie = new Movie
        {
            MovieCode = Guid.NewGuid().ToString("N")[..8],
            Title = adminState.Title,
            Description = adminState.Description,
            FileId = adminState.FileId!,
            PhotoFileId = adminState.PhotoFileId
        };

        movieRepo.AddMovie(movie);

        await botClient.SendMessage(chatId: message.Chat.Id, text: $"✅ ذخیره شد\nکد: {movie.MovieCode}");
        adminState = null;
        return;
    }

    if (!text.StartsWith("/start"))
    {
        var movies = movieRepo.SearchByTitle(text);

        if (movies.Count > 0)
        {
            List<InlineKeyboardButton[]> buttons = new();

            foreach (var movie in movies)
            {
                buttons.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData(movie.Title, "movie_" + movie.MovieCode)
                });
            }

            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "🎬 فیلم‌های پیدا شده:",
                replyMarkup: new InlineKeyboardMarkup(buttons)
            );
            return;
        }
    }

    if (text == "/panel" && message.From!.Id == BotConfig.AdminId)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "🎬 افزودن فیلم", "✏️ ویرایش فیلم" },
            new KeyboardButton[] { "🗑 حذف فیلم", "📊 آمار" },
            new KeyboardButton[] { "📢 پیام همگانی" },
            new KeyboardButton[] { "🔥 پربازدیدترین فیلم‌ها" }
        })
        { ResizeKeyboard = true };

        await botClient.SendMessage(chatId: message.Chat.Id, text: "👑 پنل مدیریت", replyMarkup: keyboard);
        return;
    }

    if (text == "🗑 حذف فیلم" && message.From!.Id == BotConfig.AdminId)
    {
        adminState = new AdminState { WaitingForDeleteCode = true };
        await botClient.SendMessage(chatId: message.Chat.Id, text: "🎬 کد فیلم را ارسال کنید:");
        return;
    }

    if (adminState != null && adminState.WaitingForDeleteCode && message.From!.Id == BotConfig.AdminId)
    {
        bool deleted = movieRepo.DeleteMovie(text);

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: deleted ? "✅ فیلم با موفقیت حذف شد." : "❌ فیلمی با این کد پیدا نشد."
        );

        adminState = null;
        return;
    }

    if (text == "✏️ ویرایش فیلم" && message.From!.Id == BotConfig.AdminId)
    {
        adminState = new AdminState { WaitingForEditCode = true };
        await botClient.SendMessage(chatId: message.Chat.Id, text: "🎬 کد فیلم را ارسال کنید.");
        return;
    }

    if (adminState != null && adminState.WaitingForEditCode && message.From!.Id == BotConfig.AdminId)
    {
        var movie = movieRepo.GetByCode(text);

        if (movie == null)
        {
            await botClient.SendMessage(chatId: message.Chat.Id, text: "❌ فیلمی با این کد پیدا نشد.");
            return;
        }

        adminState.WaitingForEditCode = false;
        adminState.EditingMovie = movie;

        var editKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📝 عنوان", "edit_title"),
                InlineKeyboardButton.WithCallbackData("📄 توضیحات", "edit_desc")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🖼 کاور", "edit_photo"),
                InlineKeyboardButton.WithCallbackData("🎥 فیلم", "edit_video")
            }
        });

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "چه چیزی را می‌خواهید ویرایش کنید؟",
            replyMarkup: editKeyboard);
        return;
    }

    if (adminState?.EditingMovie != null && adminState.Mode != null)
    {
        var movie = adminState.EditingMovie;

        if (adminState.Mode == "title") movie.Title = text;
        else if (adminState.Mode == "desc") movie.Description = text;
        else if (adminState.Mode == "photo") movie.PhotoFileId = message.Photo?.Last().FileId;
        else if (adminState.Mode == "video") movie.FileId = message.Video?.FileId ?? movie.FileId;

        movieRepo.UpdateMovie(movie);

        await botClient.SendMessage(chatId: message.Chat.Id, text: "✅ ویرایش با موفقیت انجام شد");
        adminState = null;
        return;
    }

    if (text == "📢 پیام همگانی" && message.From!.Id == BotConfig.AdminId)
    {
        adminState = new AdminState { WaitingForBroadcast = true };
        await botClient.SendMessage(chatId: message.Chat.Id, text: "📢 پیام خود را ارسال کنید (متن / عکس / فیلم)");
        return;
    }

    if (adminState?.WaitingForBroadcast == true && message.From!.Id == BotConfig.AdminId)
    {
        var users = userRepo.GetAllTelegramIds();
        int success = 0;

        foreach (var userId in users)
        {
            try
            {
                if (message.Type == MessageType.Photo)
                {
                    await botClient.SendPhoto(chatId: userId, photo: message.Photo!.Last().FileId, caption: message.Caption ?? "");
                }
                else if (message.Type == MessageType.Video)
                {
                    await botClient.SendVideo(chatId: userId, video: message.Video!.FileId, caption: message.Caption ?? "");
                }
                else if (message.Type == MessageType.Text)
                {
                    await botClient.SendMessage(chatId: userId, text: message.Text!);
                }

                success++;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        await botClient.SendMessage(chatId: message.Chat.Id, text: $"✅ ارسال شد\n👤 موفق: {success}");
        adminState = null;
        return;
    }

    if (text == "🎬 افزودن فیلم" && message.From!.Id == BotConfig.AdminId)
    {
        adminState = new AdminState { WaitingForMovie = true };
        await botClient.SendMessage(chatId: message.Chat.Id, text: "📥 فیلم را ارسال کنید");
        return;
    }

    if (adminState?.WaitingForMovie == true && message.Video != null)
    {
        adminState.FileId = message.Video.FileId;
        adminState.WaitingForMovie = false;
        adminState.WaitingForTitle = true;
        await botClient.SendMessage(chatId: message.Chat.Id, text: "✍️ اسم فیلم را بفرست");
        return;
    }

    if (adminState?.WaitingForTitle == true && !string.IsNullOrEmpty(text))
    {
        adminState.Title = text;
        adminState.WaitingForTitle = false;
        adminState.WaitingForDescription = true;
        await botClient.SendMessage(chatId: message.Chat.Id, text: "📝 توضیحات را بفرست");
        return;
    }

    if (adminState?.WaitingForDescription == true && !string.IsNullOrEmpty(text))
    {
        adminState.Description = text;

        var movie = new Movie
        {
            MovieCode = Guid.NewGuid().ToString("N")[..8],
            FileId = adminState.FileId!,
            Title = adminState.Title!,
            Description = adminState.Description
        };

        movieRepo.AddMovie(movie);

        await botClient.SendMessage(chatId: message.Chat.Id, text: $"✅ ذخیره شد\nکد: {movie.MovieCode}");
        adminState = null;
        return;
    }

    if (text == "🔥 پربازدیدترین فیلم‌ها" && message.From!.Id == BotConfig.AdminId)
    {
        var movies = movieRepo.GetTopMovies();

        if (movies.Count == 0)
        {
            await botClient.SendMessage(message.Chat.Id, "هنوز هیچ فیلمی وجود ندارد.");
            return;
        }

        string result = "🔥 ۱۰ فیلم پربازدید:\n\n";
        foreach (var movie in movies)
        {
            result += $"🎬 {movie.Title}\n👁 {movie.Views} بازدید\n\n";
        }

        await botClient.SendMessage(message.Chat.Id, result);
        return;
    }

    if (text == "/start")
    {
        var user = new TelegramMovieBot.Models.User
        {
            TelegramId = message.From!.Id,
            Username = message.From.Username,
            FirstName = message.From.FirstName
        };

        if (!userRepo.Exists(user.TelegramId))
        {
            userRepo.AddUser(user);
            await botClient.SendMessage(chatId: message.Chat.Id, text: "🎬 خوش اومدی! ربات فعال شد");
        }
        else
        {
            await botClient.SendMessage(chatId: message.Chat.Id, text: "👋 خوش برگشتی!");
        }

        return;
    }

    if (text.StartsWith("/start "))
    {
        if (!await IsUserJoined(message.From!.Id))
        {
            var joinKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithUrl("📢 عضویت در کانال", BotConfig.ChannelLink) }
            });

            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "❌ برای دریافت فیلم ابتدا در کانال عضو شوید.",
                replyMarkup: joinKeyboard);
            return;
        }

        var code = text.Replace("/start ", "").Trim();
        var movie = movieRepo.GetByCode(code);

        if (movie == null)
        {
            await botClient.SendMessage(chatId: message.Chat.Id, text: "❌ فیلم پیدا نشد");
            return;
        }

        if (!string.IsNullOrEmpty(movie.PhotoFileId))
        {
            await botClient.SendPhoto(chatId: message.Chat.Id, photo: movie.PhotoFileId, caption: movie.Title);
        }

        var sentMessage = await botClient.SendVideo(
            chatId: message.Chat.Id,
            video: movie.FileId,
            caption: $"{movie.Title}\n\n{movie.Description}\n\n👁 تعداد بازدید: {movie.Views}"
        );

        movieRepo.AddView(movie.MovieCode);

        _ = Task.Run(async () =>
        {
            await Task.Delay(30000);
            try
            {
                await botClient.DeleteMessage(chatId: message.Chat.Id, messageId: sentMessage.MessageId);
            }
            catch { }
        });

        return;
    }
}

async Task<bool> IsUserJoined(long userId)
{
    try
    {
        var member = await botClient.GetChatMember(BotConfig.ChannelUsername, userId);
        return member.Status == ChatMemberStatus.Member
            || member.Status == ChatMemberStatus.Administrator
            || member.Status == ChatMemberStatus.Creator;
    }
    catch
    {
        return false;
    }
}
