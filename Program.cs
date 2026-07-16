using System.Text;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramMovieBot.Config;
using TelegramMovieBot.Models;
using TelegramMovieBot.Repositories;

Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var adminStates = new Dictionary<long, AdminState>();

var botClient = new TelegramBotClient(BotConfig.Token);
var userRepo = new UserRepository();
var movieRepo = new MovieRepository();
var channelRepo = new ChannelRepository();

var me = await botClient.GetMe();
var botUsername = me.Username!;

app.MapPost("/webhook", async (HttpContext context) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();

        var update = JsonSerializer.Deserialize<Update>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (update != null)
        {
            await UpdateHandler(update);
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


async Task UpdateHandler(Update update)
{
    if (update.CallbackQuery != null)
    {
        await HandleCallbackQuery(update.CallbackQuery);
        return;
    }

    var message = update.Message;
    if (message == null)
        return;

    var userId = message.From!.Id;
    adminStates.TryGetValue(userId, out var adminState);
    var isAdmin = userId == BotConfig.AdminId;

    var text = message.Text;

    if (text == "📊 آمار" && isAdmin)
    {
        var userCount = userRepo.GetAll().Count();
        var movieCount = movieRepo.GetAll().Count();

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: $"📊 آمار ربات:\n\n👤 کاربران: {userCount}\n🎬 فیلم‌ها: {movieCount}"
        );

        return;
    }

    if (text == "📢 پیام همگانی" && isAdmin)
    {
        adminState = new AdminState { WaitingForBroadcast = true };
        adminStates[userId] = adminState;

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "📢 پیام خود را ارسال کنید (متن / عکس / فیلم؛ اگر دکمه‌ی زیر پیام داشته باشد هم حفظ می‌شود)"
        );

        return;
    }

    // ===== رفع باگ ۱: نگه‌داشتن دکمه‌ی شیشه‌ای هنگام پیام همگانی =====
    if (isAdmin && adminState?.WaitingForBroadcast == true)
    {
        var users = userRepo.GetAllTelegramIds();
        int success = 0;
        var keyboard = message.ReplyMarkup;

        foreach (var recipientId in users)
        {
            try
            {
                if (message.Type == MessageType.Photo)
                {
                    await botClient.SendPhoto(chatId: recipientId, photo: message.Photo!.Last().FileId, caption: message.Caption ?? "", replyMarkup: keyboard);
                }
                else if (message.Type == MessageType.Video)
                {
                    await botClient.SendVideo(chatId: recipientId, video: message.Video!.FileId, caption: message.Caption ?? "", replyMarkup: keyboard);
                }
                else if (message.Type == MessageType.Text)
                {
                    await botClient.SendMessage(chatId: recipientId, text: message.Text!, replyMarkup: keyboard);
                }

                success++;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        await botClient.SendMessage(chatId: message.Chat.Id, text: $"✅ ارسال شد\n👤 موفق: {success}");
        adminStates.Remove(userId);
        return;
    }

    // ===== فلوی افزودن کانال جدید =====
    if (isAdmin && adminState?.WaitingForChannelUsername == true && text != null)
    {
        var username = text.Trim();

        if (!username.StartsWith("@"))
        {
            await botClient.SendMessage(message.Chat.Id, "❌ آیدی کانال باید با @ شروع شود. دوباره ارسال کنید:");
            return;
        }

        channelRepo.Add(username);
        adminStates.Remove(userId);

        await botClient.SendMessage(message.Chat.Id, $"✅ کانال {username} اضافه شد.\n\n⚠️ یادت نره ربات رو ادمین همون کانال کنی، وگرنه نمی‌تونه عضویت رو چک کنه.");
        await ShowChannelManagement(message.Chat.Id);
        return;
    }

    if (isAdmin && adminState?.EditingMovie != null && adminState.Mode != null)
    {
        var movie = adminState.EditingMovie;
        bool valid = true;

        switch (adminState.Mode)
        {
            case "title":
                if (string.IsNullOrWhiteSpace(text)) { valid = false; break; }
                movie.Title = text;
                break;

            case "desc":
                if (string.IsNullOrWhiteSpace(text)) { valid = false; break; }
                movie.Description = text;
                break;

            case "photo":
                if (message.Photo == null) { valid = false; break; }
                movie.PhotoFileId = message.Photo.Last().FileId;
                break;

            case "video":
                if (message.Video == null) { valid = false; break; }
                movie.FileId = message.Video.FileId;
                break;
        }

        if (!valid)
        {
            await botClient.SendMessage(chatId: message.Chat.Id, text: "❌ نوع پیام مناسب نبود، لطفاً دوباره ارسال کن.");
            return;
        }

        movieRepo.UpdateMovie(movie);

        await botClient.SendMessage(chatId: message.Chat.Id, text: "✅ ویرایش با موفقیت انجام شد");
        adminStates.Remove(userId);
        return;
    }

    if (message.Video != null && isAdmin)
    {
        adminState = new AdminState { FileId = message.Video.FileId };
        adminStates[userId] = adminState;

        await botClient.SendMessage(chatId: message.Chat.Id, text: "🖼️ حالا عکس کاور فیلم رو بفرست");
        return;
    }

    if (message.Photo != null &&
        isAdmin &&
        adminState != null &&
        adminState.FileId != null &&
        adminState.PhotoFileId == null)
    {
        adminState.PhotoFileId = message.Photo.Last().FileId;

        await botClient.SendMessage(chatId: message.Chat.Id, text: "✍️ حالا اسم فیلم را بفرست.");
        return;
    }

    if (text == null)
        return;

    if (isAdmin && adminState != null && adminState.PhotoFileId != null && adminState.Title == null)
    {
        adminState.Title = text;

        await botClient.SendMessage(chatId: message.Chat.Id, text: "📝 حالا توضیحات رو بفرست");
        return;
    }

    // ===== تغییر ۲: دیگه پست به‌صورت خودکار به کانال ارسال نمی‌شود؛ فقط برای خود ادمین =====
    if (isAdmin &&
        adminState != null &&
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

        var caption = $"{movie.Title}\n\n{movie.Description}";

        var postKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithUrl("🎬 دریافت فیلم", $"https://t.me/{botUsername}?start={movie.MovieCode}")
            }
        });

        await botClient.SendPhoto(
            chatId: message.Chat.Id,
            photo: movie.PhotoFileId,
            caption: caption,
            replyMarkup: postKeyboard
        );

        adminStates.Remove(userId);
        return;
    }

    if (!text.StartsWith("/start") && isAdmin)
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

    if (text == "/panel" && isAdmin)
    {
        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "🎬 افزودن فیلم", "✏️ ویرایش فیلم" },
            new KeyboardButton[] { "🗑 حذف فیلم", "📊 آمار" },
            new KeyboardButton[] { "📢 پیام همگانی" },
            new KeyboardButton[] { "🔥 پربازدیدترین فیلم‌ها", "📋 لیست فیلم‌ها" },
            new KeyboardButton[] { "📡 مدیریت کانال‌ها" }
        })
        { ResizeKeyboard = true };

        await botClient.SendMessage(chatId: message.Chat.Id, text: "👑 پنل مدیریت", replyMarkup: keyboard);
        return;
    }

    // ===== تغییر ۴: مدیریت کانال‌ها از داخل ربات =====
    if (text == "📡 مدیریت کانال‌ها" && isAdmin)
    {
        await ShowChannelManagement(message.Chat.Id);
        return;
    }

    if (text == "🗑 حذف فیلم" && isAdmin)
    {
        adminState = new AdminState { WaitingForDeleteCode = true };
        adminStates[userId] = adminState;

        await botClient.SendMessage(chatId: message.Chat.Id, text: "🎬 کد فیلم را ارسال کنید:");
        return;
    }

    if (isAdmin && adminState != null && adminState.WaitingForDeleteCode)
    {
        bool deleted = movieRepo.DeleteMovie(text.Trim());

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: deleted ? "✅ فیلم با موفقیت حذف شد." : "❌ فیلمی با این کد پیدا نشد."
        );

        adminStates.Remove(userId);
        return;
    }

    if (text == "✏️ ویرایش فیلم" && isAdmin)
    {
        adminState = new AdminState { WaitingForEditCode = true };
        adminStates[userId] = adminState;

        await botClient.SendMessage(chatId: message.Chat.Id, text: "🎬 کد فیلم را ارسال کنید.");
        return;
    }

    if (isAdmin && adminState != null && adminState.WaitingForEditCode)
    {
        var movie = movieRepo.GetByCode(text.Trim());

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

    if (text == "🎬 افزودن فیلم" && isAdmin)
    {
        await botClient.SendMessage(chatId: message.Chat.Id, text: "📥 فیلم را ارسال کنید");
        return;
    }

    if (text == "🔥 پربازدیدترین فیلم‌ها" && isAdmin)
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

    if (text == "📋 لیست فیلم‌ها" && isAdmin)
    {
        var movies = movieRepo.GetAll();

        if (movies.Count == 0)
        {
            await botClient.SendMessage(message.Chat.Id, "هنوز هیچ فیلمی وجود ندارد.");
            return;
        }

        const int chunkLimit = 3500;
        var chunk = new StringBuilder($"📋 لیست فیلم‌ها ({movies.Count} مورد):\n\n");

        foreach (var movie in movies)
        {
            var line = $"🎬 {movie.Title}\nکد: {movie.MovieCode}\n\n";

            if (chunk.Length + line.Length > chunkLimit)
            {
                await botClient.SendMessage(message.Chat.Id, chunk.ToString());
                chunk.Clear();
            }

            chunk.Append(line);
        }

        if (chunk.Length > 0)
        {
            await botClient.SendMessage(message.Chat.Id, chunk.ToString());
        }

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

    // ===== تغییر ۴: چک عضویت روی لیست داینامیک کانال‌ها =====
    if (text.StartsWith("/start "))
    {
        var code = text.Replace("/start ", "").Trim();
        var notJoined = await GetNotJoinedChannels(message.From!.Id);

        if (notJoined.Count > 0)
        {
            await SendJoinPrompt(message.Chat.Id, code, notJoined);
            return;
        }

        await DeliverMovie(message.Chat.Id, code);
        return;
    }
}

// =======================
// نمایش پنل مدیریت کانال‌ها
// =======================
async Task ShowChannelManagement(long chatId)
{
    var channels = channelRepo.GetAll();
    var rows = new List<InlineKeyboardButton[]>();

    foreach (var channel in channels)
    {
        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData($"❌ {channel.Username}", "delchannel_" + channel.Id)
        });
    }

    rows.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ افزودن کانال", "addchannel") });

    var text = channels.Count == 0
        ? "هیچ کانالی برای عضویت اجباری تنظیم نشده.\nبرای افزودن، دکمه‌ی زیر را بزنید."
        : "📡 کانال‌های موردنیاز برای عضویت:\n(برای حذف، روی کانال بزنید)";

    await botClient.SendMessage(chatId, text, replyMarkup: new InlineKeyboardMarkup(rows));
}

// =======================
// چک عضویت در همه‌ی کانال‌های ثبت‌شده
// =======================
async Task<List<string>> GetNotJoinedChannels(long userId)
{
    var notJoined = new List<string>();
    var channels = channelRepo.GetAll();

    foreach (var channel in channels)
    {
        try
        {
            var member = await botClient.GetChatMember(channel.Username, userId);
            bool joined = member.Status == ChatMemberStatus.Member
                || member.Status == ChatMemberStatus.Administrator
                || member.Status == ChatMemberStatus.Creator;

            if (!joined)
                notJoined.Add(channel.Username);
        }
        catch
        {
            notJoined.Add(channel.Username);
        }
    }

    return notJoined;
}

async Task SendJoinPrompt(long chatId, string code, List<string> notJoinedChannels)
{
    var rows = new List<InlineKeyboardButton[]>();

    foreach (var ch in notJoinedChannels)
    {
        rows.Add(new[]
        {
            InlineKeyboardButton.WithUrl($"📢 عضویت در {ch}", $"https://t.me/{(ch.StartsWith("@") ? ch.Substring(1) : ch)}")
        });
    }

    rows.Add(new[] { InlineKeyboardButton.WithCallbackData("✅ عضو شدم", "check_join_" + code) });

    await botClient.SendMessage(
        chatId: chatId,
        text: "❌ برای دریافت فیلم، ابتدا در کانال‌های زیر عضو شوید:",
        replyMarkup: new InlineKeyboardMarkup(rows));
}

// =======================
// ارسال فیلم + پیام «دریافت مجدد» (تغییر ۳)
// =======================
async Task<bool> DeliverMovie(long chatId, string code)
{
    var movie = movieRepo.GetByCode(code);

    if (movie == null)
    {
        await botClient.SendMessage(chatId, "❌ فیلم پیدا نشد");
        return false;
    }

    var sentMessage = await botClient.SendVideo(
        chatId: chatId,
        video: movie.FileId,
        caption: $"{movie.Title}\n\n{movie.Description}\n\n👁 تعداد بازدید: {movie.Views}"
    );

    movieRepo.AddView(movie.MovieCode);

    var resendKeyboard = new InlineKeyboardMarkup(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("🔁 دریافت مجدد", "resend_" + movie.MovieCode) }
    });

    await botClient.SendMessage(
        chatId: chatId,
        text: "⏳ کاربر عزیز، این فیلم بعد از ۳۰ ثانیه پاک می‌شود.\nاین فیلم را در Saved Messages ذخیره کنید.",
        replyMarkup: resendKeyboard
    );

    _ = Task.Run(async () =>
    {
        await Task.Delay(30000);
        try
        {
            await botClient.DeleteMessage(chatId, sentMessage.MessageId);
        }
        catch { }
    });

    return true;
}

async Task HandleCallbackQuery(CallbackQuery callbackQuery)
{
    var data = callbackQuery.Data;
    var chatId = callbackQuery.Message?.Chat.Id;

    if (data == null || chatId == null)
    {
        await botClient.AnswerCallbackQuery(callbackQuery.Id);
        return;
    }

    var userId = callbackQuery.From.Id;
    var isAdmin = userId == BotConfig.AdminId;
    adminStates.TryGetValue(userId, out var adminState);

    if (data.StartsWith("check_join_"))
    {
        var code = data.Replace("check_join_", "");
        var notJoined = await GetNotJoinedChannels(userId);

        if (notJoined.Count > 0)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "❌ هنوز عضو همه‌ی کانال‌ها نشدی!", showAlert: true);
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, "✅ عضویت تایید شد");

        try
        {
            await botClient.DeleteMessage(chatId.Value, callbackQuery.Message!.MessageId);
        }
        catch { }

        await DeliverMovie(chatId.Value, code);
        return;
    }

    if (data.StartsWith("resend_"))
    {
        var code = data.Replace("resend_", "");
        await botClient.AnswerCallbackQuery(callbackQuery.Id);
        await DeliverMovie(chatId.Value, code);
        return;
    }

    if (data == "addchannel")
    {
        if (!isAdmin)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id);
            return;
        }

        adminStates[userId] = new AdminState { WaitingForChannelUsername = true };

        await botClient.SendMessage(chatId.Value, "🆔 آیدی کانال را با @ ارسال کنید (مثال: @mychannel)\n\n⚠️ ربات باید ادمین همان کانال باشد وگرنه نمی‌تواند عضویت را چک کند.");
        await botClient.AnswerCallbackQuery(callbackQuery.Id);
        return;
    }

    if (data.StartsWith("delchannel_"))
    {
        if (!isAdmin)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id);
            return;
        }

        if (int.TryParse(data.Replace("delchannel_", ""), out var channelId))
        {
            channelRepo.Delete(channelId);
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "✅ حذف شد");
            await ShowChannelManagement(chatId.Value);
        }
        else
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id);
        }

        return;
    }

    if (data.StartsWith("edit_"))
    {
        if (!isAdmin || adminState?.EditingMovie == null)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id);
            return;
        }

        switch (data)
        {
            case "edit_title":
                adminState.Mode = "title";
                await botClient.SendMessage(chatId.Value, "📝 عنوان جدید را ارسال کنید:");
                break;

            case "edit_desc":
                adminState.Mode = "desc";
                await botClient.SendMessage(chatId.Value, "📄 توضیحات جدید را ارسال کنید:");
                break;

            case "edit_photo":
                adminState.Mode = "photo";
                await botClient.SendMessage(chatId.Value, "🖼 کاور جدید را ارسال کنید:");
                break;

            case "edit_video":
                adminState.Mode = "video";
                await botClient.SendMessage(chatId.Value, "🎥 فیلم جدید را ارسال کنید:");
                break;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id);
        return;
    }

    if (data.StartsWith("movie_"))
    {
        var code = data.Replace("movie_", "");
        var movie = movieRepo.GetByCode(code);

        if (movie == null)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, "فیلم پیدا نشد");
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id);
        await DeliverMovie(chatId.Value, movie.MovieCode);
        return;
    }

    await botClient.AnswerCallbackQuery(callbackQuery.Id);
}
