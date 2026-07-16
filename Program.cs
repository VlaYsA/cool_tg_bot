using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using MySqlConnector;
using System.Text.Json;
using DotNetEnv;

Env.Load();

string BotToken = Environment.GetEnvironmentVariable("BOT_TOKEN") ?? throw new Exception("BOT_TOKEN not found in .env file");
string DbConnectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ?? throw new Exception("DB_CONNECTION_STRING not found in .env file");

using var cts = new CancellationTokenSource();
var bot = new TelegramBotClient(BotToken, cancellationToken: cts.Token);
var me = await bot.GetMe();

Dictionary<long, string> userStatus = new Dictionary<long, string>(); 
Dictionary<long, string> userPendingNotes = new Dictionary<long, string>();

bot.OnMessage += OnMessage;

Console.WriteLine($"@{me.Username} is running... Press Enter to terminate");
Console.ReadLine();
cts.Cancel();

async Task OnMessage(Message msg, UpdateType type)
{
    try 
    {
        if (msg.Text is null) return;
        string text = msg.Text.Trim();
        long userId = msg.From!.Id;

        if (userStatus.ContainsKey(userId) && userStatus[userId] == "WAITING_FOR_TEXT")
        {
            string noteTitle = userPendingNotes[userId];
            await HandleFinalizeAddNote(msg, noteTitle, text);
            return;
        }

        if (userStatus.ContainsKey(userId) && userStatus[userId] == "WAITING_FOR_TITLE")
        {
            if (text.Length > 20)
            {
                await bot.SendMessage(msg.Chat, "Ошибка: Название слишком длинное (макс 20 символов)");
                return;
            }
            userPendingNotes[userId] = text; 
            userStatus[userId] = "WAITING_FOR_TEXT"; 
            await bot.SendMessage(msg.Chat, $"Название '{text}' принято.\nОтправьте основной текст заметки (до 1024 символов):");
            return;
        }

        await SaveMessageToDb(msg);

        string[] parts = text.Split(' ', 2); 
        string command = parts[0].ToLower(); 
        string arguments = parts.Length > 1 ? parts[1] : string.Empty;

        switch (command)
        {
            case "/start":
                await bot.SendMessage(msg.Chat, "Привет, я Cool_bot! Я умею выполнять функции заметок, а также повторять за тобой!\n\n/help - список команд");
                break;

            case "/help":
                await bot.SendMessage(msg.Chat, "Справка:\n/add [название] - создать заметку\n/list - список названий\n/list [id] - просмотр текста по ID\n/delete [id] - удаление по ID");
                break;

            case "/add":
                await HandleStartAddNote(msg, arguments);
                break;

            case "/list":
                if (string.IsNullOrEmpty(arguments))
                    await HandleListNotes(msg);
                else
                    await HandleViewNote(msg, arguments);
                break;

            case "/delete":
                await HandleDeleteNote(msg, arguments);
                break;

            default:
                await bot.SendMessage(msg.Chat, $"Вы сказали: {text}");
                break;
        }
    } 
    catch (Exception ex) 
    {
        Console.WriteLine($"Critical Error: {ex}");
        await bot.SendMessage(msg.Chat, "Произошла внутренняя ошибка при обработке запроса.");
    }
}

async Task HandleStartAddNote(Message msg, string arguments)
{
    long userId = msg.From!.Id;

    if (!string.IsNullOrWhiteSpace(arguments))
    {
        if (arguments.Length > 20)
        {
            await bot.SendMessage(msg.Chat, "Ошибка: Название слишком длинное (макс 20 символов)");
            return;
        }

        userPendingNotes[userId] = arguments;
        userStatus[userId] = "WAITING_FOR_TEXT";
        await bot.SendMessage(msg.Chat, $"Название '{arguments}' принято.\nОтправьте основной текст заметки (до 1024 символов):");
    }
    else
    {
        userStatus[userId] = "WAITING_FOR_TITLE";
        await bot.SendMessage(msg.Chat, "Вы не указали название. Пожалуйста, введите название заметки (макс 20 символов):");
    }
}

async Task HandleFinalizeAddNote(Message msg, string title, string content)
{
    try 
    {
        if (content.Length > 1024)
        {
            await bot.SendMessage(msg.Chat, "Ошибка: Текст слишком длинный (макс 1024 символа)");
            return;
        }

        using var connection = new MySqlConnection(DbConnectionString);
        await connection.OpenAsync();
        
        string sql = "INSERT INTO notes (user_id, title, note_text) VALUES (@uid, @title, @text)";
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@uid", msg.From!.Id);
        command.Parameters.AddWithValue("@title", title);
        command.Parameters.AddWithValue("@text", content);
        
        await command.ExecuteNonQueryAsync();
        await bot.SendMessage(msg.Chat, $"Заметка '{title}' сохранена.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"DB Error in Finalize: {ex.Message}");
        await bot.SendMessage(msg.Chat, "Ошибка при сохранении в базу данных.");
    }
    finally 
    {
        userPendingNotes.Remove(msg.From!.Id);
        userStatus.Remove(msg.From!.Id);
    }
}

async Task HandleListNotes(Message msg)
{
    try 
    {
        using var connection = new MySqlConnection(DbConnectionString);
        await connection.OpenAsync();

        string sql = "SELECT id, title FROM notes WHERE user_id = @uid";
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@uid", msg.From!.Id);

        using var reader = await command.ExecuteReaderAsync();
        if (!reader.HasRows)
        {
            await bot.SendMessage(msg.Chat, "У вас пока нет заметок.");
            return;
        }

        string response = "Ваши заметки:\n";
        while (await reader.ReadAsync())
        {
            response += $"ID: {reader.GetInt32("id")} | {reader.GetString("title")}\n";
        }
        response += "\nДля чтения введите: /list [id]";
        await bot.SendMessage(msg.Chat, response);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in List: {ex.Message}");
        await bot.SendMessage(msg.Chat, "Ошибка при получении списка заметок.");
    }
}

async Task HandleViewNote(Message msg, string arguments)
{
    try 
    {
        if (!int.TryParse(arguments, out int noteId))
        {
            await bot.SendMessage(msg.Chat, "Введите корректный числовой ID заметки.");
            return;
        }

        using var connection = new MySqlConnection(DbConnectionString);
        await connection.OpenAsync();

        string sql = "SELECT title, note_text FROM notes WHERE id = @id AND user_id = @uid";
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", noteId);
        command.Parameters.AddWithValue("@uid", msg.From!.Id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            string title = reader.GetString("title");
            string text = reader.GetString("note_text");
            await bot.SendMessage(msg.Chat, $"Заметка: {title}\n\n{text}");
        }
        else
        {
            await bot.SendMessage(msg.Chat, "Заметка не найдена.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in View: {ex.Message}");
        await bot.SendMessage(msg.Chat, "Ошибка при чтении заметки.");
    }
}

async Task HandleDeleteNote(Message msg, string arguments)
{
    try 
    {
        if (!int.TryParse(arguments, out int noteId))
        {
            await bot.SendMessage(msg.Chat, "Введите корректный ID заметки.");
            return;
        }
        using var connection = new MySqlConnection(DbConnectionString);
        await connection.OpenAsync();

        string sql = "DELETE FROM notes WHERE id = @id AND user_id = @uid";
        using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", noteId);
        command.Parameters.AddWithValue("@uid", msg.From!.Id);

        int affectedRows = await command.ExecuteNonQueryAsync();
        if (affectedRows > 0)
            await bot.SendMessage(msg.Chat, $"Заметка {noteId} удалена.");
        else
            await bot.SendMessage(msg.Chat, "Заметка не найдена или у вас нет к ней доступа.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error in Delete: {ex.Message}");
        await bot.SendMessage(msg.Chat, "Ошибка при удалении заметки.");
    }
}

async Task SaveMessageToDb(Message msg)
{
    try
    {
        using var connection = new MySqlConnection(DbConnectionString);
        await connection.OpenAsync();

        string sqlUsers = "INSERT INTO users (user_id, username) VALUES (@uid, @uname) ON DUPLICATE KEY UPDATE username = @uname";
        using var cmdUsers = new MySqlCommand(sqlUsers, connection);
        cmdUsers.Parameters.AddWithValue("@uid", msg.From?.Id);
        cmdUsers.Parameters.AddWithValue("@uname", msg.From?.Username ?? (object)DBNull.Value);
        await cmdUsers.ExecuteNonQueryAsync();

        string sqlStats = "INSERT INTO user_stats (user_id, hits) VALUES (@uid, 1) ON DUPLICATE KEY UPDATE hits = hits + 1";
        using var cmdStats = new MySqlCommand(sqlStats, connection);
        cmdStats.Parameters.AddWithValue("@uid", msg.From?.Id);
        await cmdStats.ExecuteNonQueryAsync();

        string sqlMsgs = @"INSERT INTO user_messages 
                          (user_id, first_message_text, created_at, last_message_text, last_message_at) 
                          VALUES 
                          (@uid, @text, NOW(), @text, NOW()) 
                          ON DUPLICATE KEY UPDATE 
                          last_message_text = @text, 
                          last_message_at = NOW()";
        using var cmdMsgs = new MySqlCommand(sqlMsgs, connection);
        cmdMsgs.Parameters.AddWithValue("@uid", msg.From?.Id);
        cmdMsgs.Parameters.AddWithValue("@text", msg.Text ?? "");
        await cmdMsgs.ExecuteNonQueryAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database stats error: {ex.Message}");
    }
}
