using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProMaxx2.QA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdHocTestCycleType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Cycle "พบนอกแผน" ที่ระบบสร้างจาก Defect ที่แจ้งเองใช้ประเภท "Ad hoc" (DefectsController.UnplannedCycleType) —
            // เพิ่มใน Master Setting ให้ตัวกรองประเภทของหน้า Test Cycle เลือกได้ และฟอร์มแก้ไขไม่แสดงเป็น "(ปิดใช้งาน)"; idempotent
            migrationBuilder.Sql("""
                INSERT INTO MasterOptions (MasterOptionId, Category, Value, DisplayName, SortOrder, IsActive)
                SELECT NEWID(), 'TestCycleType', 'Ad hoc', 'Ad hoc', 50, 1
                WHERE NOT EXISTS (SELECT 1 FROM MasterOptions WHERE Category = 'TestCycleType' AND Value = 'Ad hoc')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ไม่ลบ — อาจมี Test Cycle ใช้ประเภทนี้แล้ว
        }
    }
}
