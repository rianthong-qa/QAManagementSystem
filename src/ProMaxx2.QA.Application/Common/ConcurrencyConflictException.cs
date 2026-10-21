namespace ProMaxx2.QA.Application.Common;

/// <summary>AUT-REL-001: ข้อมูลถูกคำขออื่นแก้ไปก่อนระหว่างที่คำขอนี้กำลังบันทึก (rowversion ไม่ตรง) — Application layer
/// ไม่อ้างอิง EF จึงให้ repository แปลง <c>DbUpdateConcurrencyException</c> เป็น exception นี้; API ตอบ 409</summary>
public sealed class ConcurrencyConflictException(string message, Exception? inner = null) : Exception(message, inner);
