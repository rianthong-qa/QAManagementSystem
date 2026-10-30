using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.TestManagement;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.TestManagement;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>ปุ่มลบ Suite (RemoveAsync): ไม่เคยมี Test Cycle ลบถาวร, เคยมี (รวม Cycle ที่ลบแล้ว) soft delete เก็บผล Execution ไว้ให้รายงาน</summary>
public sealed class TestSuiteDeleteTests
{
    private static async Task<(QaDbContext Db, TestSuiteService Service, AutomationTestFixtures.Baseline Baseline)> SetupAsync()
    {
        var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db, automationCandidate: false);
        var projectId = baseline.Project.ProjectId;
        var service = new TestSuiteService(new TestSuiteRepository(db, new ProjectAccessContext { AllowedProjectIds = [projectId] }), AutomationTestFixtures.ProjectRepository(db, projectId));
        return (db, service, baseline);
    }

    private static TestCycle Cycle(AutomationTestFixtures.Baseline b, Guid suiteId) =>
        new(b.Project.ProjectId, b.Release.ReleaseId, b.Build.BuildId, b.Environment.TestEnvironmentId, suiteId, $"CYC-{Guid.NewGuid():N}"[..12], "Cycle", "Regression", null, null, null, null, null);

    [Fact]
    public async Task Remove_HardDeletesSuiteWithoutCycles()
    {
        var (db, service, b) = await SetupAsync();
        var suite = new TestSuite(b.Project.ProjectId, "TS-1", "Empty", null, null, null);
        db.Add(suite); await db.SaveChangesAsync();

        Assert.True(await service.RemoveAsync(suite.TestSuiteId, null, default));

        Assert.False(await db.TestSuites.AnyAsync(x => x.TestSuiteId == suite.TestSuiteId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Remove_SoftDeletesSuiteWithCyclesAndKeepsThem(bool cycleSoftDeleted)
    {
        var (db, service, b) = await SetupAsync();
        var suite = new TestSuite(b.Project.ProjectId, "TS-2", "Has cycle", null, null, null);
        var cycle = Cycle(b, suite.TestSuiteId);
        if (cycleSoftDeleted) cycle.SoftDelete(null);
        db.AddRange(suite, cycle); await db.SaveChangesAsync();
        var userId = Guid.NewGuid();

        Assert.False(await service.RemoveAsync(suite.TestSuiteId, userId, default));

        var saved = await db.TestSuites.AsNoTracking().SingleAsync(x => x.TestSuiteId == suite.TestSuiteId);
        Assert.True(saved.IsDeleted);
        Assert.False(saved.IsActive);
        Assert.Equal(userId, saved.DeletedBy);
        Assert.True(await db.TestCycles.AnyAsync(x => x.TestCycleId == cycle.TestCycleId));
    }

    [Fact]
    public async Task SoftDeletedSuite_HiddenFromListUnlessRequestedAndNotEditable()
    {
        var (db, service, b) = await SetupAsync();
        var kept = new TestSuite(b.Project.ProjectId, "TS-3", "Kept", null, null, null);
        var removed = new TestSuite(b.Project.ProjectId, "TS-4", "Removed", null, null, null);
        db.AddRange(kept, removed, Cycle(b, removed.TestSuiteId)); await db.SaveChangesAsync();
        await service.RemoveAsync(removed.TestSuiteId, null, default);

        var visible = await service.ListAsync(b.Project.ProjectId, null, null, null, null, null, 1, 100, default);
        var withDeleted = await service.ListAsync(b.Project.ProjectId, null, null, null, null, null, 1, 100, default, includeDeleted: true);

        Assert.DoesNotContain(visible.Rows, x => x.TestSuiteId == removed.TestSuiteId);
        Assert.Contains(withDeleted.Rows, x => x.TestSuiteId == removed.TestSuiteId && x.IsDeleted);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.UpdateAsync(removed.TestSuiteId, new SaveTestSuiteRequest(b.Project.ProjectId, "", "Renamed", null, null, null, true), default));
    }

    [Fact]
    public async Task Restore_ReturnsSuiteAsInactive()
    {
        var (db, service, b) = await SetupAsync();
        var suite = new TestSuite(b.Project.ProjectId, "TS-5", "Restore me", null, null, null);
        db.AddRange(suite, Cycle(b, suite.TestSuiteId)); await db.SaveChangesAsync();
        await service.RemoveAsync(suite.TestSuiteId, null, default);

        await service.RestoreAsync(suite.TestSuiteId, default);

        var saved = await db.TestSuites.AsNoTracking().SingleAsync(x => x.TestSuiteId == suite.TestSuiteId);
        Assert.False(saved.IsDeleted);
        Assert.False(saved.IsActive);
        Assert.Null(saved.DeletedAt);
    }

    [Fact]
    public async Task Restore_RefusesWhenNameTakenByVisibleSuite()
    {
        var (db, service, b) = await SetupAsync();
        var suite = new TestSuite(b.Project.ProjectId, "TS-6", "Same name", null, null, null);
        db.AddRange(suite, Cycle(b, suite.TestSuiteId)); await db.SaveChangesAsync();
        await service.RemoveAsync(suite.TestSuiteId, null, default);
        // ชื่อของ Suite ที่ลบแล้วไม่กันการสร้างใหม่ — แต่กู้คืนต้องไม่ทำให้ชื่อซ้ำ
        await service.CreateAsync(new SaveTestSuiteRequest(b.Project.ProjectId, "TS-7", "Same name", null, null, null), null, default);

        await Assert.ThrowsAsync<DuplicateCodeException>(() => service.RestoreAsync(suite.TestSuiteId, default));
    }

    [Fact]
    public async Task HardDelete_StillDeletesSuiteAndLinkedCycles()
    {
        var (db, service, b) = await SetupAsync();
        var suite = new TestSuite(b.Project.ProjectId, "TS-8", "Row delete", null, null, null);
        var cycle = Cycle(b, suite.TestSuiteId);
        db.AddRange(suite, cycle); await db.SaveChangesAsync();

        await service.HardDeleteAsync(suite.TestSuiteId, default);

        Assert.False(await db.TestSuites.AnyAsync(x => x.TestSuiteId == suite.TestSuiteId));
        Assert.False(await db.TestCycles.AnyAsync(x => x.TestCycleId == cycle.TestCycleId));
    }
}
