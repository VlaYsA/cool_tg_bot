using MySqlConnector;
using CoolBot.Models;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string connectionString) => _connectionString = connectionString;

    private async Task<MySqlConnection> GetConnectionAsync()
    {
        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        return conn;
    }

    public async Task SaveUserStats(long userId, string username, string text)
    {
        using var conn = await GetConnectionAsync();
        
        string sqlUsers = "INSERT INTO users (user_id, username) VALUES (@uid, @uname) ON DUPLICATE KEY UPDATE username = @uname";
        using var cmdUsers = new MySqlCommand(sqlUsers, conn);
        cmdUsers.Parameters.AddWithValue("@uid", userId);
        cmdUsers.Parameters.AddWithValue("@uname", username ?? (object)DBNull.Value);
        await cmdUsers.ExecuteNonQueryAsync();

        string sqlStats = "INSERT INTO user_stats (user_id, hits) VALUES (@uid, 1) ON DUPLICATE KEY UPDATE hits = hits + 1";
        using var cmdStats = new MySqlCommand(sqlStats, conn);
        cmdStats.Parameters.AddWithValue("@uid", userId);
        await cmdStats.ExecuteNonQueryAsync();

        string sqlMsgs = @"INSERT INTO user_messages (user_id, first_message_text, created_at, last_message_text, last_message_at) 
                          VALUES (@uid, @text, NOW(), @text, NOW()) 
                          ON DUPLICATE KEY UPDATE last_message_text = @text, last_message_at = NOW()";
        using var cmdMsgs = new MySqlCommand(sqlMsgs, conn);
        cmdMsgs.Parameters.AddWithValue("@uid", userId);
        cmdMsgs.Parameters.AddWithValue("@text", text ?? "");
        await cmdMsgs.ExecuteNonQueryAsync();
    }

    public async Task AddNote(long userId, string title, string text)
    {
        using var conn = await GetConnectionAsync();
        string sql = "INSERT INTO notes (user_id, title, note_text) VALUES (@uid, @title, @text)";
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@text", text);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<NoteSummary>> GetUserNotes(long userId)
    {
        using var conn = await GetConnectionAsync();
        string sql = "SELECT id, title FROM notes WHERE user_id = @uid";
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@uid", userId);
        
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<NoteSummary>();
        while (await reader.ReadAsync()) 
        {
            list.Add(new NoteSummary { 
                Id = reader.GetInt32("id"), 
                Title = reader.GetString("title") 
            });
        }
        return list;
    }

    public async Task<NoteDetail?> GetNote(long userId, int noteId)
    {
        using var conn = await GetConnectionAsync();
        string sql = "SELECT title, note_text FROM notes WHERE id = @id AND user_id = @uid";
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", noteId);
        cmd.Parameters.AddWithValue("@uid", userId);
        
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync()) 
        {
            return new NoteDetail { 
                Title = reader.GetString("title"), 
                Text = reader.GetString("note_text") 
            };
        }
        return null;
    }

    public async Task<bool> DeleteNote(long userId, int noteId)
    {
        using var conn = await GetConnectionAsync();
        string sql = "DELETE FROM notes WHERE id = @id AND user_id = @uid";
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", noteId);
        cmd.Parameters.AddWithValue("@uid", userId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
