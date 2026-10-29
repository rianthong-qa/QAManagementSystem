using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.TestPlanning;
using ProMaxx2.QA.Domain.Execution;
using ProMaxx2.QA.Domain.Releases;
using ProMaxx2.QA.Domain.TestPlanning;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Test Plan / Configuration Matrix เฟส 1 — Domain, Repository, ตัวนับ และสถานะที่คำนวณ (TEST_PLAN_CONFIGURATION_MATRIX_PLAN.md)</summary>
public sealed class TestPlanTests
{
    private static TestPlan NewPlan(Guid? projectId = null, Guid? releaseId = null) =>
        new(projectId ?? Guid.NewGuid(), releaseId ?? Guid.NewGuid(), "aut-tp-001", "Regression Plan", "regression", null, null, null, null, null);

    // ---------- Domain ----------

    [Fact]
    public void New_plan_normalizes_code_and_type_and_starts_active_unfrozen()
    {
        var plan = NewPlan();
        Assert.Equal("AUT-TP-001", plan.PlanCode);
        Assert.Equal("Regression", plan.PlanType);
        Assert.Equal(TestPlanLifecycleStatuses.Active, plan.LifecycleStatus);
        Assert.False(plan.IsFrozen);
        Assert.Equal("Other", new TestPlan(Guid.NewGuid(), Guid.NewGuid(), "P", "Plan", null, null, null, null, null, null).PlanType);
    }

    [Fact]
    public void New_plan_rejects_end_before_start_and_unknown_type()
    {
        Assert.Throws<ArgumentException>(() => new TestPlan(Guid.NewGuid(), Guid.NewGuid(), "P", "Plan", null, new DateTime(2026, 10, 31), new DateTime(2026, 10, 1), null, null, null));
        Assert.Throws<ArgumentException>(() => new TestPlan(Guid.NewGuid(), Guid.NewGuid(), "P", "Plan", "Exploratory", null, null, null, null, null));
    }

    [Fact]
    public void Close_and_cancel_freeze_automatically_and_block_unfreeze_until_reopen()
    {
        var closed = NewPlan();
        closed.Close(null);
        Assert.Equal(TestPlanLifecycleStatuses.Closed, closed.LifecycleStatus);
        Assert.True(closed.IsFrozen);
        Assert.Throws<TestPlanRuleException>(() => closed.Unfreeze(null));
        Assert.Throws<TestPlanRuleException>(() => closed.Update("x", null, null, null, null, null, null));

        closed.Reopen(null);
        Assert.Equal(TestPlanLifecycleStatuses.Active, closed.LifecycleStatus);
        Assert.False(closed.IsFrozen);
        Assert.Null(closed.FrozenAt);

        var cancelled = NewPlan();
        cancelled.Cancel(null);
        Assert.True(cancelled.IsFrozen);
        Assert.Throws<TestPlanRuleException>(() => cancelled.Freeze(null));
        cancelled.Reopen(null);
        Assert.True(cancelled.IsActive);
    }

    [Fact]
    public void Freeze_and_unfreeze_work_only_on_active_plan_and_block_edits()
    {
        var plan = NewPlan();
        var user = Guid.NewGuid();
        plan.Freeze(user);
        Assert.True(plan.IsFrozen);
        Assert.Equal(user, plan.FrozenByUserId);
        Assert.Throws<TestPlanRuleException>(() => plan.Freeze(user));
        Assert.Throws<TestPlanRuleException>(() => plan.EnsureEditable());
        plan.Unfreeze(user);
        Assert.False(plan.IsFrozen);
        Assert.Throws<TestPlanRuleException>(() => plan.Unfreeze(user));
        Assert.Throws<TestPlanRuleException>(() => plan.Reopen(user));
    }

    [Fact]
    public void Soft_delete_requires_closed_or_cancelled_when_plan_has_runs()
    {
        var plan = NewPlan();
        Assert.Throws<TestPlanRuleException>(() => plan.SoftDelete(hasRuns: true, null));
        plan.Close(null);
        plan.SoftDelete(hasRuns: true, null);
        Assert.True(plan.IsDeleted);

        var empty = NewPlan();
        empty.SoftDelete(hasRuns: false, null);
        Assert.True(empty.IsDeleted);
    }

    [Theory]
    [InlineData(" Windows-Chrome ", "windows-chrome")]
    [InlineData("android14-pixel", "android14-pixel")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void Configuration_key_is_normalized(string? input, string? expected) => Assert.Equal(expected, TestPlanRun.NormalizeConfigurationKey(input));

    [Theory]
    [InlineData("windows chrome")]
    [InlineData("windows_chrome")]
    [InlineData("-windows")]
    public void Configuration_key_rejects_invalid_characters(string input) => Assert.Throws<ArgumentException>(() => TestPlanRun.NormalizeConfigurationKey(input));

    [Fact]
    public void Configuration_json_must_be_an_object()
    {
        Assert.Equal("{\"os\":\"Windows 11\"}", TestPlanRun.NormalizeConfigurationJson("{ \"os\" : \"Windows 11\" }"));
        Assert.Throws<ArgumentException>(() => TestPlanRun.NormalizeConfigurationJson("[1,2]"));
        Assert.Throws<ArgumentException>(() => TestPlanRun.NormalizeConfigurationJson("{os:"));
        Assert.Null(TestPlanRun.NormalizeConfigurationJson(null));
    }

    [Fact]
    public void Run_assignment_role_is_validated()
    {
        Assert.Equal("Reviewer", new TestPlanRunAssignment(Guid.NewGuid(), Guid.NewGuid(), "reviewer", Guid.NewGuid()).Role);
        Assert.Throws<ArgumentException>(() => new TestPlanRunAssignment(Guid.NewGuid(), Guid.NewGuid(), "Owner", Guid.NewGuid()));
    }

    // ---------- สูตรตัวนับ (§3.4) ----------

    [Fact]
    public void Summarize_matches_documented_example()
    {
        var summary = TestPlanRepository.Summarize(3, new Dictionary<string, int> { ["NotRun"] = 9, ["InProgress"] = 3, ["Pass"] = 12, ["Fail"] = 11 }, [2, 3, 2, 3]);
        Assert.Equal(35, summary.TotalCases);
        Assert.Equal(26, summary.Touched);
        Assert.Equal(23, summary.Completed);
        Assert.Equal(74.3m, summary.ProgressPercent);
        Assert.Equal(65.7m, summary.CompletionPercent);
        Assert.Equal(52.2m, summary.PassRate);
        Assert.Equal(4, summary.Retest);
        Assert.Equal(6, summary.RetestAttempts);
    }

    [Fact]
    public void Summarize_returns_null_pass_rate_when_nothing_completed()
    {
        var summary = TestPlanRepository.Summarize(1, new Dictionary<string, int> { ["NotRun"] = 4, ["InProgress"] = 1 }, []);
        Assert.Null(summary.PassRate);
        Assert.Equal(0m, summary.CompletionPercent);
        Assert.Equal(20m, summary.ProgressPercent);
    }

    // ---------- Service + Repository (EF InMemory) ----------

    private sealed record Ctx(QaDbContext Db, AutomationTestFixtures.Baseline Baseline, TestPlanService Service, TestPlanRepository Repository, TestCycleService Cycles, ProjectAccessContext Access);

    private static async Task<Ctx> CreateAsync()
    {
        var db = AutomationTestFixtures.CreateInMemoryDatabase();
        var baseline = await AutomationTestFixtures.SeedBaselineAsync(db);
        var access = new ProjectAccessContext { AllowedProjectIds = [baseline.Project.ProjectId] };
        var repository = new TestPlanRepository(db, access);
        var projects = new ProjectRepository(db, access);
        var cycles = new TestCycleService(new TestCycleRepository(db, access), projects);
        return new Ctx(db, baseline, new TestPlanService(repository, projects, cycles), repository, cycles, access);
    }

    private static SaveTestPlanRequest PlanRequest(Ctx c, string? code = null) => new(c.Baseline.Project.ProjectId, c.Baseline.Release.ReleaseId, code, "Release 1.0 Regression", "Regression", null, null, null, null);

    /// <summary>สร้าง Cycle พร้อมเคสตามสถานะที่กำหนด (TestCaseId สุ่ม — InMemory ไม่ตรวจ FK)</summary>
    private static async Task<TestCycle> AddCycleAsync(Ctx c, string code, string cycleStatus = "Draft", Guid? releaseId = null, params string[] caseStatuses)
    {
        var cycle = new TestCycle(c.Baseline.Project.ProjectId, releaseId ?? c.Baseline.Release.ReleaseId, c.Baseline.Build.BuildId, c.Baseline.Environment.TestEnvironmentId, null, code, code, null, null, null, null, null, null);
        cycle.ChangeStatus(cycleStatus);
        var order = 1;
        foreach (var status in caseStatuses)
        {
            var cycleCase = new TestCycleCase(cycle.TestCycleId, Guid.NewGuid(), 1, "P2", order++);
            cycleCase.SetStatus(status);
            cycle.Cases.Add(cycleCase);
        }
        c.Db.Add(cycle);
        await c.Db.SaveChangesAsync();
        return cycle;
    }

    private static AttachTestPlanCycleRequest Attach(Guid cycleId, string? runCode = null, string? json = null) =>
        new(cycleId, runCode, null, "windows-chrome", "Windows 11 · Chrome", json is null ? null : JsonDocument.Parse(json).RootElement, null);

    [Fact]
    public async Task Create_generates_project_code_and_rejects_duplicate()
    {
        var c = await CreateAsync();
        var plan = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        Assert.Equal("AUT-TP-001", plan.PlanCode);
        Assert.Equal(TestPlanStatuses.Draft, plan.Status);
        Assert.Equal("REL-001", plan.ReleaseCode);
        Assert.Equal("AUT-TP-002", (await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None)).PlanCode);
        await Assert.ThrowsAsync<DuplicateCodeException>(() => c.Service.CreateAsync(PlanRequest(c, "aut-tp-001"), null, CancellationToken.None));
    }

    [Fact]
    public async Task Create_rejects_release_from_other_project_and_cancelled_release()
    {
        var c = await CreateAsync();
        var foreign = new Release(Guid.NewGuid(), "REL-X", "9.0", null, null, null, null, null);
        var cancelled = new Release(c.Baseline.Project.ProjectId, "REL-C", "0.9", null, null, null, null, null);
        cancelled.ChangeStatus("Cancelled", null);
        c.Db.AddRange(foreign, cancelled);
        await c.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<EntityNotFoundException>(() => c.Service.CreateAsync(PlanRequest(c) with { ReleaseId = foreign.ReleaseId }, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.CreateAsync(PlanRequest(c) with { ReleaseId = cancelled.ReleaseId }, null, CancellationToken.None));
    }

    [Fact]
    public async Task Attach_cycle_creates_run_without_copying_cases_or_executions()
    {
        var c = await CreateAsync();
        var cycle = await AddCycleAsync(c, "AUT-CYC-001", "InProgress", null, "Pass", "NotRun");
        var plan = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);

        var run = await c.Service.AttachCycleAsync(plan.TestPlanId, Attach(cycle.TestCycleId, json: "{\"browser\":\"Chrome\"}"), null, CancellationToken.None);

        Assert.Equal("AUT-CYC-001", run.RunCode);
        Assert.Equal(cycle.CycleName, run.RunName);
        Assert.Equal("windows-chrome", run.ConfigurationKey);
        Assert.Equal("{\"browser\":\"Chrome\"}", run.ConfigurationJson);
        Assert.Equal("1", run.BuildNumber);
        Assert.Equal("QA", run.EnvironmentName);
        Assert.Equal("InProgress", run.Status);
        Assert.Equal(2, run.TotalCases);
        Assert.Equal(2, await c.Db.TestCycleCases.CountAsync());
        Assert.Equal(1, await c.Db.TestCycles.CountAsync());
    }

    [Fact]
    public async Task Attach_rejects_cycle_from_other_release_deleted_or_already_linked()
    {
        var c = await CreateAsync();
        var otherRelease = new Release(c.Baseline.Project.ProjectId, "REL-002", "2.0", null, null, null, null, null);
        c.Db.Add(otherRelease);
        await c.Db.SaveChangesAsync();
        var wrongRelease = await AddCycleAsync(c, "AUT-CYC-009", "Draft", otherRelease.ReleaseId);
        var deleted = await AddCycleAsync(c, "AUT-CYC-008");
        deleted.SoftDelete(null);
        var linked = await AddCycleAsync(c, "AUT-CYC-001");
        await c.Db.SaveChangesAsync();
        var first = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        var second = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        await c.Service.AttachCycleAsync(first.TestPlanId, Attach(linked.TestCycleId), null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.AttachCycleAsync(second.TestPlanId, Attach(wrongRelease.TestCycleId), null, CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => c.Service.AttachCycleAsync(second.TestPlanId, Attach(deleted.TestCycleId), null, CancellationToken.None));
        await Assert.ThrowsAsync<TestPlanRuleException>(() => c.Service.AttachCycleAsync(second.TestPlanId, Attach(linked.TestCycleId), null, CancellationToken.None));
    }

    [Fact]
    public async Task Attach_rejects_duplicate_run_code_and_frozen_plan()
    {
        var c = await CreateAsync();
        var a = await AddCycleAsync(c, "AUT-CYC-001");
        var b = await AddCycleAsync(c, "AUT-CYC-002");
        var d = await AddCycleAsync(c, "AUT-CYC-003");
        var plan = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        await c.Service.AttachCycleAsync(plan.TestPlanId, Attach(a.TestCycleId, "REG-WIN"), null, CancellationToken.None);

        await Assert.ThrowsAsync<DuplicateCodeException>(() => c.Service.AttachCycleAsync(plan.TestPlanId, Attach(b.TestCycleId, "reg-win"), null, CancellationToken.None));

        var entity = await c.Repository.FindAsync(plan.TestPlanId, CancellationToken.None);
        entity!.Freeze(null);
        await c.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<TestPlanRuleException>(() => c.Service.AttachCycleAsync(plan.TestPlanId, Attach(d.TestCycleId), null, CancellationToken.None));
    }

    [Fact]
    public async Task Update_cannot_change_release_or_code()
    {
        var c = await CreateAsync();
        var plan = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        var updated = await c.Service.UpdateAsync(plan.TestPlanId, PlanRequest(c) with { PlanName = "Renamed", PlanType = "UAT" }, null, CancellationToken.None);
        Assert.Equal("Renamed", updated.PlanName);
        Assert.Equal("UAT", updated.PlanType);
        Assert.NotNull(updated.UpdatedAt);
        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.UpdateAsync(plan.TestPlanId, PlanRequest(c) with { ReleaseId = Guid.NewGuid() }, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.UpdateAsync(plan.TestPlanId, PlanRequest(c, "AUT-TP-999"), null, CancellationToken.None));
    }

    [Fact]
    public async Task Summary_counts_cases_and_retest_from_final_non_deleted_executions_only()
    {
        var c = await CreateAsync();
        var cycle1 = await AddCycleAsync(c, "AUT-CYC-001", "InProgress", null, "Pass", "Pass", "Fail", "InProgress");
        var cycle2 = await AddCycleAsync(c, "AUT-CYC-002", "Draft", null, "NotRun", "Blocked");
        var cases = cycle1.Cases.OrderBy(x => x.ExecutionOrder).ToList();
        var build = c.Baseline.Build.BuildId;
        var env = c.Baseline.Environment.TestEnvironmentId;
        var tester = Guid.NewGuid();
        TestExecution Exec(TestCycleCase cycleCase, int no, string status) => new(cycleCase.TestCycleCaseId, no, build, env, tester, status, null, null, []);
        // เคส 1: Save Progress แล้วจบ Pass — ทดสอบครั้งเดียว ไม่ใช่ Retest แม้ ExecutionNo = 2
        c.Db.AddRange(Exec(cases[0], 1, "InProgress"), Exec(cases[0], 2, "Pass"));
        // เคส 2: Fail แล้ว Retest เป็น Pass — Retest 1 เคส, 1 attempt
        c.Db.AddRange(Exec(cases[1], 1, "Fail"), Exec(cases[1], 2, "Pass"));
        // เคส 3: Fail 1 ครั้ง + Execution ที่ถูกลบ — ไม่นับแถวที่ลบ
        var removed = Exec(cases[2], 1, "Pass");
        removed.SoftDelete(null);
        c.Db.AddRange(removed, Exec(cases[2], 2, "Fail"));
        await c.Db.SaveChangesAsync();
        var plan = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        await c.Service.AttachCycleAsync(plan.TestPlanId, Attach(cycle1.TestCycleId), null, CancellationToken.None);
        await c.Service.AttachCycleAsync(plan.TestPlanId, Attach(cycle2.TestCycleId, "RUN-2"), null, CancellationToken.None);

        var detail = await c.Service.GetAsync(plan.TestPlanId, CancellationToken.None);

        var s = detail.Summary;
        Assert.Equal(2, s.RunCount);
        Assert.Equal(6, s.TotalCases);
        Assert.Equal(1, s.Untested);
        Assert.Equal(1, s.InProgress);
        Assert.Equal(2, s.Passed);
        Assert.Equal(1, s.Failed);
        Assert.Equal(1, s.Blocked);
        Assert.Equal(5, s.Touched);
        Assert.Equal(4, s.Completed);
        Assert.Equal(50m, s.PassRate);
        Assert.Equal(1, s.Retest);
        Assert.Equal(1, s.RetestAttempts);
        Assert.Equal(TestPlanStatuses.InProgress, detail.Status);
        Assert.Equal(2, detail.Runs.Count);
        Assert.Equal(4, detail.Runs[0].TotalCases);
    }

    [Fact]
    public async Task Plan_status_is_computed_from_runs_and_lifecycle()
    {
        var c = await CreateAsync();
        // หน้ารายละเอียดคำนวณสถานะใน SQL (WithStatus) ส่วนหน้ารายการคำนวณในหน่วยความจำ (PlanStatus) — ต้องตรงกันทุกขั้น
        async Task<string> StatusAsync(Guid id)
        {
            var detail = (await c.Service.GetAsync(id, CancellationToken.None)).Status;
            var listed = (await c.Service.ListAsync(c.Baseline.Project.ProjectId, null, null, null, null, null, 1, 100, CancellationToken.None)).Items.Rows.Single(x => x.TestPlanId == id).Status;
            Assert.Equal(detail, listed);
            return detail;
        }
        var plan = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        Assert.Equal(TestPlanStatuses.Draft, await StatusAsync(plan.TestPlanId));

        // Run ยัง Draft และไม่มีเคสถูกแตะ → Draft; มีเคสถูกแตะ → InProgress
        var draftCycle = await AddCycleAsync(c, "AUT-CYC-001", "Draft", null, "NotRun");
        await c.Service.AttachCycleAsync(plan.TestPlanId, Attach(draftCycle.TestCycleId), null, CancellationToken.None);
        Assert.Equal(TestPlanStatuses.Draft, await StatusAsync(plan.TestPlanId));
        draftCycle.Cases.Single().SetStatus("InProgress");
        await c.Db.SaveChangesAsync();
        Assert.Equal(TestPlanStatuses.InProgress, await StatusAsync(plan.TestPlanId));

        // ทุก Run จบ (Completed/Cancelled) → Completed แม้ยังมีเคสค้าง InProgress
        var cancelledCycle = await AddCycleAsync(c, "AUT-CYC-002", "Cancelled");
        await c.Service.AttachCycleAsync(plan.TestPlanId, Attach(cancelledCycle.TestCycleId), null, CancellationToken.None);
        draftCycle.ChangeStatus("Completed");
        await c.Db.SaveChangesAsync();
        Assert.Equal(TestPlanStatuses.Completed, await StatusAsync(plan.TestPlanId));

        var entity = await c.Repository.FindAsync(plan.TestPlanId, CancellationToken.None);
        entity!.Close(null);
        await c.Db.SaveChangesAsync();
        Assert.Equal(TestPlanStatuses.Closed, await StatusAsync(plan.TestPlanId));
        entity.Reopen(null);
        entity.Cancel(null);
        await c.Db.SaveChangesAsync();
        Assert.Equal(TestPlanStatuses.Cancelled, await StatusAsync(plan.TestPlanId));
    }

    [Fact]
    public async Task List_filters_by_computed_status_and_returns_summary_of_all_matches()
    {
        var c = await CreateAsync();
        var active = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        var cycle = await AddCycleAsync(c, "AUT-CYC-001", "InProgress", null, "Pass", "NotRun");
        await c.Service.AttachCycleAsync(active.TestPlanId, Attach(cycle.TestCycleId), null, CancellationToken.None);

        var all = await c.Service.ListAsync(c.Baseline.Project.ProjectId, null, null, null, null, null, 1, 20, CancellationToken.None);
        var inProgress = await c.Service.ListAsync(c.Baseline.Project.ProjectId, null, TestPlanStatuses.InProgress, null, null, null, 1, 20, CancellationToken.None);
        var drafts = await c.Service.ListAsync(c.Baseline.Project.ProjectId, null, TestPlanStatuses.Draft, null, null, "TP-002", 1, 20, CancellationToken.None);

        Assert.Equal(2, all.Items.Total);
        Assert.Equal(1, all.Summary.RunCount);
        Assert.Equal(2, all.Summary.TotalCases);
        var row = Assert.Single(inProgress.Items.Rows);
        Assert.Equal(active.TestPlanId, row.TestPlanId);
        Assert.Equal(50m, row.ProgressPercent);
        Assert.Equal(100m, row.PassRate);
        Assert.Equal("AUT-TP-002", Assert.Single(drafts.Items.Rows).PlanCode);
        Assert.Equal(TestPlanSummaryDto.Empty, drafts.Summary);
    }

    [Fact]
    public async Task Plans_outside_allowed_projects_and_deleted_plans_are_hidden()
    {
        var c = await CreateAsync();
        var plan = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        var outsider = new TestPlanRepository(c.Db, new ProjectAccessContext { AllowedProjectIds = [Guid.NewGuid()] });
        Assert.Null(await outsider.GetAsync(plan.TestPlanId, CancellationToken.None));
        Assert.Equal(0, (await outsider.ListAsync(null, null, null, null, null, null, 1, 20, CancellationToken.None)).Items.Total);

        var entity = await c.Repository.FindAsync(plan.TestPlanId, CancellationToken.None);
        entity!.SoftDelete(hasRuns: false, null);
        await c.Db.SaveChangesAsync();
        Assert.Null(await c.Repository.GetAsync(plan.TestPlanId, CancellationToken.None));
    }

    // ---------- เฟส 2: Lifecycle / Run / ลบ Cycle / Scope / Policy / Error contract ----------

    private static async Task<(TestPlanDto Plan, TestCycle Cycle)> PlanWithRunAsync(Ctx c, string cycleStatus = "InProgress")
    {
        var cycle = await AddCycleAsync(c, "AUT-CYC-001", cycleStatus, null, "NotRun");
        var plan = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        await c.Service.AttachCycleAsync(plan.TestPlanId, Attach(cycle.TestCycleId), null, CancellationToken.None);
        return (plan, cycle);
    }

    [Fact]
    public async Task Close_requires_reason_and_all_runs_terminal_then_freezes_and_audits()
    {
        var c = await CreateAsync();
        var empty = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        var noRuns = await Assert.ThrowsAsync<TestPlanRuleException>(() => c.Service.CloseAsync(empty.TestPlanId, "done", null, CancellationToken.None));
        Assert.Equal(TestPlanErrorCodes.RunsNotTerminal, noRuns.Code);

        var (plan, cycle) = await PlanWithRunAsync(c);
        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.CloseAsync(plan.TestPlanId, "  ", null, CancellationToken.None));
        var open = await Assert.ThrowsAsync<TestPlanRuleException>(() => c.Service.CloseAsync(plan.TestPlanId, "done", null, CancellationToken.None));
        Assert.Equal(TestPlanErrorCodes.RunsNotTerminal, open.Code);

        cycle.ChangeStatus("Completed");
        await c.Db.SaveChangesAsync();
        var closed = await c.Service.CloseAsync(plan.TestPlanId, "Regression ครบแล้ว", null, CancellationToken.None);

        Assert.Equal(TestPlanStatuses.Closed, closed.Status);
        Assert.True(closed.IsFrozen);
        var audit = await c.Db.AuditLogs.SingleAsync(x => x.EntityId == plan.TestPlanId.ToString() && x.Action == "Closed");
        Assert.Contains("Regression ครบแล้ว", audit.ChangeSummary);
        var unfreeze = await Assert.ThrowsAsync<TestPlanRuleException>(() => c.Service.UnfreezeAsync(plan.TestPlanId, "edit", null, CancellationToken.None));
        Assert.Equal(TestPlanErrorCodes.NotActive, unfreeze.Code);

        var reopened = await c.Service.ReopenAsync(plan.TestPlanId, "ต้องรันเพิ่ม", null, CancellationToken.None);
        Assert.False(reopened.IsFrozen);
        Assert.Equal(TestPlanStatuses.Completed, reopened.Status);
    }

    [Fact]
    public async Task Frozen_plan_blocks_edit_attach_and_run_changes_with_frozen_code()
    {
        var c = await CreateAsync();
        var (plan, _) = await PlanWithRunAsync(c);
        var other = await AddCycleAsync(c, "AUT-CYC-002");
        await c.Service.FreezeAsync(plan.TestPlanId, "รอ Sign-off", null, CancellationToken.None);
        var runId = (await c.Service.GetAsync(plan.TestPlanId, CancellationToken.None)).Runs.Single().TestPlanRunId;

        foreach (var action in new Func<Task>[]
        {
            () => c.Service.UpdateAsync(plan.TestPlanId, PlanRequest(c), null, CancellationToken.None),
            () => c.Service.AttachCycleAsync(plan.TestPlanId, Attach(other.TestCycleId), null, CancellationToken.None),
            () => c.Service.UpdateRunAsync(plan.TestPlanId, runId, new UpdateTestPlanRunRequest("x", null, null, null, null), null, CancellationToken.None),
            () => c.Service.DetachRunAsync(plan.TestPlanId, runId, null, CancellationToken.None),
        })
        {
            var ex = await Assert.ThrowsAsync<TestPlanRuleException>(action);
            Assert.Equal(TestPlanErrorCodes.Frozen, ex.Code);
        }
    }

    [Fact]
    public async Task Create_run_from_suite_creates_populated_cycle_and_run()
    {
        var c = await CreateAsync();
        var suite = new ProMaxx2.QA.Domain.TestManagement.TestSuite(c.Baseline.Project.ProjectId, "TS-1", "Regression", null, null, null);
        c.Db.AddRange(suite, new ProMaxx2.QA.Domain.TestManagement.TestSuiteCase(suite.TestSuiteId, c.Baseline.TestCase.TestCaseId, 1, true));
        await c.Db.SaveChangesAsync();
        var plan = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);

        var run = await c.Service.CreateRunAsync(plan.TestPlanId, new CreateTestPlanRunRequest("REG-WIN", null, "windows-chrome", "Windows 11 · Chrome", null, null, true, null, suite.TestSuiteId, c.Baseline.Build.BuildId, c.Baseline.Environment.TestEnvironmentId, null), null, CancellationToken.None);

        Assert.Equal("REG-WIN", run.RunCode);
        Assert.Equal("Release 1.0 Regression · Windows 11 · Chrome", run.CycleName);
        Assert.Equal(1, run.TotalCases);
        Assert.StartsWith("AUT-CYC-", run.CycleCode);
        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.CreateRunAsync(plan.TestPlanId, new CreateTestPlanRunRequest(null, null, null, null, null, null, true, null, null, c.Baseline.Build.BuildId, c.Baseline.Environment.TestEnvironmentId, null), null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.CreateRunAsync(plan.TestPlanId, new CreateTestPlanRunRequest(null, null, null, null, null, null, false, null, null, null, null, null), null, CancellationToken.None));
    }

    [Fact]
    public async Task Detach_run_is_rejected_once_cycle_ever_had_execution_even_if_deleted()
    {
        var c = await CreateAsync();
        var (plan, cycle) = await PlanWithRunAsync(c);
        var runId = (await c.Service.GetAsync(plan.TestPlanId, CancellationToken.None)).Runs.Single().TestPlanRunId;
        var execution = new TestExecution(cycle.Cases.Single().TestCycleCaseId, 1, c.Baseline.Build.BuildId, c.Baseline.Environment.TestEnvironmentId, Guid.NewGuid(), "Pass", null, null, []);
        execution.SoftDelete(null);
        c.Db.Add(execution);
        await c.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<TestPlanRuleException>(() => c.Service.DetachRunAsync(plan.TestPlanId, runId, null, CancellationToken.None));
        Assert.Equal(TestPlanErrorCodes.RunHasExecutions, ex.Code);
    }

    [Fact]
    public async Task Detach_run_without_execution_unlinks_cycle_and_allows_cycle_delete()
    {
        var c = await CreateAsync();
        var (plan, cycle) = await PlanWithRunAsync(c);
        var runId = (await c.Service.GetAsync(plan.TestPlanId, CancellationToken.None)).Runs.Single().TestPlanRunId;
        await Assert.ThrowsAsync<TestCycleLinkedToPlanException>(() => c.Cycles.DeleteAsync(cycle.TestCycleId, null, CancellationToken.None));

        await c.Service.DetachRunAsync(plan.TestPlanId, runId, null, CancellationToken.None);

        Assert.Empty(await c.Db.TestPlanRuns.ToListAsync());
        Assert.True(await c.Db.TestCycles.AnyAsync(x => x.TestCycleId == cycle.TestCycleId && !x.IsDeleted));
        await c.Cycles.DeleteAsync(cycle.TestCycleId, null, CancellationToken.None);
        Assert.True((await c.Db.TestCycles.SingleAsync(x => x.TestCycleId == cycle.TestCycleId)).IsDeleted);
    }

    [Fact]
    public async Task Deleting_plan_requires_closed_or_cancelled_and_keeps_cycle_locked()
    {
        var c = await CreateAsync();
        var (plan, cycle) = await PlanWithRunAsync(c);
        var active = await Assert.ThrowsAsync<TestPlanRuleException>(() => c.Service.DeleteAsync(plan.TestPlanId, null, CancellationToken.None));
        Assert.Equal(TestPlanErrorCodes.HasRuns, active.Code);

        await c.Service.CancelAsync(plan.TestPlanId, "ยกเลิกแผน", null, CancellationToken.None);
        await c.Service.DeleteAsync(plan.TestPlanId, null, CancellationToken.None);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => c.Service.GetAsync(plan.TestPlanId, CancellationToken.None));
        var blocked = await Assert.ThrowsAsync<TestCycleLinkedToPlanException>(() => c.Cycles.DeleteAsync(cycle.TestCycleId, null, CancellationToken.None));
        Assert.Contains("ลบแล้ว", blocked.Message);
        var other = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        await Assert.ThrowsAsync<TestPlanRuleException>(() => c.Service.AttachCycleAsync(other.TestPlanId, Attach(cycle.TestCycleId), null, CancellationToken.None));
    }

    [Fact]
    public async Task Matrix_filters_by_run_status_configuration_and_pages()
    {
        var c = await CreateAsync();
        var plan = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        var a = await AddCycleAsync(c, "AUT-CYC-001", "InProgress");
        var b = await AddCycleAsync(c, "AUT-CYC-002", "Draft");
        var d = await AddCycleAsync(c, "AUT-CYC-003", "InProgress");
        await c.Service.AttachCycleAsync(plan.TestPlanId, Attach(a.TestCycleId), null, CancellationToken.None);
        await c.Service.AttachCycleAsync(plan.TestPlanId, Attach(b.TestCycleId) with { ConfigurationKey = "android-pixel" }, null, CancellationToken.None);
        await c.Service.AttachCycleAsync(plan.TestPlanId, Attach(d.TestCycleId), null, CancellationToken.None);

        Assert.Equal(2, (await c.Service.MatrixAsync(plan.TestPlanId, "InProgress", null, null, 1, 50, CancellationToken.None)).Total);
        Assert.Equal("AUT-CYC-002", Assert.Single((await c.Service.MatrixAsync(plan.TestPlanId, null, "Android-Pixel", null, 1, 50, CancellationToken.None)).Rows).RunCode);
        var paged = await c.Service.MatrixAsync(plan.TestPlanId, null, null, null, 2, 2, CancellationToken.None);
        Assert.Equal(3, paged.Total);
        Assert.Single(paged.Rows);
    }

    [Fact]
    public async Task Scope_guard_hides_plan_and_run_of_other_projects()
    {
        var c = await CreateAsync();
        var (plan, _) = await PlanWithRunAsync(c);
        var runId = (await c.Service.GetAsync(plan.TestPlanId, CancellationToken.None)).Runs.Single().TestPlanRunId;
        var member = new ProjectScopeGuard(c.Db, c.Access);
        var outsider = new ProjectScopeGuard(c.Db, new ProjectAccessContext { AllowedProjectIds = [Guid.NewGuid()] });

        Assert.True(await member.RouteValueAsync("planId", plan.TestPlanId, CancellationToken.None));
        Assert.True(await member.RouteValueAsync("runId", runId, CancellationToken.None));
        Assert.False(await outsider.RouteValueAsync("planId", plan.TestPlanId, CancellationToken.None));
        Assert.False(await outsider.RouteValueAsync("runId", runId, CancellationToken.None));
    }

    [Theory]
    [InlineData("TestPlanView", "TESTPLAN.VIEW")]
    [InlineData("TestPlanEdit", "TESTPLAN.EDIT")]
    [InlineData("TestPlanFreeze", "TESTPLAN.FREEZE")]
    [InlineData("TestPlanAssign", "TESTPLAN.ASSIGN")]
    public async Task Policies_require_their_permission_or_sys_admin(string policy, string permission)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddAuthorizationBuilder().AddTestPlanPolicies();
        var auth = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
        static ClaimsPrincipal With(string type, string value) => new(new ClaimsIdentity([new Claim(type, value)], "TestAuth"));

        Assert.True((await auth.AuthorizeAsync(With("permission", permission), null, policy)).Succeeded);
        Assert.True((await auth.AuthorizeAsync(With(ClaimTypes.Role, "SYS_ADMIN"), null, policy)).Succeeded);
        Assert.False((await auth.AuthorizeAsync(With("permission", "EXECUTION.RUN"), null, policy)).Succeeded);
    }

    [Fact]
    public async Task Controller_maps_rule_violation_to_409_with_test_plan_code()
    {
        var c = await CreateAsync();
        var (plan, _) = await PlanWithRunAsync(c);
        await c.Service.FreezeAsync(plan.TestPlanId, "lock", null, CancellationToken.None);
        var controller = new ProMaxx2.QA.Api.Controllers.TestPlansController(c.Service)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
        };

        var frozen = await controller.Update(plan.TestPlanId, PlanRequest(c), CancellationToken.None);
        var missing = await controller.Get(Guid.NewGuid(), CancellationToken.None);
        var invalid = await controller.Freeze(plan.TestPlanId, new TestPlanActionRequest(""), CancellationToken.None);

        var conflict = Assert.IsType<Microsoft.AspNetCore.Mvc.ConflictObjectResult>(frozen.Result);
        Assert.Equal(TestPlanErrorCodes.Frozen, Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(conflict.Value).Extensions["code"]);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundObjectResult>(missing.Result);
        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(invalid.Result);
    }

    // ---------- เฟส 4: Run-level Assignment / Clone-Rerun ----------

    private static async Task<Guid> AddMemberAsync(Ctx c)
    {
        var user = new ProMaxx2.QA.Domain.Identity.User($"member-{Guid.NewGuid():N}"[..20], "Member", null, "x");
        var userId = user.UserId;
        c.Db.AddRange(user, new ProMaxx2.QA.Domain.Identity.ProjectUser(c.Baseline.Project.ProjectId, userId, null));
        await c.Db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task Assign_replaces_team_and_round_robins_only_unassigned_not_run_cases()
    {
        var c = await CreateAsync();
        var cycle = await AddCycleAsync(c, "AUT-CYC-001", "InProgress", null, "NotRun", "NotRun", "NotRun", "Pass", "NotRun");
        var existingOwner = Guid.NewGuid();
        var ordered = cycle.Cases.OrderBy(x => x.ExecutionOrder).ToList();
        ordered[4].AssignTester(existingOwner);
        await c.Db.SaveChangesAsync();
        var plan = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        var run = await c.Service.AttachCycleAsync(plan.TestPlanId, Attach(cycle.TestCycleId), null, CancellationToken.None);
        var a = await AddMemberAsync(c);
        var b = await AddMemberAsync(c);
        var reviewer = await AddMemberAsync(c);
        var actor = Guid.NewGuid();

        var result = await c.Service.AssignRunAsync(plan.TestPlanId, run.TestPlanRunId, new AssignTestPlanRunRequest([new(a, "tester"), new(b, "Tester"), new(reviewer, "Reviewer")], true), actor, CancellationToken.None);

        Assert.Equal(3, (await c.Service.MembersAsync(plan.TestPlanId, CancellationToken.None)).Count);
        Assert.Equal(3, result.AssignedCases);
        Assert.Equal(3, result.Run.Assignments.Count);
        Assert.Equal(0, result.Run.UnassignedCases);
        Assert.Equal(a, ordered[0].AssignedTesterUserId);
        Assert.Equal(b, ordered[1].AssignedTesterUserId);
        Assert.Equal(a, ordered[2].AssignedTesterUserId);
        Assert.Null(ordered[3].AssignedTesterUserId);              // Pass แล้ว ไม่ assign
        Assert.Equal(existingOwner, ordered[4].AssignedTesterUserId); // มีผู้รับผิดชอบแล้ว ไม่เขียนทับ
        Assert.Equal(3, await c.Db.TestCycleCaseAssignments.CountAsync(x => x.Status == "Assigned"));
        Assert.Equal(3, await c.Db.AssignmentHistories.CountAsync(x => x.AlgorithmVersion == "testplan-round-robin-v1"));

        // ส่งทีมใหม่ = แทนที่ทั้งชุด แต่ไม่ถอด assignment รายเคสที่ทำไปแล้ว
        var replaced = await c.Service.AssignRunAsync(plan.TestPlanId, run.TestPlanRunId, new AssignTestPlanRunRequest([new(b, "Tester")], false), actor, CancellationToken.None);
        Assert.Equal(b, Assert.Single(replaced.Run.Assignments).TesterUserId);
        Assert.Equal(a, ordered[0].AssignedTesterUserId);
    }

    [Fact]
    public async Task Assign_rejects_non_member_missing_tester_for_bulk_and_frozen_plan()
    {
        var c = await CreateAsync();
        var (plan, _) = await PlanWithRunAsync(c);
        var runId = (await c.Service.GetAsync(plan.TestPlanId, CancellationToken.None)).Runs.Single().TestPlanRunId;
        var member = await AddMemberAsync(c);
        var actor = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.AssignRunAsync(plan.TestPlanId, runId, new AssignTestPlanRunRequest([new(Guid.NewGuid(), "Tester")], false), actor, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.AssignRunAsync(plan.TestPlanId, runId, new AssignTestPlanRunRequest([new(member, "Reviewer")], true), actor, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.AssignRunAsync(plan.TestPlanId, runId, new AssignTestPlanRunRequest([new(member, "Owner")], false), actor, CancellationToken.None));
        await c.Service.FreezeAsync(plan.TestPlanId, "lock", null, CancellationToken.None);
        var frozen = await Assert.ThrowsAsync<TestPlanRuleException>(() => c.Service.AssignRunAsync(plan.TestPlanId, runId, new AssignTestPlanRunRequest([new(member, "Tester")], true), actor, CancellationToken.None));
        Assert.Equal(TestPlanErrorCodes.Frozen, frozen.Code);
    }

    [Fact]
    public async Task Clone_creates_new_plan_on_target_build_without_history_and_keeps_source()
    {
        var c = await CreateAsync();
        var cycle = await AddCycleAsync(c, "AUT-CYC-001", "Completed", null, "Pass", "Fail");
        var tester = Guid.NewGuid();
        cycle.Cases.First().AssignTester(tester);
        c.Db.Add(new TestExecution(cycle.Cases.First().TestCycleCaseId, 1, c.Baseline.Build.BuildId, c.Baseline.Environment.TestEnvironmentId, tester, "Pass", null, null, []));
        var newBuild = new Build(c.Baseline.Release.ReleaseId, "2", "1.1", null, null, DateTime.UtcNow, null, null, null);
        var newEnv = new TestEnvironment(c.Baseline.Project.ProjectId, "UAT", null);
        c.Db.AddRange(newBuild, newEnv);
        await c.Db.SaveChangesAsync();
        var source = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        var run = await c.Service.AttachCycleAsync(source.TestPlanId, Attach(cycle.TestCycleId, "REG-WIN", "{\"os\":\"Windows 11\"}"), null, CancellationToken.None);
        var member = await AddMemberAsync(c);
        var actor = Guid.NewGuid();
        await c.Service.AssignRunAsync(source.TestPlanId, run.TestPlanRunId, new AssignTestPlanRunRequest([new(member, "Tester")], false), actor, CancellationToken.None);
        await c.Service.CloseAsync(source.TestPlanId, "done", null, CancellationToken.None);

        var clone = await c.Service.CloneAsync(source.TestPlanId, new CloneTestPlanRequest(c.Baseline.Release.ReleaseId, newBuild.BuildId, null, [new(run.TestPlanRunId, newEnv.TestEnvironmentId)], null, null), actor, CancellationToken.None);

        Assert.NotEqual(source.TestPlanId, clone.TestPlanId);
        Assert.Equal(source.TestPlanId, clone.CopiedFromTestPlanId);
        Assert.Equal("AUT-TP-002", clone.PlanCode);
        Assert.Equal("Release 1.0 Regression (Rerun)", clone.PlanName);
        Assert.Equal(TestPlanStatuses.Draft, clone.Status);
        Assert.False(clone.IsFrozen);
        var newRun = Assert.Single(clone.Runs);
        Assert.Equal("REG-WIN", newRun.RunCode);
        Assert.Equal("windows-chrome", newRun.ConfigurationKey);
        Assert.Equal("{\"os\":\"Windows 11\"}", newRun.ConfigurationJson);
        Assert.Equal(newBuild.BuildId, newRun.BuildId);
        Assert.Equal(newEnv.TestEnvironmentId, newRun.EnvironmentId);
        Assert.NotEqual(cycle.TestCycleId, newRun.TestCycleId);
        Assert.Equal(2, newRun.TotalCases);
        Assert.Equal(2, newRun.Untested);
        Assert.Equal(member, Assert.Single(newRun.Assignments).TesterUserId);
        var newCases = await c.Db.TestCycleCases.Where(x => x.TestCycleId == newRun.TestCycleId).ToListAsync();
        Assert.All(newCases, x => Assert.Null(x.AssignedTesterUserId));
        Assert.False(await c.Db.TestExecutions.AnyAsync(x => x.CycleCase.TestCycleId == newRun.TestCycleId));
        var original = await c.Service.GetAsync(source.TestPlanId, CancellationToken.None);
        Assert.Equal(TestPlanStatuses.Closed, original.Status);
        Assert.Equal(cycle.TestCycleId, Assert.Single(original.Runs).TestCycleId);
        Assert.Single(await c.Db.AuditLogs.Where(x => x.EntityId == clone.TestPlanId.ToString() && x.Action == "Cloned").ToListAsync());
    }

    [Fact]
    public async Task Clone_rejects_plan_without_runs_other_modes_and_unknown_override()
    {
        var c = await CreateAsync();
        var empty = await c.Service.CreateAsync(PlanRequest(c), null, CancellationToken.None);
        var actor = Guid.NewGuid();
        var request = new CloneTestPlanRequest(c.Baseline.Release.ReleaseId, c.Baseline.Build.BuildId, null, null, null, null);
        var noRuns = await Assert.ThrowsAsync<TestPlanRuleException>(() => c.Service.CloneAsync(empty.TestPlanId, request, actor, CancellationToken.None));
        Assert.Equal(TestPlanErrorCodes.NoRuns, noRuns.Code);

        var (plan, _) = await PlanWithRunAsync(c);
        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.CloneAsync(plan.TestPlanId, request with { CloneMode = "SuiteLatest" }, actor, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.CloneAsync(plan.TestPlanId, request with { EnvironmentOverrides = [new(Guid.NewGuid(), c.Baseline.Environment.TestEnvironmentId)] }, actor, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => c.Service.CloneAsync(plan.TestPlanId, request, null, CancellationToken.None));
    }

    // ---------- แปลงเป็น SQL ได้ (ไม่เชื่อมต่อ DB จริง) ----------

    private static QaDbContext SqlServerDb() => new(new DbContextOptionsBuilder<QaDbContext>()
        .UseSqlServer(@"Server=(localdb)\mssqllocaldb;Database=ProMaxx2QA;Trusted_Connection=True;TrustServerCertificate=True").Options);

    [Fact]
    public void Computed_status_and_counters_translate_to_sql()
    {
        using var db = SqlServerDb();
        var sql = TestPlanRepository.WithStatus(db.TestPlans.AsNoTracking().Where(x => !x.IsDeleted))
            .Where(x => x.Status == TestPlanStatuses.InProgress)
            .OrderByDescending(x => x.Row.Plan.CreatedAt).Skip(0).Take(20)
            .Select(x => new { x.Row.Plan.PlanCode, x.Status, x.Row.TotalCases, x.Row.Passed })
            .ToQueryString();
        Assert.Contains("CASE", sql);
        Assert.Contains("[LifecycleStatus]", sql);
        Assert.Contains("[CurrentStatus]", sql);
    }

    [Fact]
    public void Retest_query_translates_to_grouped_sql()
    {
        using var db = SqlServerDb();
        var sql = TestPlanRepository.RetestCountsQuery(db, db.TestPlanRuns.Where(r => r.TestPlanId == Guid.Empty)).ToQueryString();
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("HAVING", sql);
        Assert.Contains("[IsDeleted]", sql);
    }
}
