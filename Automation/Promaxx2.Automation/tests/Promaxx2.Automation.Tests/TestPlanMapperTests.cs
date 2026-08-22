using Promaxx2.Automation.Hub;
using Promaxx2.Automation.Core;
using NUnit.Framework;

namespace Promaxx2.Automation.Tests;

[TestFixture]
public class TestPlanMapperTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ModuleId = Guid.NewGuid();

    private static TestCaseDto MakeCase(string code, string title, params StepDto[] steps) => new(
        Guid.NewGuid(), ProjectId, ModuleId, code, title,
        null, null, "P0", "Functional", true, "Ready", 1, null, steps);

    [Test]
    public void Maps_code_title_priority_module_and_steps()
    {
        var source = new TestPlanSource(
            [
                MakeCase("TC-SALE-001", "ขายสินค้าสด",
                    new StepDto(1, "เปิดหน้าขาย", "", "หน้าขายแสดง"),
                    new StepDto(2, "สแกนสินค้า", "SKU=1001", "รายการเพิ่ม 1 แถว"))
            ],
            new Dictionary<Guid, string> { [ModuleId] = "TRN · Transaction" });

        var plan = TestPlanMapper.ToTestPlan(source, targetApp: "pos");

        Assert.That(plan.Cases, Has.Count.EqualTo(1));
        var c = plan.Cases[0];
        Assert.Multiple(() =>
        {
            Assert.That(plan.SchemaVersion, Is.EqualTo("1.1"));
            Assert.That(plan.ProjectId, Is.EqualTo(ProjectId));
            Assert.That(c.TestCaseId, Is.Not.Null);
            Assert.That(c.TestCaseCode, Is.EqualTo("TC-SALE-001"));
            Assert.That(c.TargetApp, Is.EqualTo("pos"));
            Assert.That(c.Module, Is.EqualTo("TRN · Transaction"));
            Assert.That(c.Steps, Has.Count.EqualTo(2));
            Assert.That(c.Steps[1].Data, Is.EqualTo("SKU=1001"));
        });
    }

    [Test]
    public void Sorts_cases_by_code_and_steps_by_step_no()
    {
        var source = new TestPlanSource(
            [
                MakeCase("TC-B-002", "second"),
                MakeCase("TC-A-001", "first", new StepDto(2, "b", "", ""), new StepDto(1, "a", "", ""))
            ],
            new Dictionary<Guid, string>());

        var plan = TestPlanMapper.ToTestPlan(source, targetApp: "pos");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Cases.Select(c => c.TestCaseCode), Is.EqualTo(new[] { "TC-A-001", "TC-B-002" }));
            Assert.That(plan.Cases[0].Steps.Select(s => s.Action), Is.EqualTo(new[] { "a", "b" }));
        });
    }

    [Test]
    public void Rejects_duplicate_case_codes()
    {
        var source = new TestPlanSource(
            [MakeCase("TC-DUP-001", "one"), MakeCase("TC-DUP-001", "two")],
            new Dictionary<Guid, string>());

        Assert.Throws<InvalidOperationException>(() => TestPlanMapper.ToTestPlan(source, targetApp: "pos"));
    }

    [Test]
    public void Rejects_invalid_target_app()
    {
        var source = new TestPlanSource([], new Dictionary<Guid, string>());
        Assert.Throws<ArgumentException>(() => TestPlanMapper.ToTestPlan(source, targetApp: "web"));
    }

    [Test]
    public void Routes_master_data_cases_to_promaxxs_app_when_explicitly_selected()
    {
        var source = new TestPlanSource(
            [MakeCase("TC-ITEM-001", "เพิ่มสินค้า")],
            new Dictionary<Guid, string> { [ModuleId] = "Inventory Master" });

        var plan = TestPlanMapper.ToTestPlan(source, targetApp: "app");

        Assert.That(plan.Cases.Single().TargetApp, Is.EqualTo("app"));
    }

    [Test]
    public void Scanner_diff_reports_added_removed_and_changed_ids()
    {
        var baseline = new[] { new ScannedScreen("Home", [
            new("SaveButton", "Save", "Button", "Button", "0"),
            new("OldButton", "Old", "Button", "Button", "1")], [], []) };
        var current = new[] { new ScannedScreen("Home", [
            new("SaveButton", "Save", "TextBox", "Edit", "0"),
            new("NewButton", "New", "Button", "Button", "1")], [], []) };

        var diff = AutomationIdScanner.CompareReports(baseline,current);

        Assert.That(diff.Added, Is.EqualTo(new[] { "Home/NewButton" }));
        Assert.That(diff.Removed, Is.EqualTo(new[] { "Home/OldButton" }));
        Assert.That(diff.Changed, Is.EqualTo(new[] { "Home/SaveButton" }));
    }

    [Test]
    public void Quality_gate_allows_legacy_findings_but_blocks_new_regressions()
    {
        var baselineScreen = new ScannedScreen("Home", [new("Save", "Save", "Button", "Button", "0")], ["Button: legacy (1)"], []);
        var currentScreen = new ScannedScreen("Home", [], ["Button: legacy (1)", "Button: new (2)"], ["Duplicate (2)"]);
        var baseline = new ScannerReport("1.0", "app", "1.0", "app.exe", "now", [baselineScreen], null);
        var current = new ScannerReport("1.0", "app", "1.1", "app.exe", "now", [currentScreen], null);

        var result = AutomationIdQualityGate.Evaluate(baseline, current, new());

        Assert.That(result.Passed, Is.False);
        Assert.That(result.NewMissingAutomationIds, Has.Count.EqualTo(1));
        Assert.That(result.NewDuplicateAutomationIds, Has.Count.EqualTo(1));
        Assert.That(result.RemovedAutomationIds, Is.EqualTo(new[] { "Home/Save" }));
    }
}
