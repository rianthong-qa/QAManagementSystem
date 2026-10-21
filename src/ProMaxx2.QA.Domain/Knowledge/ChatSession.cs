namespace ProMaxx2.QA.Domain.Knowledge;

/// <summary>บทสนทนากับผู้ช่วย AI ของผู้ใช้หนึ่งคน — ผู้ใช้เห็นเฉพาะของตัวเอง</summary>
public sealed class ChatSession
{
    private ChatSession() { }

    public ChatSession(Guid userId, string? title, Guid? projectId)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.");
        ChatSessionId = Guid.NewGuid();
        UserId = userId;
        ProjectId = projectId;
        CreatedAt = UpdatedAt = DateTime.UtcNow;
        Rename(title);
    }

    public Guid ChatSessionId { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = "บทสนทนาใหม่";
    public Guid? ProjectId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public void Rename(string? title)
    {
        var text = string.IsNullOrWhiteSpace(title) ? "บทสนทนาใหม่" : title.Trim().ReplaceLineEndings(" ");
        Title = text.Length > 200 ? text[..200] : text;
    }

    public void Touch() => UpdatedAt = DateTime.UtcNow;
    public void Delete() => IsDeleted = true;
}

/// <summary>ข้อความในบทสนทนา — Role = user / assistant; CitationsJson = แหล่งอ้างอิงที่ใช้ตอบ; Feedback = 1 (มีประโยชน์) / -1 (ไม่มีประโยชน์)</summary>
public sealed class ChatMessage
{
    public static readonly string[] Roles = ["user", "assistant"];

    private ChatMessage() { }

    public ChatMessage(Guid chatSessionId, string role, string content, string? citationsJson = null, string? model = null, int? promptTokens = null, int? completionTokens = null)
    {
        if (!Roles.Contains(role)) throw new ArgumentException("Role ไม่ถูกต้อง");
        ChatSessionId = chatSessionId;
        Role = role;
        Content = content ?? string.Empty;
        CitationsJson = citationsJson;
        Model = model;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        CreatedAt = DateTime.UtcNow;
    }

    public long ChatMessageId { get; private set; }
    public Guid ChatSessionId { get; private set; }
    public string Role { get; private set; } = "user";
    public string Content { get; private set; } = string.Empty;
    public string? CitationsJson { get; private set; }
    public string? Model { get; private set; }
    public int? PromptTokens { get; private set; }
    public int? CompletionTokens { get; private set; }
    public short? Feedback { get; private set; }
    public string? FeedbackNote { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public void SetFeedback(short? feedback, string? note)
    {
        if (feedback is not (null or 1 or -1)) throw new ArgumentException("Feedback ต้องเป็น 1 หรือ -1");
        if (Role != "assistant") throw new InvalidOperationException("ให้ Feedback ได้เฉพาะคำตอบของผู้ช่วย AI");
        Feedback = feedback;
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        FeedbackNote = text?.Length > 1000 ? text[..1000] : text;
    }
}
