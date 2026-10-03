namespace ProMaxx2.QA.Api.Services;

public sealed record CrmStaffDto(string StaffCode, string Name);

/// <summary>
/// Cache รายชื่อพนักงาน (รหัส → ชื่อ) จาก BlueID directory สำหรับแสดง "6101 เหรียญทอง" แทนรหัสล้วนในหน้า CRM.
/// directory เป็นข้อมูลเดียวกันสำหรับทุกผู้ใช้และเปลี่ยนไม่บ่อย จึงเก็บแบบ singleton ร่วมกัน 1 ชั่วโมง — ผู้ใช้คนแรกที่ cache
/// หมดอายุเป็นคนดึงใหม่ด้วย token CRM ของตัวเอง; ไม่เก็บ/ไม่ส่ง email ออกไป
/// </summary>
public sealed class CrmStaffDirectoryCache(TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<CrmStaffDto>? staff;
    private DateTimeOffset expiresAt;

    public CrmStaffDirectoryCache() : this(TimeProvider.System) { }

    public async Task<IReadOnlyList<CrmStaffDto>> GetAsync(Func<CancellationToken, Task<IReadOnlyList<BlueIdUserDto>>> load, CancellationToken ct)
    {
        if (staff is { } hit && clock.GetUtcNow() < expiresAt) return hit;
        await gate.WaitAsync(ct);
        try
        {
            if (staff is { } fresh && clock.GetUtcNow() < expiresAt) return fresh;
            var loaded = await load(ct);
            staff = loaded
                .Where(x => !string.IsNullOrWhiteSpace(x.StaffCode))
                .GroupBy(x => x.StaffCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new CrmStaffDto(g.Key, g.First().Name.Trim()))
                .OrderBy(x => x.StaffCode, StringComparer.Ordinal)
                .ToArray();
            expiresAt = clock.GetUtcNow() + Lifetime;
            return staff;
        }
        finally { gate.Release(); }
    }
}
