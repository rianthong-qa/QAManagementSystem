using System.ComponentModel;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ProMaxx2.QA.Application.Knowledge;
using ProMaxx2.QA.Domain.Governance;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Api.Services;

/// <summary>
/// เครื่องมือ MCP (อ่านอย่างเดียว) ให้ Claude Code / Claude Desktop ค้นคลังความรู้ QA — AI_KNOWLEDGE_CHAT_PLAN.md Phase 2B
/// สิทธิ์ตามผู้ถือ Personal Access Token (สมาชิก Project เท่านั้น), ข้อมูล CRM เฉพาะเมื่อผู้ใช้มี CRM.VIEW และผู้ดูแลอนุญาตส่ง CRM ให้ AI ภายนอก
/// (Claude เป็น AI ภายนอกเสมอ — Setting Center → คลังความรู้), ข้ามเอกสาร ExcludeFromAi,
/// ข้อความผ่าน SensitiveDataMasker และตัดผลไม่เกิน ~8,000 ตัวอักษร (Claude Code เตือนเมื่อผลเกิน 10,000 token); บันทึก Audit Log ทุกครั้ง
/// </summary>
[McpServerToolType]
public sealed class KnowledgeMcpTools(IHttpContextAccessor http, QaDbContext db, KnowledgeSearchService search, KnowledgeIndexService indexer, IConfiguration configuration)
{
    public const int MaxOutputChars = 8000;
    public const string AuditEntityType = "KnowledgeMcp";

    public const string ServerInstructions =
        "ProMaxx2 QA Hub knowledge base (Thai). Use search_knowledge first to find Defects, knowledge articles, documents (module specs, requirements, manuals) and — when allowed — customer CRM tickets, then get_defect / get_article / get_document / get_crm_ticket for details. " +
        "Use find_similar_defects before reporting a new bug to check for duplicates. Always cite Defect codes and include the web links returned. " +
        "Content is masked: tokens like [EMAIL], [PHONE], [CUSTOMER] are redacted values — never guess them.";

    [McpServerTool(Name = "list_projects", Title = "รายชื่อ Project ที่เข้าถึงได้", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List the QA Hub projects the current user is a member of (code and name). Use the project code to filter other tools.")]
    public async Task<string> ListProjects(CancellationToken cancellationToken)
    {
        var (userId, scope) = await ScopeAsync(null, null, cancellationToken);
        var projects = await db.Projects.AsNoTracking().Where(x => scope.AllowedProjectIds.Contains(x.ProjectId)).OrderBy(x => x.ProjectCode).Select(x => new { x.ProjectCode, x.ProjectName, x.IsActive }).ToListAsync(cancellationToken);
        var text = projects.Count == 0
            ? "คุณยังไม่ได้เป็นสมาชิก Project ใดใน QA Hub — ค้นได้เฉพาะความรู้กลาง"
            : "Project ที่เข้าถึงได้:\n" + string.Join("\n", projects.Select(p => $"- {p.ProjectCode}: {p.ProjectName}{(p.IsActive ? "" : " (ปิดใช้งาน)")}"));
        await AuditAsync(userId, "list_projects", $"{projects.Count} Project", cancellationToken);
        return text;
    }

    [McpServerTool(Name = "search_knowledge", Title = "ค้นคลังความรู้ QA", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Search the QA knowledge base (Defects, knowledge articles, uploaded documents such as module specs and requirements, and customer CRM support tickets when the administrator allows it; Thai full-text). Returns titles, snippets, status and web links. Query in Thai or English, e.g. symptom, error message, menu name or Defect code.")]
    public async Task<string> SearchKnowledge(
        [Description("Search text, e.g. 'พิมพ์ใบกำกับภาษีไม่ได้' or a Defect code")] string query,
        [Description("Optional project code from list_projects to limit results")] string? projectCode = null,
        [Description("Optional source filter: 'defect', 'article', 'document' or 'crm'")] string? source = null,
        [Description("Max results (1-10, default 8)")] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var q = KnowledgeQuery.Normalize(query) ?? throw new McpException($"กรุณาระบุคำค้นอย่างน้อย {KnowledgeQuery.MinLength} ตัวอักษร");
        var sources = source?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "defect" or "defects" => new[] { "Defect" },
            "article" or "articles" => new[] { "Article" },
            "document" or "documents" or "file" or "files" => new[] { "File" },
            "crm" or "ticket" or "tickets" or "crmticket" => new[] { "CrmTicket" },
            _ => throw new McpException("source ต้องเป็น 'defect', 'article', 'document' หรือ 'crm'"),
        };
        var (userId, scope) = await ScopeAsync(projectCode, sources, cancellationToken);
        if (sources is ["CrmTicket"] && !scope.IncludeCrm) throw new McpException(CrmNotAllowed);
        var results = await search.SearchAsync(q, scope, Math.Clamp(limit, 1, 10), cancellationToken, forAi: true);
        await AuditAsync(userId, "search_knowledge", $"\"{Cut(q, 80)}\" → {results.Count} รายการ", cancellationToken);
        if (results.Count == 0) return $"ไม่พบความรู้ที่ตรงกับ \"{q}\" — ลองคำที่สั้นลง คำพ้อง หรือไม่ระบุ project";
        var defects = await DefectStatusAsync(results.Where(x => x.SourceType == "Defect").Select(x => x.SourceKey), cancellationToken);
        var sb = new StringBuilder($"พบ {results.Count} รายการสำหรับ \"{q}\":\n");
        var n = 0;
        foreach (var r in results)
        {
            sb.Append($"\n{++n}. [{KnowledgePrompt.SourceLabel(r.SourceType)}] {r.Title}\n");
            var meta = new List<string> { r.ProjectName ?? "ความรู้กลาง" };
            if (defects.TryGetValue(r.SourceKey, out var d)) meta.Add($"สถานะ {d.Status} · ความรุนแรง {d.Severity}");
            if (r.MatchedChunks > 1) meta.Add($"ตรงกัน {r.MatchedChunks} ช่วง");
            sb.Append("   ").Append(string.Join(" · ", meta)).Append('\n');
            sb.Append("   ").Append(SensitiveDataMasker.Mask(r.Snippet).ReplaceLineEndings(" ")).Append('\n');
            if (WebUrl(r.SourceUrl) is { } url) sb.Append("   ลิงก์: ").Append(url).Append('\n');
            sb.Append("   รายละเอียด: ").Append(r.SourceType switch { "Defect" => $"get_defect(\"{d?.Code ?? r.SourceKey}\")", "File" => $"get_document(\"{r.SourceKey}\")", "CrmTicket" => $"get_crm_ticket(\"{r.SourceKey}\")", _ => $"get_article(\"{r.SourceKey}\")" }).Append('\n');
        }
        return Limit(sb.ToString());
    }

    [McpServerTool(Name = "get_defect", Title = "รายละเอียด Defect", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get full details of one Defect: status, severity, module, release/build, description, steps to reproduce, expected/actual result, linked test cases and latest comments.")]
    public async Task<string> GetDefect(
        [Description("Defect code (e.g. PMX2-DEF-1258) or Defect id (GUID)")] string defect,
        CancellationToken cancellationToken = default)
    {
        var key = defect?.Trim() ?? "";
        if (key.Length == 0) throw new McpException("กรุณาระบุรหัส Defect");
        var (userId, scope) = await ScopeAsync(null, null, cancellationToken);
        var code = key.ToUpperInvariant();
        Guid? byId = Guid.TryParse(key, out var parsed) ? parsed : null;
        var found = await db.Defects.AsNoTracking()
            .Where(x => !x.IsDeleted && scope.AllowedProjectIds.Contains(x.ProjectId) && (byId.HasValue ? x.DefectId == byId.Value : x.DefectCode == code))
            .Select(x => new { x.DefectId, x.DefectCode }).FirstOrDefaultAsync(cancellationToken);
        await AuditAsync(userId, "get_defect", $"{key} → {(found is null ? "ไม่พบ" : found.DefectCode)}", cancellationToken);
        if (found is null) throw new McpException($"ไม่พบ Defect \"{key}\" ใน Project ที่คุณเป็นสมาชิก");
        if (await db.KnowledgeDocuments.AsNoTracking().AnyAsync(x => x.SourceType == "Defect" && x.SourceKey == found.DefectId.ToString() && x.ExcludeFromAi, cancellationToken))
            throw new McpException($"Defect {found.DefectCode} ถูกตั้งค่าไม่ให้ส่งข้อมูลให้ AI");
        var text = (await indexer.BuildDefectTextsByIdAsync([found.DefectId], cancellationToken)).GetValueOrDefault(found.DefectId)
            ?? throw new McpException($"ไม่พบ Defect \"{key}\"");
        var url = WebUrl(KnowledgeIndexService.DefectUrl(found.DefectId));
        return Limit($"{SensitiveDataMasker.Mask(text.Header)}\n{SensitiveDataMasker.Mask(text.Body)}\n\nลิงก์: {url}");
    }

    [McpServerTool(Name = "get_article", Title = "อ่านบทความความรู้", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get the full content of a published knowledge article (how-to, FAQ, troubleshooting) by its id from search_knowledge.")]
    public async Task<string> GetArticle(
        [Description("Article id (GUID) from search_knowledge")] string articleId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(articleId?.Trim(), out var id)) throw new McpException("article_id ต้องเป็น GUID จาก search_knowledge");
        var (userId, scope) = await ScopeAsync(null, null, cancellationToken);
        var article = await db.KnowledgeArticles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.KnowledgeArticleId == id && !x.IsDeleted && x.Status == "Published" && (x.ProjectId == null || scope.AllowedProjectIds.Contains(x.ProjectId.Value)), cancellationToken);
        await AuditAsync(userId, "get_article", $"{id} → {(article is null ? "ไม่พบ" : Cut(article.Title, 60))}", cancellationToken);
        if (article is null) throw new McpException("ไม่พบบทความ หรือคุณไม่มีสิทธิ์ใน Project ของบทความนี้");
        if (await db.KnowledgeDocuments.AsNoTracking().AnyAsync(x => x.SourceType == "Article" && x.SourceKey == id.ToString() && x.ExcludeFromAi, cancellationToken))
            throw new McpException("บทความนี้ถูกตั้งค่าไม่ให้ส่งข้อมูลให้ AI");
        var projectName = article.ProjectId.HasValue ? await db.Projects.AsNoTracking().Where(x => x.ProjectId == article.ProjectId).Select(x => x.ProjectName).FirstOrDefaultAsync(cancellationToken) : null;
        var text = KnowledgeSourceText.ForArticle(new(article.Title, article.Tags, projectName, article.BodyMarkdown));
        return Limit($"{SensitiveDataMasker.Mask(text.Header)}\n{SensitiveDataMasker.Mask(text.Body)}\n\nลิงก์: {WebUrl(KnowledgeIndexService.ArticleUrl(id))}");
    }

    [McpServerTool(Name = "get_document", Title = "อ่านเอกสารในคลังความรู้", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get the extracted text of an uploaded document (PDF/DOCX/Markdown — e.g. module specification, requirement, manual) by its id from search_knowledge. Long documents are split into parts; use 'part' to read further.")]
    public async Task<string> GetDocument(
        [Description("Document id (GUID) from search_knowledge")] string documentId,
        [Description("Part number for long documents (1 = first ~7,600 characters)")] int part = 1,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(documentId?.Trim(), out var id)) throw new McpException("document_id ต้องเป็น GUID จาก search_knowledge");
        var (userId, scope) = await ScopeAsync(null, null, cancellationToken);
        var file = await db.KnowledgeFiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.KnowledgeFileId == id && !x.IsDeleted && x.ExtractedText != null && (x.ProjectId == null || scope.AllowedProjectIds.Contains(x.ProjectId.Value)), cancellationToken);
        await AuditAsync(userId, "get_document", $"{id} part {part} → {(file is null ? "ไม่พบ" : Cut(file.FileName, 60))}", cancellationToken);
        if (file is null) throw new McpException("ไม่พบเอกสาร หรือคุณไม่มีสิทธิ์ใน Project ของเอกสารนี้");
        if (await db.KnowledgeDocuments.AsNoTracking().AnyAsync(x => x.SourceType == "File" && x.SourceKey == id.ToString() && x.ExcludeFromAi, cancellationToken))
            throw new McpException("เอกสารนี้ถูกตั้งค่าไม่ให้ส่งข้อมูลให้ AI");
        var text = SensitiveDataMasker.Mask(file.ExtractedText!);
        var size = MaxOutputChars - 400;
        var parts = Math.Max(1, (int)Math.Ceiling(text.Length / (double)size));
        if (part < 1 || part > parts) throw new McpException($"เอกสารนี้มี {parts} ส่วน — part ต้องอยู่ระหว่าง 1–{parts}");
        var slice = text.Substring((part - 1) * size, Math.Min(size, text.Length - (part - 1) * size));
        var next = part < parts ? $"อ่านต่อ: get_document(\"{id}\", part: {part + 1}) · " : "";
        return $"[เอกสาร] {file.FileName} — ส่วนที่ {part}/{parts}\n{slice}\n\n{next}ลิงก์: {WebUrl(KnowledgeIndexService.FileUrl(id))}";
    }

    [McpServerTool(Name = "get_crm_ticket", Title = "รายละเอียด CRM Ticket", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get a customer support (CRM) ticket from the knowledge base: problem description and support answers/solution, with customer and staff names masked. Only available when the administrator allows CRM data for external AI.")]
    public async Task<string> GetCrmTicket(
        [Description("CRM Job No. from search_knowledge")] string jobNo,
        CancellationToken cancellationToken = default)
    {
        var key = jobNo?.Trim() ?? "";
        if (key.Length == 0) throw new McpException("กรุณาระบุ Job No.");
        var (userId, scope) = await ScopeAsync(null, null, cancellationToken);
        if (!scope.IncludeCrm) throw new McpException(CrmNotAllowed);
        var text = (await indexer.BuildCrmTextsByJobNoAsync([key], cancellationToken)).GetValueOrDefault(key);
        await AuditAsync(userId, "get_crm_ticket", $"{key} → {(text is null ? "ไม่พบ" : "พบ")}", cancellationToken);
        if (text is null) throw new McpException($"ไม่พบ CRM Ticket \"{key}\" ในคลังความรู้");
        if (await db.KnowledgeDocuments.AsNoTracking().AnyAsync(x => x.SourceType == "CrmTicket" && x.SourceKey == key && x.ExcludeFromAi, cancellationToken))
            throw new McpException($"Ticket {key} ถูกตั้งค่าไม่ให้ส่งข้อมูลให้ AI");
        return Limit($"{SensitiveDataMasker.Mask(text.Header)}\n{SensitiveDataMasker.Mask(text.Body)}\n\nลิงก์: {WebUrl(KnowledgeIndexService.CrmUrl(key))}");
    }

    [McpServerTool(Name = "find_similar_defects", Title = "หา Defect ที่คล้ายกัน", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Find existing Defects similar to a bug description — use before reporting a new Defect to avoid duplicates. Returns codes, status, severity and links.")]
    public async Task<string> FindSimilarDefects(
        [Description("Bug symptom or description (Thai or English)")] string description,
        [Description("Optional project code from list_projects")] string? projectCode = null,
        [Description("Max results (1-10, default 5)")] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var q = KnowledgeQuery.Normalize(description) ?? throw new McpException("กรุณาระบุอาการหรือรายละเอียดของปัญหา");
        var (userId, scope) = await ScopeAsync(projectCode, ["Defect"], cancellationToken);
        var results = await search.SearchAsync(q, scope, Math.Clamp(limit, 1, 10), cancellationToken, forAi: true);
        await AuditAsync(userId, "find_similar_defects", $"\"{Cut(q, 80)}\" → {results.Count} รายการ", cancellationToken);
        if (results.Count == 0) return "ไม่พบ Defect ที่คล้ายกัน";
        var defects = await DefectStatusAsync(results.Select(x => x.SourceKey), cancellationToken);
        var sb = new StringBuilder($"Defect ที่อาจซ้ำหรือคล้ายกัน ({results.Count} รายการ — เรียงตามความใกล้เคียงของข้อความ):\n");
        var n = 0;
        foreach (var r in results)
        {
            defects.TryGetValue(r.SourceKey, out var d);
            sb.Append($"\n{++n}. {r.Title}\n   {r.ProjectName ?? "-"}{(d is null ? "" : $" · สถานะ {d.Status} · ความรุนแรง {d.Severity}")}\n");
            sb.Append("   ").Append(SensitiveDataMasker.Mask(r.Snippet).ReplaceLineEndings(" ")).Append('\n');
            if (WebUrl(r.SourceUrl) is { } url) sb.Append("   ลิงก์: ").Append(url).Append('\n');
        }
        return Limit(sb.ToString());
    }

    // ---------- helpers ----------

    private async Task<(Guid UserId, KnowledgeSearchScope Scope)> ScopeAsync(string? projectCode, IReadOnlyCollection<string>? sources, CancellationToken ct)
    {
        var user = http.HttpContext?.User ?? throw new McpException("ไม่พบข้อมูลผู้ใช้");
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) throw new McpException("ไม่พบข้อมูลผู้ใช้");
        var scope = await search.ScopeForAsync(user, userId, null, sources, ct);
        // Claude เป็น AI ภายนอกเสมอ — ข้อมูล CRM เฉพาะเมื่อผู้ดูแลอนุญาตส่ง CRM ให้ AI ภายนอก (และผู้ใช้มี CRM.VIEW จาก ScopeForAsync)
        if (scope.IncludeCrm)
        {
            var allow = await db.AiConfigurations.AsNoTracking().OrderByDescending(x => x.UpdatedAt).Select(x => x.AllowCrmDataToExternalAi).FirstOrDefaultAsync(ct);
            if (!allow) scope = scope with { IncludeCrm = false };
        }
        if (!string.IsNullOrWhiteSpace(projectCode))
        {
            var code = projectCode.Trim().ToUpperInvariant();
            var projectId = await db.Projects.AsNoTracking().Where(x => x.ProjectCode.ToUpper() == code).Select(x => (Guid?)x.ProjectId).FirstOrDefaultAsync(ct);
            if (projectId is null || !scope.AllowedProjectIds.Contains(projectId.Value)) throw new McpException($"ไม่พบ Project \"{projectCode}\" ที่คุณเป็นสมาชิก — ดูรายชื่อด้วย list_projects");
            scope = scope with { ProjectId = projectId };
        }
        return (userId, scope);
    }

    private async Task<Dictionary<string, DefectStatus>> DefectStatusAsync(IEnumerable<string> sourceKeys, CancellationToken ct)
    {
        var ids = sourceKeys.Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Defects.AsNoTracking().Where(x => ids.Contains(x.DefectId))
            .ToDictionaryAsync(x => x.DefectId.ToString(), x => new DefectStatus(x.DefectCode, x.Status, x.Severity), ct);
    }

    private const string CrmNotAllowed = "ข้อมูล CRM ใช้กับ Claude ไม่ได้ — ต้องมีสิทธิ์ CRM.VIEW และผู้ดูแลต้องเปิด \"อนุญาตส่งข้อมูล CRM ให้ AI ภายนอก\" ใน Setting Center";

    private sealed record DefectStatus(string Code, string Status, string Severity);

    private async Task AuditAsync(Guid userId, string tool, string summary, CancellationToken ct)
    {
        var prefix = http.HttpContext?.User.FindFirstValue("token_prefix");
        var ip = http.HttpContext?.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        db.AuditLogs.Add(new AuditLog(userId, "Called", AuditEntityType, tool, Cut($"Claude (MCP) {tool}: {summary} · token {prefix}…", 1000), null, null, ip));
        await db.SaveChangesAsync(ct);
    }

    private string? WebUrl(string? sourceUrl) =>
        string.IsNullOrWhiteSpace(sourceUrl) ? null : $"{(configuration["PublicWebBaseUrl"] ?? "https://promaxx2.qahub.store").TrimEnd('/')}/{sourceUrl.TrimStart('/')}";

    private static string Limit(string text) => text.Length <= MaxOutputChars ? text : text[..MaxOutputChars] + "\n\n…(ตัดข้อความที่ยาวเกิน — ดูฉบับเต็มจากลิงก์)";
    private static string Cut(string value, int max) => value.Length > max ? value[..max] + "…" : value;
}
