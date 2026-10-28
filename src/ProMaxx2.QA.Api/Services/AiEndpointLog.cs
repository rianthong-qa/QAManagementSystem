namespace ProMaxx2.QA.Api.Services;

/// <summary>Logger category สำหรับความล้มเหลวของ AI generate endpoints (Requirement/Test Case/Test Suite)
/// — ใช้แยก log ของ AI ออกจาก controller อื่นโดยไม่ต้องเพิ่ม ILogger เข้า constructor ของทุก controller</summary>
public sealed class AiEndpointLog;
