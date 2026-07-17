using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

public class BotHandler
{
    private readonly TelegramBotClient _bot;
    private readonly DatabaseService _db;
    private readonly NoteManager _notes;

    public BotHandler(TelegramBotClient bot, DatabaseService db, NoteManager notes)
    {
        _bot = bot;
        _db = db;
        _notes = notes;
    }

    public async Task OnMessage(Message msg, UpdateType type)
    {
        if (msg.Text is null) return;
        string text = msg.Text.Trim();
        long userId = msg.From!.Id;

        try
        {
            if (_notes.IsWaitingForSomething(userId))
            {
                await _notes.HandleNoteInput(msg, text);
                return;
            }

            await _db.SaveUserStats(userId, msg.From.Username ?? "", text);

            string[] parts = text.Split(' ', 2);
            string command = parts[0].ToLower();
            string args = parts.Length > 1 ? parts[1] : "";

            switch (command)
            {
                case "/start":
                    await _bot.SendMessage(msg.Chat, "Привет! Я Cool_bot! Я умею выполнять функции заметок, а также повторять за тобой!\n\n/help - список команд");
                    break;
                case "/help":
                    await _bot.SendMessage(msg.Chat, "Справка:\n/add [название] - создать заметку\n/list - список заметок\n/list [id] - просмотр текста заметок по ID\n/delete [id] - удалить заметку");
                    break;
                case "/add":
                    await _notes.HandleAddNoteStart(msg, args);
                    break;
                case "/list":
                    if (string.IsNullOrEmpty(args)) await _notes.ListNotes(msg); else await _notes.ViewNote(msg, args);
                    break;
                case "/delete":
                    await _notes.DeleteNote(msg, args);
                    break;
                default:
                    await _bot.SendMessage(msg.Chat, $"Неизвестная команда: /help - для вывода команд");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            await _bot.SendMessage(msg.Chat, "Произошла ошибка.");
        }
    }
}
