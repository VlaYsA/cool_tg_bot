using Telegram.Bot;
using Telegram.Bot.Types;

public class NoteManager
{
    private readonly DatabaseService _db;
    private readonly TelegramBotClient _bot;
    private readonly Dictionary<long, string> _userStatus = new();
    private readonly Dictionary<long, string> _userPendingNotes = new();

    public NoteManager(DatabaseService db, TelegramBotClient bot)
    {
        _db = db;
        _bot = bot;
    }

    public bool IsWaitingForSomething(long userId) => _userStatus.ContainsKey(userId);

    public string? GetStatus(long userId) => _userStatus.GetValueOrDefault(userId);

    public async Task HandleAddNoteStart(Message msg, string args)
    {
        long userId = msg.From!.Id;
        if (!string.IsNullOrWhiteSpace(args))
        {
            if (args.Length > 20) { await _bot.SendMessage(msg.Chat, "Ошибка: Название слишком длинное (макс 20)"); return; }
            _userPendingNotes[userId] = args;
            _userStatus[userId] = "WAITING_FOR_TEXT";
            await _bot.SendMessage(msg.Chat, $"Название '{args}' принято.\nОтправьте текст заметки:");
        }
        else
        {
            _userStatus[userId] = "WAITING_FOR_TITLE";
            await _bot.SendMessage(msg.Chat, "Введите название заметки (макс 20 символов):");
        }
    }

    public async Task HandleNoteInput(Message msg, string text)
    {
        long userId = msg.From!.Id;
        string status = _userStatus[userId];

        if (status == "WAITING_FOR_TITLE")
        {
            if (text.Length > 20) { await _bot.SendMessage(msg.Chat, "Ошибка: слишком длинно"); return; }
            _userPendingNotes[userId] = text;
            _userStatus[userId] = "WAITING_FOR_TEXT";
            await _bot.SendMessage(msg.Chat, $"Название '{text}' принято.\nОтправьте текст заметки:");
        }
        else if (status == "WAITING_FOR_TEXT")
        {
            if (text.Length > 1024) { await _bot.SendMessage(msg.Chat, "Ошибка: текст слишком длинный"); return; }
            string title = _userPendingNotes[userId];
            await _db.AddNote(userId, title, text);
            await _bot.SendMessage(msg.Chat, $"Заметка '{title}' сохранена.");
            _userStatus.Remove(userId);
            _userPendingNotes.Remove(userId);
        }
    }

    public async Task ListNotes(Message msg)
    {
        var notes = await _db.GetUserNotes(msg.From!.Id);
        if (notes == null || notes.Count == 0) 
        { 
            await _bot.SendMessage(msg.Chat, "У вас нет заметок."); 
            return; 
        }
        
        string res = "Ваши заметки:\n" + string.Join("\n", notes.Select(n => $"ID: {n.Id} | {n.Title}")) + "\n\n/list [id]";
        await _bot.SendMessage(msg.Chat, res);
    }


    public async Task ViewNote(Message msg, string arg)
    {
        if (!int.TryParse(arg, out int id)) { await _bot.SendMessage(msg.Chat, "Введите числовой ID."); return; }
        var note = await _db.GetNote(msg.From!.Id, id);
        await _bot.SendMessage(msg.Chat, note != null ? $"Заметка: {note.Title}\n\n{note.Text}" : "Заметка не найдена.");
    }


    public async Task DeleteNote(Message msg, string arg)
    {
        if (!int.TryParse(arg, out int id)) { await _bot.SendMessage(msg.Chat, "Введите числовой ID."); return; }
        bool success = await _db.DeleteNote(msg.From!.Id, id);
        await _bot.SendMessage(msg.Chat, success ? $"Заметка {id} удалена." : "Заметка не найдена.");
    }
}
