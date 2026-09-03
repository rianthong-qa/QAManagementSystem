using System.Net;
using System.Text.Json;

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
public sealed class CrmApiClient(IHttpClientFactory clients, CrmTokenService tokenService, CrmConfigurationService crmConfig)
{
    // Fixed for this one integration — not admin-configurable (see CrmConfiguration.cs).
    private const string BaseUrl = "https://bluesea.seniorsoft.com/booklicenceapi";
    private const string BlueIdUserDirectoryUrl = "https://blueid.seniorsoft.com/blueidapi/UserAccount/DWUserAccountSeniorV2";

    public async Task<string> ResolveBugServiceTypeIdAsync(Guid userId, CancellationToken ct)
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, $"{BaseUrl}/Support/SysSrviceType", userId, ct);
        var body = await SendAsync(request, userId, ct);
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
        using var request = await AuthorizedAsync(HttpMethod.Get, $"{BaseUrl}/Support/Followup", userId, ct);
        var body = await SendAsync(request, userId, ct);
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
    public static string GetFieldAsString(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop) || prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "";
        return JsonElementToString(prop);
    }

    // ดึง snapshot ปัจจุบันของ job ทั้งใบจาก CRM — จำเป็นก่อน UpdateSupportJobAsync เสมอ เพราะ PUT /Support
    // เป็นการเขียนทับทั้ง object ไม่ใช่ partial update ต้อง carry-over ทุก field เดิมมาด้วย ไม่ใช่แค่ field ที่จะแก้
    public async Task<JsonElement> GetJobDetailAsync(Guid userId, string jobNo, string jobType, CancellationToken ct)
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, $"{BaseUrl}/Support/HelpDesksJob?JobNo={Uri.EscapeDataString(jobNo)}&JobType={Uri.EscapeDataString(jobType)}", userId, ct);
        var body = await SendAsync(request, userId, ct);
        using var doc = JsonDocument.Parse(body);
        var first = doc.RootElement.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined) throw new CrmIntegrationException($"ไม่พบ Job {jobNo} ใน CRM");
        return first.Clone(); // Clone ก่อน doc ถูก dispose — JsonElement อ้างอิง buffer ของ JsonDocument เดิมอยู่
    }

    // ดึงข้อความ/ไฟล์แนบทั้งหมดในเคส (JobDetailsHD ใช้ endpoint นี้ผ่าน DataTables serverSide ajax — dataSrc:
    // 'helpDeskAnswers') สำหรับ Phase 2 poller ฝั่ง CRM → QA Hub (ดู CrmSyncService.PollCommentsAsync) —
    // draw/start/length เป็น parameter มาตรฐานของ DataTables server-side ที่หน้า JobDetailsHD ส่งไปด้วยเสมอ (เผื่อ
    // backend ต้องการ) ยังไม่ได้ยืนยันว่า backend บังคับให้ต้องมีครบทุกตัวจริงไหม — ถ้า CRM ตอบ 400 กลับมาต้องดู
    // response body ว่าขาด parameter ไหน (ดู SendAsync ที่แนบ body มากับ CrmIntegrationException เสมอ)
    public async Task<IReadOnlyList<CrmHelpDeskAnswer>> GetHelpDeskAnswersAsync(Guid userId, string jobNo, CancellationToken ct)
    {
        using var request = await AuthorizedAsync(HttpMethod.Get,
            $"{BaseUrl}/Support/HelpDeskAnswerMain?DetailJobNo={Uri.EscapeDataString(jobNo)}&draw=1&start=0&length=100", userId, ct);
        var body = await SendAsync(request, userId, ct);
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("helpDeskAnswers", out var answers) || answers.ValueKind != JsonValueKind.Array)
            return [];
        var result = new List<CrmHelpDeskAnswer>();
        foreach (var item in answers.EnumerateArray())
        {
            var answerNo = GetFieldAsString(item, "answerNo");
            if (string.IsNullOrWhiteSpace(answerNo)) continue; // ไม่มี answerNo ก็ไม่รู้จะเทียบว่าใหม่/เก่ายังไง ข้าม
            result.Add(new CrmHelpDeskAnswer(
                answerNo,
                GetFieldAsString(item, "description"),
                GetFieldAsString(item, "posted"),
                item.TryGetProperty("ansDate", out var ansDate) ? ansDate.GetString() : null,
                GetFieldAsString(item, "fanswerType"),
                item.TryGetProperty("image", out var image) ? image.GetString() : null));
        }
        return result;
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
        using var request = await AuthorizedAsync(HttpMethod.Get, BlueIdUserDirectoryUrl, userId, ct);
        var body = await SendAsync(request, userId, ct);
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

    private async Task<string> SendAsync(HttpRequestMessage request, Guid userId, CancellationToken ct)
    {
        using var client = clients.CreateClient();
        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenService.Invalidate(userId);
            throw new CrmIntegrationException("CRM ปฏิเสธ token (401) กรุณาลองใหม่อีกครั้ง");
        }
        if (!response.IsSuccessStatusCode) throw new CrmIntegrationException($"CRM ตอบกลับไม่สำเร็จ ({(int)response.StatusCode}): {body}");
        return body;
    }
}
