using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>GET /test-cases/picker — ตัวเลือก Test Case ของฟอร์ม Test Suite ต้องได้ทั้ง Project ใน query เดียว และเคารพสิทธิ์ Project</summary>
public sealed class TestCasePickerTests
{
    private static TestCase Case(Guid projectId, Guid moduleId, string code, string priority = "P1") =>
        new(projectId, moduleId, code, $"Title {code}", null, null, priority, "Functional", false, null, [new TestStepInput(1, "Do", null, "Done")], null);

    [Fact]
    public async Task Picker_returns_every_case_of_the_project_sorted_by_code_without_other_projects()
    {
        using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var other = new Project("OTH", "Other", null, null, null);
        db.AddRange(other, Case(baseline.Project.ProjectId, baseline.Module.ModuleId, "TC-SALE-000", "P2"), Case(other.ProjectId, baseline.Module.ModuleId, "TC-OTHER-001"));
        await db.SaveChangesAsync();

        var rows = await new TestCaseRepository(db, new ProjectAccessContext { AllowedProjectIds = [baseline.Project.ProjectId, other.ProjectId] })
            .PickerAsync(baseline.Project.ProjectId, CancellationToken.None);

        Assert.Equal(["TC-SALE-000", "TC-SALE-001"], rows.Select(x => x.TestCaseCode));
        Assert.All(rows, x => Assert.Equal(baseline.Project.ProjectId, x.ProjectId));
        Assert.Equal("P2", rows[0].Priority);
        Assert.Equal(baseline.Module.ModuleId, rows[1].ModuleId);
    }

    [Fact]
    public async Task Picker_returns_nothing_for_a_project_the_caller_cannot_access()
    {
        using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);

        var rows = await new TestCaseRepository(db, new ProjectAccessContext { AllowedProjectIds = [Guid.NewGuid()] })
            .PickerAsync(baseline.Project.ProjectId, CancellationToken.None);

        Assert.Empty(rows);
    }
}
