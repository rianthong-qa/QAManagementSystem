using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ProMaxx2.QA.Api.Services;

public sealed record BlueIdUserDto(string StaffCode, string Name, string? Email);

// แถวหนึ่งจาก /Support/HelpDeskAnswerMain — ข้อความ/ไฟล์แนบในเคส ใช้สำหรับ Phase 2 poller ฝั่ง CRM → QA Hub
// (ดู CrmSyncService.PollCommentsAsync) fanswerType: "A"/"D" = ข้อความ, "P" = รูป/ไฟล์แนบ (ดู JobDetail.txt เดิม)
public sealed record CrmHelpDeskAnswer(string AnswerNo, string Description, string Posted, string? AnsDate, string FAnswerType, string? Image);

// The 21 CRM "Create Job" fields, locked with the CRM/QA teams — see Document/03-Architecture-and-Plan/CRM_INTEGRATION_PLAN.md §5.
// ToFormFields() is the single place that maps this record to the multipart/form-data field names CRM expects.
public sealed record CrmCreateJobPayload(
    string Subject, string Member, string FName, string LName, string Tel, string Email,
    string SysCustomerType, string RecipientId, string OwnerSubjectId, string Assignto, string SysDevelop,
    string Status, string Source, string BranchId, string SysserViceType, string SysFollowupId,
    string SysProductId, string SysVersionId, string SysOsId, string Description, string Posted, string JobType,
    string ContactDate, string Duedate)
{
    public IReadOnlyDictionary<string, string> ToFormFields() => new Dictionary<string, string>
    {
        ["Subject"] = Subject, ["Member"] = Member, ["FName"] = FName, ["LName"] = LName, ["Tel"] = Tel, ["Email"] = Email,
        ["SysCustomerType"] = SysCustomerType, ["RecipientId"] = RecipientId, ["OwnerSubjectId"] = OwnerSubjectId,
        ["Assignto"] = Assignto, ["SysDevelop"] = SysDevelop, ["Status"] = Status, ["Source"] = Source, ["BranchId"] = BranchId,
        ["SysserViceType"] = SysserViceType, ["SysFollowupId"] = SysFollowupId, ["SysProductId"] = SysProductId,
        ["SysVersionId"] = SysVersionId, ["SysOsId"] = SysOsId, ["Description"] = Description, ["Posted"] = Posted, ["JobType"] = JobType,
        // ไม่เคยส่ง field นี้มาก่อน — CRM เลย default เป็น epoch 0 (แสดงเป็น 01/01/2513 07:00) ต้องส่งวันเวลาปัจจุบันเสมอ
        ["ContactDate"] = ContactDate,
        // เหมือนกัน — ไม่เคยส่ง Duedate ตอนสร้าง ticket มาก่อน (เป็น epoch 0 เหมือน ContactDate) ตอนนี้ส่งวันที่
        // ปัจจุบันเวลาเที่ยงคืนเป็นค่าเริ่มต้น (yyyy-M-d'T'00:00:00 ปีเป็น ค.ศ. ไม่ padding เดือน/วัน เหมือน ContactDate)
        ["Duedate"] = Duedate,
    };
}

// CRM has no separate "add note" endpoint — updating an existing job re-submits every field via the same
// PUT /Support endpoint used to create it (see JobDetailsHD's SubmitUpdate()). Reverse-engineered field list from
// that page's formdata.append(...) calls — mirrors CrmCreateJobPayload but carries a JobNo and the extra fields
// (NickName/Fax/RefJobNo/Duedate/BuildDetail) that only exist on the update form, not the create form.
public sealed record CrmUpdateJobPayload(
    string JobNo, string Subject, string Member, string SysCustomerType, string FName, string LName, string NickName,
    string Fax, string Tel, string Email, string Assignto, string RecipientId, string OwnerSubjectId, string Status,
    string Source, string RefJobNo, string SysBranchId, string Description, string SysserViceType, string SysProductId,
    string Duedate, string SysVersionId, string BuildDetail, string SysOsId, string SysFollowupId, string SysDevelop,
    string Posted, string JobType)
{
    public IReadOnlyDictionary<string, string> ToFormFields() => new Dictionary<string, string>
    {
        ["JobNo"] = JobNo, ["Subject"] = Subject, ["Member"] = Member, ["SysCustomerType"] = SysCustomerType,
        ["FName"] = FName, ["LName"] = LName, ["NickName"] = NickName, ["Fax"] = Fax, ["Tel"] = Tel, ["Email"] = Email,
        ["Assignto"] = Assignto, ["RecipientId"] = RecipientId, ["OwnerSubjectId"] = OwnerSubjectId, ["Status"] = Status,
        ["Source"] = Source, ["RefJobNo"] = RefJobNo, ["SysBranchId"] = SysBranchId, ["Description"] = Description,
        ["SysserViceType"] = SysserViceType, ["SysProductId"] = SysProductId, ["Duedate"] = Duedate,
        ["SysVersionId"] = SysVersionId, ["BuildDetail"] = BuildDetail, ["SysOsId"] = SysOsId,
        ["SysFollowupId"] = SysFollowupId, ["SysDevelop"] = SysDevelop, ["Posted"] = Posted, ["JobType"] = JobType,
    };
}

// Thin HTTP wrapper around the CRM (BlueSea Helpdesk, booklicenceapi) and BlueID user directory endpoints
// reverse-engineered from the CRM's own front-end (see CRM_INTEGRATION_PLAN.md §4). Credentials/base URLs come
// from CrmConfigurationService; auth tokens from CrmTokenService.
public sealed class CrmApiClient(
    IHttpClientFactory clients,
    CrmTokenService tokenService,
    CrmConfigurationService crmConfig,
    IMemoryCache cache,
    ILogger<CrmApiClient> logger)
{
    // ทุก call ไป CRM/BlueID: timeout สั้นกว่า default 100 วินาทีของ HttpClient และจำกัดขนาด response ที่อ่านเข้าหน่วยความจำ
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    public const long MaxResponseBytes = 20 * 1024 * 1024;
    // HelpDeskExport ส่งผลทั้งช่วงวันที่มาทีเดียว — cache body ต่อผู้ใช้+ตัวกรองไว้สั้น ๆ ให้การเปลี่ยนหน้า/ค้นหาไม่ดาวน์โหลดซ้ำ
    public static readonly TimeSpan TicketListCacheLifetime = TimeSpan.FromSeconds(60);
    public const int MaxTicketListRangeDays = 366;
    private sealed record CachedTicketList(string Body, DateTimeOffset FetchedAt);
    // Fixed for this one integration — not admin-configurable (see CrmConfiguration.cs).
    private const string BaseUrl = "https://bluesea.seniorsoft.com/booklicenceapi";
    private const string BlueIdUserDirectoryUrl = "https://blueid.seniorsoft.com/blueidapi/UserAccount/DWUserAccountSeniorV2";
    private const int HelpDeskAnswerPageSize = 100;
    private const int HelpDeskAnswerMaxPages = 10;

    /// <summary>Validates the per-user CRM token against a read-only lookup endpoint.</summary>
    public async Task ProbeConnectionAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            var body = await GetBodyWithRetryAsync($"{BaseUrl}/Support/SysSrviceType", userId, ct);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
                throw new CrmBadResponseException("CRM ส่งผลลัพธ์ Connection Probe ไม่ใช่ JSON object หรือ array");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new CrmTimeoutException("CRM ใช้เวลาตอบกลับนานเกินกำหนด กรุณาลองใหม่อีกครั้ง", ex);
        }
        catch (JsonException ex)
        {
            throw new CrmBadResponseException("CRM ส่งผลลัพธ์ Connection Probe ไม่ใช่ JSON ที่รองรับ", ex);
        }
    }

    public async Task<string> ResolveBugServiceTypeIdAsync(Guid userId, CancellationToken ct)
    {
        var body = await GetBodyWithRetryAsync($"{BaseUrl}/Support/SysSrviceType", userId, ct);
        using var doc = JsonDocument.Parse(body);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("serviceName", out var name) && string.Equals(name.GetString(), "Bug", StringComparison.OrdinalIgnoreCase))
                return JsonElementToString(item.GetProperty("sysServiceType"));
        }
        throw new CrmIntegrationException("CRM ไม่มีประเภทงาน 'Bug' ใน SysSrviceType กรุณาตรวจสอบฝั่ง CRM");
    }

    // SysFollowupId ไม่มีค่า "0/ไม่ระบุ" ให้ใช้ (ตัวเลข 1-5 ที่เห็นในหน้า "New Job" เป็นแค่ hardcode ในฟอร์มนั้น
    // เฉยๆ ไม่ใช่ ID จริงจากตาราง FOLLOWUP — หน้า "Update Job" ดึงค่าจริงจาก /Support/Followup ต่างหาก และยืนยัน
    // แล้วว่า "1" ก็ยัง violate FK) — resolve ID แรกที่ CRM คืนมาจริง (พยายามหาอันที่ชื่อ "ตกลง" ก่อน ถ้าไม่เจอ
    // ก็ใช้ตัวแรกสุดในลิสต์ เหมือนพฤติกรรม browser ตอนไม่ได้เลือกอะไรในดรอปดาวน์)
    public async Task<string> ResolveDefaultFollowupIdAsync(Guid userId, CancellationToken ct)
    {
        var body = await GetBodyWithRetryAsync($"{BaseUrl}/Support/Followup", userId, ct);
        using var doc = JsonDocument.Parse(body);
        var items = doc.RootElement.EnumerateArray().ToList();
        if (items.Count == 0) throw new CrmIntegrationException("CRM ไม่มีข้อมูล Followup ใน /Support/Followup กรุณาตรวจสอบฝั่ง CRM");
        var match = items.FirstOrDefault(x => x.TryGetProperty("followUpName", out var n) && string.Equals(n.GetString(), "ตกลง", StringComparison.Ordinal));
        var chosen = match.ValueKind == JsonValueKind.Undefined ? items[0] : match;
        return JsonElementToString(chosen.GetProperty("sysFollowUpID"));
    }

    // sysServiceType (และ field อื่นๆ ที่คล้ายกันจาก CRM) มาเป็น JSON number จริง ไม่ใช่ string ตามที่เดาไว้แต่แรก
    // (.GetString() throw ตรงๆ ถ้าเป็น number ไม่ใช่ return null ที่จะให้ ?? ทำงานต่อได้) — แปลงแบบรองรับทั้งคู่
    private static string JsonElementToString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? "",
        JsonValueKind.Number => el.GetRawText(),
        _ => el.GetRawText(),
    };

    // Public เพราะ CrmSendToCrmService ต้องอ่าน field ของ job snapshot ที่ได้จาก GetJobDetailAsync ด้วยเช่นกัน
    // ตอนประกอบ payload ของ UpdateSupportJobAsync (carry-over ทุก field เดิม ยกเว้น Description ที่แก้)
    public static string GetFieldAsString(JsonElement obj, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!obj.TryGetProperty(propertyName, out var prop) || prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            return JsonElementToString(prop);
        }
        return "";
    }

    private static string? GetNullableFieldAsString(JsonElement obj, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!obj.TryGetProperty(propertyName, out var prop) || prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            return JsonElementToString(prop);
        }
        return null;
    }

    // ดึง snapshot ปัจจุบันของ job ทั้งใบจาก CRM — จำเป็นก่อน UpdateSupportJobAsync เสมอ เพราะ PUT /Support
    // เป็นการเขียนทับทั้ง object ไม่ใช่ partial update ต้อง carry-over ทุก field เดิมมาด้วย ไม่ใช่แค่ field ที่จะแก้
    public async Task<JsonElement> GetJobDetailAsync(Guid userId, string jobNo, string jobType, CancellationToken ct)
    {
        var body = await GetBodyWithRetryAsync($"{BaseUrl}/Support/HelpDesksJob?JobNo={Uri.EscapeDataString(jobNo)}&JobType={Uri.EscapeDataString(jobType)}", userId, ct);
        using var doc = JsonDocument.Parse(body);
        return ParseJobDetail(doc.RootElement, jobNo);
    }

    public static JsonElement ParseJobDetail(JsonElement raw, string jobNo)
    {
        var first = FindJob(raw);
        if (first.ValueKind == JsonValueKind.Undefined)
            throw new CrmIntegrationException($"ไม่พบ Job {jobNo} ใน CRM หรือ Response ไม่ใช่รูปแบบที่รองรับ");
        return first.Clone();
    }

    private static JsonElement FindJob(JsonElement value)
    {
        if (IsJob(value)) return value;
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                if (IsJob(item)) return item;
            return default;
        }
        if (value.ValueKind != JsonValueKind.Object) return default;

        foreach (var wrapperName in new[] { "data", "result", "job", "helpDeskJob", "jobs" })
        {
            if (!value.TryGetProperty(wrapperName, out var wrapped)) continue;
            var found = FindJob(wrapped);
            if (found.ValueKind != JsonValueKind.Undefined) return found;
        }
        return default;
    }

    private static bool IsJob(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        !string.IsNullOrWhiteSpace(CrmTicketListParser.ReadString(value, "jobNo", "JobNo", "jobno"));

    // ดึงข้อความ/ไฟล์แนบทั้งหมดในเคส (JobDetailsHD ใช้ endpoint นี้ผ่าน DataTables serverSide ajax — dataSrc:
    // 'helpDeskAnswers') สำหรับ Phase 2 poller ฝั่ง CRM → QA Hub (ดู CrmSyncService.PollCommentsAsync) —
    // draw/start/length เป็น parameter มาตรฐานของ DataTables server-side ที่หน้า JobDetailsHD ส่งไปด้วยเสมอ
    // Adapter ดึงต่อได้สูงสุด 10 หน้า (1,000 รายการ) และหยุดทันทีเมื่อหมดหน้า/ได้ข้อมูลซ้ำ เพื่อป้องกัน
    // response ที่ CRM ไม่รองรับ pagination ไม่ให้เกิด loop หรือยิง request ไม่จำกัด
    public async Task<IReadOnlyList<CrmHelpDeskAnswer>> GetHelpDeskAnswersAsync(Guid userId, string jobNo, CancellationToken ct)
    {
        var result = new List<CrmHelpDeskAnswer>();
        var seenAnswerNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var page = 0; page < HelpDeskAnswerMaxPages; page++)
        {
            var start = page * HelpDeskAnswerPageSize;
            var body = await GetBodyWithRetryAsync(
                $"{BaseUrl}/Support/HelpDeskAnswerMain?DetailJobNo={Uri.EscapeDataString(jobNo)}&draw=1&start={start}&length={HelpDeskAnswerPageSize}", userId, ct);
            using var doc = JsonDocument.Parse(body);
            var pageItems = ParseHelpDeskAnswers(doc.RootElement);
            var added = 0;
            foreach (var item in pageItems)
            {
                if (!seenAnswerNos.Add(item.AnswerNo)) continue;
                result.Add(item);
                added++;
            }
            if (pageItems.Count < HelpDeskAnswerPageSize || added == 0) break;
        }
        return result;
    }

    public static IReadOnlyList<CrmHelpDeskAnswer> ParseHelpDeskAnswers(JsonElement raw)
    {
        var answers = FindAnswerArray(raw);
        if (answers.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<CrmHelpDeskAnswer>();
        foreach (var item in answers.EnumerateArray())
        {
            var answerNo = GetFieldAsString(item, "answerNo", "AnswerNo");
            if (string.IsNullOrWhiteSpace(answerNo)) continue; // ไม่มี answerNo ก็ไม่รู้จะเทียบว่าใหม่/เก่ายังไง ข้าม
            result.Add(new CrmHelpDeskAnswer(
                answerNo,
                GetFieldAsString(item, "description", "Description"),
                GetFieldAsString(item, "posted", "Posted"),
                GetNullableFieldAsString(item, "ansDate", "AnsDate"),
                GetFieldAsString(item, "fanswerType", "FAnswerType"),
                GetNullableFieldAsString(item, "image", "Image")));
        }
        return result;
    }

    private static JsonElement FindAnswerArray(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value;
        if (value.ValueKind != JsonValueKind.Object) return default;
        foreach (var wrapperName in new[] { "helpDeskAnswers", "answers", "data", "result" })
        {
            if (!value.TryGetProperty(wrapperName, out var wrapped)) continue;
            var found = FindAnswerArray(wrapped);
            if (found.ValueKind != JsonValueKind.Undefined) return found;
        }
        return default;
    }

    // เหมือน CreateSupportJobAsync แต่เป็น PUT (CRM ไม่มี endpoint แก้ไข/เพิ่มโน้ตแยกต่างหาก — ใช้ endpoint
    // เดียวกับตอนสร้าง ticket เขียนทับทั้งใบเสมอ ดู CrmUpdateJobPayload ด้านบน)
    public async Task UpdateSupportJobAsync(Guid userId, CrmUpdateJobPayload payload, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        foreach (var (key, value) in payload.ToFormFields()) content.Add(new StringContent(value ?? ""), key);
        using var request = await AuthorizedAsync(HttpMethod.Put, $"{BaseUrl}/Support", userId, ct);
        request.Content = content;
        await SendAsync(request, userId, ct); // ไม่ต้อง parse response กลับ — ไม่มี JobNo ใหม่ให้ต้องอ่าน (JobNo เดิมอยู่แล้ว)
    }

    public async Task<string> CreateSupportJobAsync(Guid userId, CrmCreateJobPayload payload, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        foreach (var (key, value) in payload.ToFormFields()) content.Add(new StringContent(value ?? ""), key);
        using var request = await AuthorizedAsync(HttpMethod.Post, $"{BaseUrl}/Support", userId, ct);
        request.Content = content;
        var body = await SendAsync(request, userId, ct);
        return ExtractJobNo(body);
    }

    public async Task<IReadOnlyList<BlueIdUserDto>> GetSeniorUserDirectoryAsync(Guid userId, CancellationToken ct)
    {
        var body = await GetBodyWithRetryAsync(BlueIdUserDirectoryUrl, userId, ct);
        using var doc = JsonDocument.Parse(body);
        var result = new List<BlueIdUserDto>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var staffCode = item.TryGetProperty("seniorSoftID", out var idProp) ? JsonElementToString(idProp) : null; // seniorSoftID can come back as a JSON number, not always a string
            if (string.IsNullOrWhiteSpace(staffCode)) continue;
            var name = item.TryGetProperty("fullName", out var nameProp) ? nameProp.GetString()?.Trim() ?? staffCode : staffCode;
            var email = item.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
            result.Add(new BlueIdUserDto(staffCode, name, email));
        }
        return result;
    }

    /// <summary>
    /// Reads CRM HelpDeskExport for the currently logged-in CRM account. The CRM endpoint returns
    /// the complete filtered result, so QA Hub applies the final user-scope filter and pagination
    /// locally until upstream pagination is verified against the production contract.
    /// </summary>
    public async Task<CrmTicketListResult> ListJobsAsync(Guid userId, CrmTicketListQuery query, CancellationToken ct, bool refresh = false)
    {
        try
        {
            var (cfg, _) = await crmConfig.GetRuntimeAsync(userId, ct);
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            var from = query.From ?? today.AddDays(-30);
            var to = query.To ?? today;
            if (to < from) throw new ArgumentException("วันที่สิ้นสุดต้องไม่น้อยกว่าวันที่เริ่มต้น");
            if (to.DayNumber - from.DayNumber > MaxTicketListRangeDays)
                throw new ArgumentException($"ช่วงวันที่ต้องไม่เกิน {MaxTicketListRangeDays} วัน");
            var cacheKey = $"crm:list:{userId:N}:{cfg.Username}:{query.Status?.Trim()}:{from:yyyyMMdd}:{to:yyyyMMdd}";

            var payload = new
            {
                SJobtype = "0",
                SService = "",
                SProduct = "",
                SStatus = string.IsNullOrWhiteSpace(query.Status) ? Array.Empty<string>() : new[] { query.Status.Trim() },
                SSysCustomer = Array.Empty<string>(),
                SCaseModify = Array.Empty<string>(),
                SFstatus = Array.Empty<string>(),
                SFModify = Array.Empty<string>(),
                SPayMent = Array.Empty<string>(),
                SSysUnregisterlist = Array.Empty<string>(),
                SBranch = "",
                SRecipientId = (string?)null,
                SOwnerSubject = (string?)null,
                SAssignTo = cfg.Username,
                SContactDateS = from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                SContactDateE = to.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                SDueDateS = "",
                SDueDateE = "",
                SBegDateModifyS = "",
                SBegDateModifyE = "",
                SEndDateModifyS = "",
                SEndDateModifyE = ""
            };

            if (refresh || !cache.TryGetValue(cacheKey, out CachedTicketList? cached) || cached is null)
            {
                string fresh;
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        using var request = await AuthorizedAsync(HttpMethod.Post, $"{BaseUrl}/Support/HelpDeskExport", userId, ct);
                        request.Content = JsonContent.Create(payload);
                        fresh = await SendAsync(request, userId, ct);
                        break;
                    }
                    catch (CrmIntegrationException ex) when (ex.RemoteStatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                    {
                        // SendAsync invalidates the per-user token on 401; the next attempt obtains a fresh token.
                    }
                }
                cached = new CachedTicketList(fresh, DateTimeOffset.UtcNow);
                cache.Set(cacheKey, cached, TicketListCacheLifetime);
            }
            var body = cached.Body;
            try
            {
                var result = CrmTicketListParser.Parse(body, query with { From = from, To = to }, cfg.Username, cached.FetchedAt);
                if (result.Total == 0)
                {
                    var diagnostics = CrmTicketListParser.Diagnose(body);
                    logger.LogWarning(
                        "CRM HelpDeskExport returned zero scoped tickets. Root={RootKind}, Records={RecordCount}, JobNo={RecordsWithJobNo}, Assignee={RecordsWithAssignee}, ContactDate={RecordsWithContactDate}, Fields={Fields}",
                        diagnostics.RootKind,
                        diagnostics.RecordCount,
                        diagnostics.RecordsWithJobNo,
                        diagnostics.RecordsWithAssignee,
                        diagnostics.RecordsWithContactDate,
                        string.Join(',', diagnostics.SamplePropertyNames));
                }

                return result;
            }
            catch (JsonException ex)
            {
                throw new CrmBadResponseException("CRM ส่งข้อมูลรายการงานไม่ใช่ JSON ที่ระบบรองรับ", ex);
            }
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new CrmTimeoutException("CRM ใช้เวลาตอบกลับนานเกินกำหนด กรุณาลองใหม่อีกครั้ง", ex);
        }
    }

    // The exact response shape of POST /Support (raw JobNo string vs. a JSON object wrapping it) hasn't been
    // confirmed against the real CRM yet — isolated here so that's a one-line fix at integration-test time, not
    // a change to the orchestration/controller layer.
    private static string ExtractJobNo(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            try { return JsonSerializer.Deserialize<string>(trimmed) ?? throw new CrmIntegrationException("CRM ส่ง JobNo กลับมาเป็นค่าว่าง"); }
            catch (JsonException) { /* fall through to plain-string handling below */ }
        }
        if (trimmed.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(trimmed);
            foreach (var key in new[] { "jobNo", "JobNo", "jobno" })
                if (doc.RootElement.TryGetProperty(key, out var value)) return value.GetString() ?? throw new CrmIntegrationException("CRM ส่ง JobNo กลับมาเป็นค่าว่าง");
            throw new CrmIntegrationException($"CRM ตอบกลับ JSON ที่ไม่มี field JobNo ที่รู้จัก: {trimmed}");
        }
        if (string.IsNullOrWhiteSpace(trimmed)) throw new CrmIntegrationException("CRM ไม่ได้ส่ง JobNo กลับมา");
        return trimmed; // plain-text JobNo, e.g. "BHD690831000034"
    }

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string url, Guid userId, CancellationToken ct)
    {
        var (cfg, password) = await crmConfig.GetRuntimeAsync(userId, ct);
        var token = await tokenService.GetTokenAsync(userId, cfg.MerchantId, cfg.Username, password, ct);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new("Bearer", token.AccessToken);
        return request;
    }

    private async Task<string> GetBodyWithRetryAsync(string url, Guid userId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = await AuthorizedAsync(HttpMethod.Get, url, userId, ct);
            try
            {
                return await SendAsync(request, userId, ct);
            }
            catch (CrmIntegrationException ex) when (ex.RemoteStatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                // SendAsync invalidates the per-user token on 401; retry once with a fresh token.
            }
        }
    }

    private async Task<string> SendAsync(HttpRequestMessage request, Guid userId, CancellationToken ct)
    {
        using var client = clients.CreateClient();
        client.Timeout = RequestTimeout;
        client.MaxResponseContentBufferSize = MaxResponseBytes;
        HttpResponseMessage? sent = null;
        string body;
        try
        {
            sent = await client.SendAsync(request, ct);
            body = await sent.Content.ReadAsStringAsync(ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            sent?.Dispose();
            throw new CrmIntegrationException($"CRM ตอบกลับไม่ทันเวลา (เกิน {RequestTimeout.TotalSeconds:0} วินาที) กรุณาลองใหม่อีกครั้ง: {ex.Message}", HttpStatusCode.GatewayTimeout);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("buffer", StringComparison.OrdinalIgnoreCase))
        {
            sent?.Dispose();
            throw new CrmResultTooLargeException($"ข้อมูลจาก CRM มีขนาดเกิน {MaxResponseBytes / 1024 / 1024} MB กรุณาจำกัดช่วงวันที่ให้แคบลง");
        }
        catch (HttpRequestException ex)
        {
            sent?.Dispose();
            throw new CrmIntegrationException($"CRM ไม่พร้อมใช้งาน — เชื่อมต่อ CRM ไม่ได้ ({ex.HttpRequestError}) กรุณาลองใหม่ภายหลัง", HttpStatusCode.ServiceUnavailable);
        }
        using var response = sent;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenService.Invalidate(userId);
            throw new CrmIntegrationException("CRM ปฏิเสธ token (401) กรุณาลองใหม่อีกครั้ง", HttpStatusCode.Unauthorized);
        }
        if (!response.IsSuccessStatusCode) throw new CrmIntegrationException($"CRM ตอบกลับไม่สำเร็จ ({(int)response.StatusCode}): {body}", response.StatusCode);
        return body;
    }
}
