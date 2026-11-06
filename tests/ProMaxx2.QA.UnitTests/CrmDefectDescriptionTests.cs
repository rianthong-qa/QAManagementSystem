using ProMaxx2.QA.Api.Services;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Description ของ CRM Ticket ที่ส่งจาก Defect — เรียงตามสิ่งที่ Dev ต้องรู้ก่อน และไม่เกิน 1000 ตัวอักษรโดยลิงก์อยู่ครบ</summary>
public sealed class CrmDefectDescriptionTests
{
    private const string Url = "https://promaxx2.qahub.store/?d=qvu24npd";
    private const string Raw = "ลบจังหวัด (ระบุชื่อ) ที่ถูกใช้ในที่อยู่ลูกค้า แล้วบันทึก ระบบแจ้ง \"บันทึกไม่สำเร็จ: violation of FOREIGN KEY constraint\" เป็น error ดิบภาษาอังกฤษ";

    // รูปแบบเดียวกับที่ DefectAutoCreateService เขียน (PMX2-DEF-2254)
    private static readonly string ExecutionDescription = string.Join("\n",
        "Test Cycle: PMX2-CYC-768 (ข้อมูลพื้นฐานบุคคล-PMX2-TS-557-Functional PMX2-REL-008 beta.1-ภายใน) — Run #1",
        "ประเภท Cycle: Functional",
        "Environment: Windows 11",
        "Build: beta.1-ภายใน",
        "Priority: P1 | ประเภท Test Case: Functional",
        "วัตถุประสงค์: ยืนยันการห้ามลบ",
        "เงื่อนไขก่อนเริ่ม: ผู้ใช้เข้าสู่ระบบด้วยสิทธิ์ Administrator เปิดเมนูข้อมูลพื้นฐานบุคคล",
        "ผู้ทดสอบ: ณิชกานต์ ช่วยเมือง",
        "ขั้นตอนที่ Fail (1 ขั้น):",
        $"- ขั้นที่ 1: ลบจังหวัดที่ถูกใช้ในที่อยู่ลูกค้า — ผลจริง: {Raw}",
        "เวลาที่ทดสอบ: 06/11/2569 10:26 น.");

    private static CrmDescriptionInput Input(string? description, string? steps, string? expected = "ระบบห้ามลบและแจ้งว่าถูกใช้งานอยู่", string? actual = null, int images = 0, int skipped = 0) =>
        new("PMX2-DEF-2254", "High", "ข้อมูลพื้นฐานบุคคล", "beta.1-ภายใน", description, steps, expected, actual, "PMX2-MOD-030-TC-012", images, skipped, Url);

    [Fact]
    public void Execution_defect_puts_problem_first_and_drops_cycle_noise()
    {
        var steps = $"1. ลบจังหวัดที่ถูกใช้ในที่อยู่ลูกค้า (Fail) | ข้อมูล: - | คาดหวัง: ระบบห้ามลบ | ผลจริง: {Raw} | [ขั้นนี้ล้มเหลว]";
        var text = CrmDefectDescription.Build(Input(ExecutionDescription, steps, images: 2));

        Assert.StartsWith("[QA Hub] PMX2-DEF-2254 · ความรุนแรง High\nโมดูล ข้อมูลพื้นฐานบุคคล · Build beta.1-ภายใน · Windows 11\n\n[ปัญหาที่พบ]\nขั้นที่ 1 ลบจังหวัดที่ถูกใช้ในที่อยู่ลูกค้า: ลบจังหวัด", text);
        Assert.True(text.IndexOf("[ปัญหาที่พบ]") < text.IndexOf("[ผลที่ควรเป็น]") && text.IndexOf("[ผลที่ควรเป็น]") < text.IndexOf("[ขั้นตอนทำซ้ำ]"));
        Assert.Contains("1. ลบจังหวัดที่ถูกใช้ในที่อยู่ลูกค้า  ← Fail", text);
        Assert.Contains("ก่อนเริ่ม: ผู้ใช้เข้าสู่ระบบ", text);
        Assert.Contains("PMX2-MOD-030-TC-012 · PMX2-CYC-768 Run #1", text);
        Assert.Contains("ทดสอบโดย ณิชกานต์ ช่วยเมือง 06/11/2569 10:26 น.", text);
        Assert.Contains("แนบรูป 2 รูป", text);
        Assert.EndsWith($"รายละเอียดเต็ม: {Url}", text);
        foreach (var noise in new[] { "ประเภท Cycle", "วัตถุประสงค์", "Priority", "ข้อมูลทดสอบ: -", "คาดหวัง:" }) Assert.DoesNotContain(noise, text);
        Assert.Equal(1, CountOf(text, "ลบจังหวัด (ระบุชื่อ)")); // ไม่ซ้ำระหว่างปัญหาที่พบกับขั้นตอน
    }

    [Fact]
    public void Long_steps_are_reduced_to_failed_step_and_the_one_before_while_link_survives()
    {
        var steps = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"{i}. ทำขั้นตอนที่ยาวพอสมควรเพื่อทดสอบการย่อข้อความ ลำดับ {i} ({(i == 20 ? "Fail" : "Pass")}) | คาดหวัง: ผ่าน"));
        var text = CrmDefectDescription.Build(Input(ExecutionDescription, steps));

        Assert.True(text.Length <= CrmDefectDescription.MaxLength);
        Assert.Contains("(อีก 18 ขั้น ดูในลิงก์)\n19. ", text);
        Assert.Contains("20. ทำขั้นตอนที่ยาวพอสมควรเพื่อทดสอบการย่อข้อความ ลำดับ 20  ← Fail\n(อีก 10 ขั้น ดูในลิงก์)", text);
        Assert.Contains("[ผลที่ควรเป็น]\nระบบห้ามลบและแจ้งว่าถูกใช้งานอยู่", text);
        Assert.EndsWith($"บางส่วนถูกย่อ — ดูฉบับเต็ม: {Url}", text);
    }

    [Fact]
    public void Manual_defect_uses_its_own_text_and_cuts_problem_last()
    {
        var shortText = CrmDefectDescription.Build(Input("หน้าจอค้างเมื่อกดบันทึก", "1. เปิดหน้า\n2. กดบันทึก", "บันทึกสำเร็จ", "หน้าจอค้าง 30 วินาที"));
        Assert.Contains("[ปัญหาที่พบ]\nหน้าจอค้างเมื่อกดบันทึก\nผลจริง: หน้าจอค้าง 30 วินาที", shortText);
        Assert.Contains("[ขั้นตอนทำซ้ำ]\n1. เปิดหน้า\n2. กดบันทึก", shortText);

        var longText = CrmDefectDescription.Build(Input(new string('ก', 3000), "1. เปิดหน้า", new string('ข', 600)));
        Assert.True(longText.Length <= CrmDefectDescription.MaxLength);
        Assert.Contains("ก…", longText);
        Assert.Contains("[ผลที่ควรเป็น]\nขขข", longText);
        Assert.DoesNotContain("[ขั้นตอนทำซ้ำ]", longText);
        Assert.EndsWith(Url, longText);
    }

    [Fact]
    public void Steps_copied_from_test_case_keep_only_action_and_collect_test_data()
    {
        // PMX2-DEF-2225: ขั้นตอนจาก Test Case "1. ชื่อขั้น | ข้อมูล: … | คาดหวัง: …" และมีแต่ผลจริง
        var text = CrmDefectDescription.Build(Input(null, "1. เปิดบิลขาย | ข้อมูล: A001 จำนวน 1 ชิ้น | คาดหวัง: บันทึกสำเร็จ\n2. ตั้งค่าไม่อนุญาตสต๊อกติดลบ | คาดหวัง: แจ้งเตือน", "แจ้งเตือนไม่สามารถตั้งค่าได้", "ระบบสามารถตั้งค่าได้"));
        Assert.Contains("[ปัญหาที่พบ]\nระบบสามารถตั้งค่าได้\n", text);
        Assert.Contains("[ขั้นตอนทำซ้ำ]\n1. เปิดบิลขาย\n2. ตั้งค่าไม่อนุญาตสต๊อกติดลบ\nข้อมูลทดสอบ: A001 จำนวน 1 ชิ้น", text);
        Assert.DoesNotContain("คาดหวัง:", text);
    }

    [Fact]
    public void Skipped_images_are_pointed_to_the_link()
    {
        var text = CrmDefectDescription.Build(Input("ปัญหา", null, null, null, images: 1, skipped: 2));
        Assert.Contains("แนบรูป 1 รูป · รูปอีก 2 รูปดูในลิงก์", text);
        Assert.DoesNotContain("[ผลที่ควรเป็น]", text);
    }

    private static int CountOf(string text, string value) => (text.Length - text.Replace(value, "").Length) / value.Length;
}
