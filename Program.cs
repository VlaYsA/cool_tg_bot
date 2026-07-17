using Telegram.Bot;
using DotNetEnv;

Env.Load();

string BotToken = Environment.GetEnvironmentVariable("BOT_TOKEN") ?? throw new Exception("BOT_TOKEN missing");
string DbConn = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ?? throw new Exception("DB_CONN missing");

using var cts = new CancellationTokenSource();
var bot = new TelegramBotClient(BotToken, cancellationToken: cts.Token);

var dbService = new DatabaseService(DbConn);
var noteManager = new NoteManager(dbService, bot);
var handler = new BotHandler(bot, dbService, noteManager);

bot.OnMessage += handler.OnMessage;

var me = await bot.GetMe();
Console.WriteLine($"@{me.Username} is running... Press Enter to terminate");
Console.ReadLine();
cts.Cancel();
