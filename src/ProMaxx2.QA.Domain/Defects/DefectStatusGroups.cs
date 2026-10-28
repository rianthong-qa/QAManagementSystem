namespace ProMaxx2.QA.Domain.Defects;

/// <summary>
/// นิยามกลุ่มสถานะ Defect เดียวทั้งระบบ (DEFECT_BUILD_LIFECYCLE.md เฟส 4) — Dashboard, Test Summary, Regression, สถิติหน้า Defect และ Release Gate ใช้ชุดเดียวกัน
/// "ยังไม่จบ" (NotDone) = ยังไม่ได้แก้ + รอตรวจซ้ำ เพราะ Resolved ยังไม่มีใครยืนยันว่าแก้ได้จริง
/// </summary>
public static class DefectStatusGroups
{
    /// <summary>Dev ยังไม่ได้แก้</summary>
    public static readonly string[] Unresolved=["Open","In Progress"];
    /// <summary>Dev แก้แล้ว รอ QA ตรวจซ้ำบน Fix Build</summary>
    public static readonly string[] AwaitingVerification=["Resolved"];
    /// <summary>จบแล้ว — ตรวจซ้ำผ่าน/ปิดด้วยผลอื่น หรือไม่ใช่ Defect</summary>
    public static readonly string[] Done=["Closed","Rejected"];
    /// <summary>Unresolved + AwaitingVerification</summary>
    public static readonly string[] NotDone=["Open","In Progress","Resolved"];
    /// <summary>ผลการปิดที่ยังเป็นปัญหาที่ผู้ใช้เจอได้ — แสดงเป็น Known Issue ใน Test Summary</summary>
    public static readonly string[] KnownIssueResolutions=["Deferred","WontFix"];

    public static bool IsDone(string status)=>Done.Contains(status);
}
