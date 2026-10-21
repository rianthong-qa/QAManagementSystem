using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Domain.Knowledge;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

public sealed record ChatSessionDto(Guid ChatSessionId, string Title, Guid? ProjectId, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record ChatMessageDto(long ChatMessageId, string Role, string Content, IReadOnlyList<KnowledgeCitation> Citations, string? Model, short? Feedback, DateTime CreatedAt);
public sealed record ChatSessionDetailDto(ChatSessionDto Session, IReadOnlyList<ChatMessageDto> Messages);
public sealed record AskKnowledgeRequest(string Question, Guid? ProjectId = null, IReadOnlyList<string>? Sources = null);

/// <summary>คำขอผิดรูปแบบ (400) / ไม่มีสิทธิ์ (403) — โยนก่อนเริ่มส่ง stream เพื่อให้ controller ตอบเป็น HTTP status ปกติ</summary>
public sealed class KnowledgeChatException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// ผู้ช่วย AI คลังความรู้ (AI_KNOWLEDGE_CHAT_PLAN.md Phase 2) — ดึงความรู้ตามสิทธิ์ผู้ถาม → ส่ง LLM พร้อมเลขอ้างอิง → stream คำตอบ
/// คำถาม + คำตอบบันทึกพร้อมกันเมื่อตอบเสร็จ (หรือผู้ใช้กดหยุด) — ตอบไม่สำเร็จจะไม่เหลือคำถามค้างในประวัติ
/// ไม่พบความรู้ที่เกี่ยวข้อง = ตอบ "ไม่พบ" ทันทีโดยไม่เรียก LLM (ไม่ให้ AI เดา)
/// </summary>
public sealed class KnowledgeChatService(QaDbContext db, KnowledgeSearchService search, IAiChatClient ai, ILogger<KnowledgeChatService> logger)
{
    public const string AuditEntityType = "KnowledgeChat";
    public const string StoppedSuffix = "\n\n_(หยุดการตอบ)_";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All) };

    public async Task<ChatSessionDto> CreateSessionAsync(Guid userId, Guid? projectId, CancellationToken ct)
    {
        var session = new ChatSession(userId, null, projectId);
        db.ChatSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return ToDto(session);
    }

    public async Task<IReadOnlyList<ChatSessionDto>> ListSessionsAsync(Guid userId, CancellationToken ct) =>
        (await db.ChatSessions.AsNoTracking().Where(x => x.UserId == userId && !x.IsDeleted).OrderByDescending(x => x.UpdatedAt).Take(100).ToListAsync(ct))
            .Select(ToDto).ToList();

    public async Task<ChatSessionDetailDto?> GetSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await db.ChatSessions.AsNoTracking().FirstOrDefaultAsync(x => x.ChatSessionId == sessionId && x.UserId == userId && !x.IsDeleted, ct);
        if (session is null) return null;
        var messages = await db.ChatMessages.AsNoTracking().Where(x => x.ChatSessionId == sessionId).OrderBy(x => x.ChatMessageId).ToListAsync(ct);
        return new(ToDto(session), messages.Select(ToDto).ToList());
    }

    public async Task<bool> DeleteSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await db.ChatSessions.FirstOrDefaultAsync(x => x.ChatSessionId == sessionId && x.UserId == userId && !x.IsDeleted, ct);
        if (session is null) return false;
        session.Delete();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SetFeedbackAsync(Guid userId, long messageId, short? feedback, string? note, CancellationToken ct)
    {
        var message = await db.ChatMessages.FirstOrDefaultAsync(x => x.ChatMessageId == messageId, ct);
        if (message is null || !await db.ChatSessions.AnyAsync(x => x.ChatSessionId == message.ChatSessionId && x.UserId == userId && !x.IsDeleted, ct)) return false;
        try { message.SetFeedback(feedback, note); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { throw new KnowledgeChatException(400, ex.Message); }
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// ถามผู้ช่วย AI — emit(eventName, data): "sources" (แหล่งที่ใช้) → "delta" (ข้อความทีละส่วน) → "done" หรือ "error"
    /// ข้อผิดพลาดก่อนเริ่มตอบโยน <see cref="KnowledgeChatException"/> (ยังไม่มีการ emit)
    /// </summary>
    public async Task AskAsync(ClaimsPrincipal user, Guid userId, Guid sessionId, AskKnowledgeRequest request, Func<string, object, Task> emit, CancellationToken ct)
    {
        var question = request.Question?.Trim() ?? "";
        if (question.Length == 0) throw new KnowledgeChatException(400, "กรุณาพิมพ์คำถาม");
        if (question.Length > KnowledgePrompt.MaxQuestionLength) throw new KnowledgeChatException(400, $"คำถามยาวได้ไม่เกิน {KnowledgePrompt.MaxQuestionLength:N0} ตัวอักษร");
        var session = await db.ChatSessions.FirstOrDefaultAsync(x => x.ChatSessionId == sessionId && x.UserId == userId && !x.IsDeleted, ct)
            ?? throw new KnowledgeChatException(404, "ไม่พบบทสนทนา");

        AiRuntimeConfiguration runtime;
        try { runtime = await ai.GetRuntimeAsync(ct); }
        catch (AiNotConfiguredException ex) { throw new KnowledgeChatException(503, $"ผู้ช่วย AI ยังไม่พร้อมใช้งาน: {ex.Message}"); }

        var sources = request.Sources?.Where(x => KnowledgeDocument.SourceTypes.Contains(x)).ToArray();
        var scope = await search.ScopeForAsync(user, userId, request.ProjectId, sources is { Length: > 0 } ? sources : null, ct);
        if (request.ProjectId.HasValue && !scope.AllowedProjectIds.Contains(request.ProjectId.Value)) throw new KnowledgeChatException(403, "คุณไม่มีสิทธิ์ใน Project ที่เลือก");
        // ข้อมูล CRM ส่งให้ AI ภายนอกได้เฉพาะเมื่อผู้ดูแลอนุญาต (Setting Center → คลังความรู้)
        var allowCrmExternal = await db.AiConfigurations.AsNoTracking().OrderByDescending(x => x.UpdatedAt).Select(x => x.AllowCrmDataToExternalAi).FirstOrDefaultAsync(ct);
        var crmExcluded = scope.IncludeCrm && runtime.Provider != "Local" && !allowCrmExternal;
        if (crmExcluded) scope = scope with { IncludeCrm = false };

        var history = await db.ChatMessages.AsNoTracking().Where(x => x.ChatSessionId == sessionId).OrderByDescending(x => x.ChatMessageId)
            .Take(KnowledgePrompt.MaxHistoryMessages).OrderBy(x => x.ChatMessageId).Select(x => new { x.Role, x.Content }).ToListAsync(ct);
        var previousQuestion = history.LastOrDefault(x => x.Role == "user")?.Content;
        var contexts = await search.RetrieveContextAsync(KnowledgePrompt.RetrievalQuery(question, previousQuestion), scope, ct);

        await emit("sources", new { citations = KnowledgePrompt.Citations(contexts, ""), crmExcluded });

        var answer = new StringBuilder();
        var model = contexts.Count == 0 ? null : $"{runtime.Provider}/{runtime.Model}";
        var stopped = false;
        if (contexts.Count == 0)
        {
            answer.Append(KnowledgePrompt.NotFoundAnswer);
            await emit("delta", new { text = KnowledgePrompt.NotFoundAnswer });
        }
        else
        {
            var messages = KnowledgePrompt.History(history.Select(x => (x.Role, x.Content)))
                .Select(x => new AiChatMessage(x.Role, x.Content))
                .Append(new AiChatMessage("user", KnowledgePrompt.UserTurn(question, contexts))).ToList();
            var system = KnowledgePrompt.System(DateTime.UtcNow.AddHours(7).ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("th-TH")));
            try
            {
                await foreach (var delta in ai.StreamAsync(runtime, system, messages, sessionId.ToString(), ct))
                {
                    answer.Append(delta);
                    await emit("delta", new { text = delta });
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // ผู้ใช้กดหยุด/ปิดหน้า — เก็บคำตอบเท่าที่ได้ (ถ้ามี) ไว้ในประวัติ
                stopped = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException or IOException)
            {
                logger.LogWarning(ex, "Knowledge chat provider {Provider} failed", runtime.Provider);
                await emit("error", new { message = $"ผู้ช่วย AI ตอบไม่สำเร็จ: {Cut(ex.Message, 300)}" });
                return;
            }
        }

        if (stopped && answer.Length == 0) return;
        if (stopped) answer.Append(StoppedSuffix);
        var text = answer.ToString();
        var citations = KnowledgePrompt.Citations(contexts, text);
        var userMessage = new ChatMessage(sessionId, "user", question);
        var assistantMessage = new ChatMessage(sessionId, "assistant", text, JsonSerializer.Serialize(citations, Json), model);
        db.ChatMessages.AddRange(userMessage, assistantMessage);
        if (session.Title == "บทสนทนาใหม่") session.Rename(KnowledgePrompt.SessionTitle(question));
        session.Touch();
        db.AuditLogs.Add(new AuditLog(userId, "Asked", AuditEntityType, sessionId.ToString(),
            $"ถามผู้ช่วย AI · อ้างอิง {citations.Count(x => x.Cited)}/{citations.Count} แหล่ง · {model ?? "ไม่เรียก AI (ไม่พบความรู้)"}{(crmExcluded ? " · ไม่ส่งข้อมูล CRM" : "")}", null, null, null));
        // บันทึกแม้ผู้ใช้ยกเลิกคำขอแล้ว
        await db.SaveChangesAsync(CancellationToken.None);
        if (!stopped)
            await emit("done", new { userMessageId = userMessage.ChatMessageId, assistantMessageId = assistantMessage.ChatMessageId, citations, title = session.Title, model });
    }

    private static ChatSessionDto ToDto(ChatSession s) => new(s.ChatSessionId, s.Title, s.ProjectId, s.CreatedAt, s.UpdatedAt);

    private static ChatMessageDto ToDto(ChatMessage m) => new(m.ChatMessageId, m.Role, m.Content, ParseCitations(m.CitationsJson), m.Model, m.Feedback, m.CreatedAt);

    private static IReadOnlyList<KnowledgeCitation> ParseCitations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<KnowledgeCitation>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string Cut(string value, int max) => value.Length > max ? value[..max] + "…" : value;
}
