namespace CoolBot.Models
{
    public class NoteSummary
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    public class NoteDetail
    {
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
