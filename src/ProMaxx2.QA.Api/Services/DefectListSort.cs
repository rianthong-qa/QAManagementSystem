using ProMaxx2.QA.Domain.Defects;

namespace ProMaxx2.QA.Api.Services;

/// <summary>เรียงรายการ Defect ก่อนแบ่งหน้า (หัวคอลัมน์หน้า Defect) — sortBy = "&lt;field&gt;_&lt;asc|desc&gt;"
/// ค่าไม่รู้จัก/ว่าง = สร้างล่าสุดก่อน (ลำดับเดิม); ค่าเท่ากันเรียงต่อด้วยวันที่สร้างล่าสุดให้แบ่งหน้าแล้วลำดับคงที่</summary>
public static class DefectListSort
{
    public static IQueryable<Defect> Apply(IQueryable<Defect> query, string? sortBy)
    {
        var parts = (sortBy ?? "").Split('_', 2);
        var desc = parts.Length < 2 || !parts[1].Equals("asc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<Defect> ordered = parts[0].ToLowerInvariant() switch
        {
            "code" => desc ? query.OrderByDescending(x => x.DefectCode) : query.OrderBy(x => x.DefectCode),
            "title" => desc ? query.OrderByDescending(x => x.Title) : query.OrderBy(x => x.Title),
            // ตามระดับความรุนแรง Critical → Low (asc = รุนแรงสุดก่อน) ไม่ใช่ตัวอักษร
            "severity" => desc
                ? query.OrderByDescending(x => x.Severity == "Critical" ? 0 : x.Severity == "High" ? 1 : x.Severity == "Medium" ? 2 : x.Severity == "Low" ? 3 : 4)
                : query.OrderBy(x => x.Severity == "Critical" ? 0 : x.Severity == "High" ? 1 : x.Severity == "Medium" ? 2 : x.Severity == "Low" ? 3 : 4),
            // ตามลำดับงาน Open → In Progress → Resolved → Closed → Rejected
            "status" => desc
                ? query.OrderByDescending(x => x.Status == "Open" ? 0 : x.Status == "In Progress" ? 1 : x.Status == "Resolved" ? 2 : x.Status == "Closed" ? 3 : 4)
                : query.OrderBy(x => x.Status == "Open" ? 0 : x.Status == "In Progress" ? 1 : x.Status == "Resolved" ? 2 : x.Status == "Closed" ? 3 : 4),
            "createdat" => desc ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt),
            _ => query.OrderByDescending(x => x.CreatedAt),
        };
        return ordered.ThenByDescending(x => x.CreatedAt);
    }
}
