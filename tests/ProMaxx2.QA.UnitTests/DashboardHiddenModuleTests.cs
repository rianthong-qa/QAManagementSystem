using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Domain.Defects;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Projects;
using ProMaxx2.QA.Domain.Requirements;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Dashboard ต้องไม่นำข้อมูลของ Module ที่ซ่อน (IsActive=false) มาคำนวณ</summary>
public sealed class DashboardHiddenModuleTests
{
    [Fact]
    public async Task Summary_excludes_data_of_hidden_modules()
    {
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var projectId = Guid.NewGuid();
        var visible = new ProductModule(projectId, "MOD-001", "Visible", null, null, null, null);
        var hidden = new ProductModule(projectId, "MOD-002", "Hidden", null, null, null, null);
        hidden.Deactivate(null);
        db.Modules.AddRange(visible, hidden);
        db.Requirements.AddRange(Requirement(projectId, visible.ModuleId, "REQ-1"), Requirement(projectId, hidden.ModuleId, "REQ-2"));
        db.TestCases.AddRange(Case(projectId, visible.ModuleId, "TC-1"), Case(projectId, hidden.ModuleId, "TC-2"));
        db.Defects.AddRange(Defect(projectId, visible.ModuleId, "DF-1"), Defect(projectId, hidden.ModuleId, "DF-2"), Defect(projectId, null, "DF-3"));
        await db.SaveChangesAsync();

        var summary = await new DashboardRepository(db, new ProjectAccessContext()).GetAsync(projectId, null, null, CancellationToken.None);

        Assert.Equal(1, summary.TotalRequirements);
        Assert.Equal(1, summary.TotalTestCaseCount);
        Assert.Equal(2, summary.TotalDefects);
        Assert.Equal([visible.ModuleId], summary.Modules.Select(x => x.ModuleId));
    }

    [Fact]
    public async Task Module_rows_count_cycle_cases_in_scope()
    {
        // หน้า Dashboard ใช้ CycleCases แยกโมดูลที่มี Cycle ในขอบเขต Release/Build ออกจากโมดูลที่ไม่ได้ทดสอบ
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        var idle = new ProductModule(seed.Project.ProjectId, "MOD-IDLE", "Idle", null, null, null, null);
        db.Modules.Add(idle);
        var cycle = new TestCycle(seed.Project.ProjectId, seed.Release.ReleaseId, seed.Build.BuildId, seed.Environment.TestEnvironmentId, null, "CYC-001", "Cycle", null, null, null, null, null, null);
        db.TestCycles.Add(cycle);
        db.TestCycleCases.Add(new TestCycleCase(cycle.TestCycleId, seed.TestCase.TestCaseId, seed.TestCase.RevisionNo, "P1", 1));
        await db.SaveChangesAsync();

        var summary = await new DashboardRepository(db, new ProjectAccessContext()).GetAsync(seed.Project.ProjectId, seed.Release.ReleaseId, null, CancellationToken.None);

        Assert.Equal(1, summary.Modules.Single(x => x.ModuleId == seed.Module.ModuleId).CycleCases);
        Assert.Equal(0, summary.Modules.Single(x => x.ModuleId == idle.ModuleId).CycleCases);
    }

    [Fact]
    public async Task Module_test_case_count_follows_release_scope()
    {
        // เลือก Release = นับเฉพาะ Test Case ที่อยู่ใน Cycle (ไม่ซ้ำแม้อยู่หลาย Cycle) ให้ตรงกับตัวเลขรวมของหน้า; ไม่เลือก = ทั้ง Project
        await using var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var seed = await AutomationTestFixtures.SeedBaselineAsync(db);
        db.TestCases.Add(new TestCase(seed.Project.ProjectId, seed.Module.ModuleId, "TC-SALE-900", "Not planned", null, null, "P1", null, false, null, [new TestStepInput(1, "Action", null, "Expected")], null));
        foreach (var code in new[] { "CYC-001", "CYC-002" })
        {
            var cycle = new TestCycle(seed.Project.ProjectId, seed.Release.ReleaseId, seed.Build.BuildId, seed.Environment.TestEnvironmentId, null, code, code, null, null, null, null, null, null);
            db.TestCycles.Add(cycle);
            db.TestCycleCases.Add(new TestCycleCase(cycle.TestCycleId, seed.TestCase.TestCaseId, seed.TestCase.RevisionNo, "P1", 1));
        }
        await db.SaveChangesAsync();
        var repository = new DashboardRepository(db, new ProjectAccessContext());

        var byRelease = await repository.GetAsync(seed.Project.ProjectId, seed.Release.ReleaseId, null, CancellationToken.None);
        var byProject = await repository.GetAsync(seed.Project.ProjectId, null, null, CancellationToken.None);

        Assert.Equal(1, byRelease.Modules.Single(x => x.ModuleId == seed.Module.ModuleId).TestCases);
        Assert.Equal(byRelease.TotalTestCaseCount, byRelease.Modules.Sum(x => x.TestCases));
        Assert.Equal(2, byProject.Modules.Single(x => x.ModuleId == seed.Module.ModuleId).TestCases);
    }

    [Fact]
    public void Hidden_module_filter_translates_to_sql()
    {
        // InMemory ไม่ตรวจการแปลงเป็น SQL — เงื่อนไขแบบเดียวกับ DashboardRepository ต้องแปลงได้ทั้ง ModuleId ตรงและผ่าน navigation
        using var db = new QaDbContext(new DbContextOptionsBuilder<QaDbContext>()
            .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True").Options);
        var hiddenModules = db.Modules.Where(m => !m.IsActive);
        Assert.Contains("[IsActive]", db.Defects.Where(x => !hiddenModules.Any(m => m.ModuleId == x.ModuleId)).ToQueryString());
        Assert.Contains("[IsActive]", db.TestExecutions.Where(x => !hiddenModules.Any(m => m.ModuleId == x.CycleCase.TestCase.ModuleId)).ToQueryString());
    }

    private static Requirement Requirement(Guid projectId, Guid moduleId, string code) => new(projectId, null, moduleId, code, code, null, null, "P1", null, null, null, true, null);
    private static TestCase Case(Guid projectId, Guid moduleId, string code) => new(projectId, moduleId, code, code, null, null, "P1", null, false, null, [new TestStepInput(1, "Action", null, "Expected")], null);
    private static Defect Defect(Guid projectId, Guid? moduleId, string code) => new(projectId, null, null, moduleId, code, code, "Low", "Open", null, null, null, null, null, null);
}
