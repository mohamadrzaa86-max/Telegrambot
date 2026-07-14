using System.Text;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramMovieBot.Config;
using TelegramMovieBot.Models;
using TelegramMovieBot.Repositories;


var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
// وضعیت ادمین حالا per-user است (بر اساس Id کاربر) نه یک متغیر global مشترک،
// تا در صورت پردازش هم‌زمان چند آپدیت یا وجود چند ادمین، وضعیت‌ها قاطی نشوند.
var adminStates = new Dictionary<long, AdminState>();

var botClient = new TelegramBotClient(BotConfig.Token);

var userRepo = new UserRepository();
var movieRepo = new MovieRepository();

// یوزرنیم ربات برای ساختن لینک دریافت فیلم (t.me/<username>?start=<code>) لازم است.
var me = await botClient.GetMe();
var botUsername = me.Username!;

using var cts = new CancellationTokenSource();

var receiverOptions = new ReceiverOptions
{
    AllowedUpdates = Array.Empty<UpdateType>()
};

botClient.StartReceiving(
    UpdateHandler,
    ErrorHandler,
    receiverOptions,
    cts.Token
);

Console.WriteLine("Bot is running...");
Console.ReadLine();

app.MapPost("/webhook", async (HttpContext context) =>
{
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();

        Console.WriteLine("RAW BODY: " + body);

        var update = JsonSerializer.Deserialize<Update>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (update != null)
        {
            Console.WriteLine("Parsed Message is null? " + (update.Message == null));
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

// =======================
// Update Handler
// =======================
async Task UpdateHandler(ITelegramBotClient botClient, Update update, CancellationToken token)
{
    // نکته مهم: update.Message برای CallbackQuery همیشه null است
    // (پیام واقعی داخل update.CallbackQuery.Message قرار دارد).
    // به همین دلیل باید CallbackQuery را جدا و قبل از هر return زودهنگام پردازش کنیم،
    // وگرنه دکمه‌های شیشه‌ای اصلاً کار نمی‌کنند.
    if (update.CallbackQuery != null)
    {
        await HandleCallbackQuery(update.CallbackQuery, token);
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

    // =======================
    // پیام همگانی و ادامه‌ی حالت ویرایش باید همین‌جا (زودتر از قاپیدن ویدیو/عکس
    // توسط فلوی افزودن فیلم، و زودتر از خروج به‌خاطر متن خالی) چک بشن؛
    // چون caption عکس/ویدیو توی message.Text نمیاد و این پیام‌ها همیشه text==null دارن.
    // =======================
    if (text == "📢 پیام همگانی" && isAdmin)
    {
        adminState = new AdminState
        {
            WaitingForBroadcast = true
        };
        adminStates[userId] = adminState;

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "📢 پیام خود را ارسال کنید (متن / عکس / فیلم)"
        );

        return;
    }

    if (isAdmin && adminState?.WaitingForBroadcast == true)
    {
        var users = userRepo.GetAllTelegramIds();
        int success = 0;

        foreach (var recipientId in users)
        {
            try
            {
                if (message.Type == MessageType.Photo)
                {
                    await botClient.SendPhoto(
                        chatId: recipientId,
                        photo: message.Photo!.Last().FileId,
                        caption: message.Caption ?? ""
                    );
                }
                else if (message.Type == MessageType.Video)
                {
                    await botClient.SendVideo(
                        chatId: recipientId,
                        video: message.Video!.FileId,
                        caption: message.Caption ?? ""
                    );
                }
                else if (message.Type == MessageType.Text)
                {
                    await botClient.SendMessage(
                        chatId: recipientId,
                        text: message.Text!
                    );
                }

                success++;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: $"✅ ارسال شد\n👤 موفق: {success}"
        );

        adminStates.Remove(userId);
        return;
    }

    if (isAdmin &&
        adminState?.EditingMovie != null &&
        adminState.Mode != null)
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
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "❌ نوع پیام مناسب نبود، لطفاً دوباره ارسال کن."
            );
            return;
        }

        movieRepo.UpdateMovie(movie);

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "✅ ویرایش با موفقیت انجام شد"
        );

        adminStates.Remove(userId);
        return;
    }

    // =======================
    // 1) گرفتن فیلم از ادمین (شروع فلوی افزودن فیلم)
    // =======================
    if (message.Video != null && isAdmin)
    {
        adminState = new AdminState
        {
            FileId = message.Video.FileId
        };
        adminStates[userId] = adminState;

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "🖼️ حالا عکس کاور فیلم رو بفرست"
        );

        return;
    }

    if (message.Photo != null &&
        isAdmin &&
        adminState != null &&
        adminState.FileId != null &&
        adminState.PhotoFileId == null)
    {
        adminState.PhotoFileId = message.Photo.Last().FileId;

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "✍️ حالا اسم فیلم را بفرست."
        );

        return;
    }

    if (text == null)
        return;

    // =======================
    // 2) گرفتن اسم فیلم
    // =======================
    if (isAdmin &&
        adminState != null &&
        adminState.PhotoFileId != null &&
        adminState.Title == null)
    {
        adminState.Title = text;

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "📝 حالا توضیحات رو بفرست"
        );

        return;
    }

    // =======================
    // 3) گرفتن توضیحات + ذخیره
    // =======================
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

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: $"✅ ذخیره شد\nکد: {movie.MovieCode}"
        );

        var caption = $"{movie.Title}\n\n{movie.Description}";

        // هر دو پست (کانال و کپی ادمین) از دکمه‌ی لینکی استفاده می‌کنند، نه callback؛
        // با زدن دکمه کاربر وارد چت خصوصی ربات می‌شود و فیلم آنجا نمایش داده می‌شود
        // (نه اینکه فیلم مستقیم توی کانال ارسال شود).
        var postKeyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithUrl(
                    "🎬 دریافت فیلم",
                    $"https://t.me/{botUsername}?start={movie.MovieCode}")
            }
        });

        try
        {
            await botClient.SendPhoto(
                chatId: BotConfig.ChannelUsername,
                photo: movie.PhotoFileId,
                caption: caption,
                replyMarkup: postKeyboard
            );
        }
        catch (Exception ex)
        {
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: $"⚠️ ارسال پست به کانال ناموفق بود: {ex.Message}"
            );
        }

        // یک نسخه هم برای خود ادمین (همان دکمه‌ی لینکی، پس موقع فوروارد هم زنده می‌ماند).
        var adminCopyKeyboard = postKeyboard;

        await botClient.SendPhoto(
            chatId: message.Chat.Id,
            photo: movie.PhotoFileId,
            caption: caption,
            replyMarkup: adminCopyKeyboard
        );

        adminStates.Remove(userId);
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
                    InlineKeyboardButton.WithCallbackData(
                        movie.Title,
                        "movie_" + movie.MovieCode)
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
            new KeyboardButton[] { "🔥 پربازدیدترین فیلم‌ها", "📋 لیست فیلم‌ها" }
        })
        {
            ResizeKeyboard = true
        };

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "👑 پنل مدیریت",
            replyMarkup: keyboard
        );

        return;
    }

    if (text == "🗑 حذف فیلم" && isAdmin)
    {
        adminState = new AdminState
        {
            WaitingForDeleteCode = true
        };
        adminStates[userId] = adminState;

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "🎬 کد فیلم را ارسال کنید:"
        );

        return;
    }

    if (isAdmin &&
        adminState != null &&
        adminState.WaitingForDeleteCode)
    {
        bool deleted = movieRepo.DeleteMovie(text.Trim());

        if (deleted)
        {
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "✅ فیلم با موفقیت حذف شد."
            );
        }
        else
        {
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "❌ فیلمی با این کد پیدا نشد."
            );
        }

        adminStates.Remove(userId);
        return;
    }

    if (text == "✏️ ویرایش فیلم" && isAdmin)
    {
        adminState = new AdminState
        {
            WaitingForEditCode = true
        };
        adminStates[userId] = adminState;

        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "🎬 کد فیلم را ارسال کنید."
        );

        return;
    }

    if (isAdmin &&
        adminState != null &&
        adminState.WaitingForEditCode)
    {
        var movie = movieRepo.GetByCode(text.Trim());

        if (movie == null)
        {
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "❌ فیلمی با این کد پیدا نشد."
            );
            return;
        }

        adminState.WaitingForEditCode = false;
        adminState.EditingMovie = movie;

        var keyboard = new InlineKeyboardMarkup(new[]
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
            replyMarkup: keyboard);

        return;
    }

    if (text == "🎬 افزودن فیلم" && isAdmin)
    {
        // فقط راهنمایی می‌کنیم؛ خودِ فلو با رسیدن ویدیو (بخش ۱ بالا) شروع می‌شود.
        // (قبلاً اینجا یک فلوی موازی و مرده با فیلدهای WaitingForMovie/Title/Description
        // وجود داشت که هرگز اجرا نمی‌شد چون بخش ۱ همیشه زودتر ویدیو را می‌گرفت.)
        await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "📥 فیلم را ارسال کنید"
        );

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

        // چون تعداد فیلم‌ها ممکنه زیاد باشه و پیام تلگرام محدودیت طول داره (۴۰۹۶ کاراکتر)،
        // لیست رو به چند تیکه تقسیم می‌کنیم.
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

    // =======================
    // /start
    // =======================
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

            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "🎬 خوش اومدی! ربات فعال شد"
            );
        }
        else
        {
            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "👋 خوش برگشتی!"
            );
        }

        return;
    }

    // =======================
    // /start code (ارسال فیلم)
    // =======================
    if (text.StartsWith("/start "))
    {
        var code = text.Replace("/start ", "").Trim();

        if (!await IsUserJoined(message.From!.Id))
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithUrl(
                        "📢 عضویت در کانال",
                        BotConfig.ChannelLink)
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ عضو شدم", "check_join_" + code)
                }
            });

            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "❌ برای دریافت فیلم ابتدا در کانال عضو شوید.",
                replyMarkup: keyboard);

            return;
        }

        await DeliverMovie(message.Chat.Id, code);
        return;
    }
}

// =======================
// ارسال فیلم به یک چت (مشترک بین مسیر /start code و دکمه‌ی «✅ عضو شدم»)
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

    // ⏳ حذف بعد از ۳۰ ثانیه
    _ = Task.Run(async () =>
    {
        await Task.Delay(30000);

        try
        {
            await botClient.DeleteMessage(chatId, sentMessage.MessageId);
        }
        catch
        {
            // اگر حذف نشد (مثلاً دسترسی نبود)
        }
    });

    return true;
}

// =======================
// Callback Query Handler
// =======================
async Task HandleCallbackQuery(CallbackQuery callbackQuery, CancellationToken token)
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

        if (!await IsUserJoined(userId))
        {
            await botClient.AnswerCallbackQuery(
                callbackQuery.Id,
                "❌ هنوز عضو کانال نشدی!",
                showAlert: true);
            return;
        }

        await botClient.AnswerCallbackQuery(callbackQuery.Id, "✅ عضویت تایید شد");
        await DeliverMovie(chatId.Value, code);
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

        var sentMessage = await botClient.SendVideo(
            chatId: chatId.Value,
            video: movie.FileId,
            caption: $"{movie.Title}\n\n{movie.Description}\n\n👁 تعداد بازدید: {movie.Views}"
        );

        movieRepo.AddView(movie.MovieCode);

        // ⏳ حذف بعد از ۳۰ ثانیه (هماهنگ با مسیر /start code)
        _ = Task.Run(async () =>
        {
            await Task.Delay(30000);

            try
            {
                await botClient.DeleteMessage(
                    chatId: chatId.Value,
                    messageId: sentMessage.MessageId
                );
            }
            catch
            {
                // اگر حذف نشد (مثلاً دسترسی نبود)
            }
        });

        await botClient.AnswerCallbackQuery(callbackQuery.Id);
        return;
    }

    await botClient.AnswerCallbackQuery(callbackQuery.Id);
}

// =======================
// Error Handler
// =======================
Task ErrorHandler(ITelegramBotClient bot, Exception ex, CancellationToken token)
{
    Console.WriteLine(ex.Message);
    return Task.CompletedTask;
}

async Task<bool> IsUserJoined(long userId)
{
    try
    {
        var member = await botClient.GetChatMember(
            BotConfig.ChannelUsername,
            userId);

        return member.Status == ChatMemberStatus.Member
            || member.Status == ChatMemberStatus.Administrator
            || member.Status == ChatMemberStatus.Creator;
    }
    catch
    {
        return false;
    }
}
