using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

public sealed class CrmFlowTrackingTests
{
    [Fact]
    public async Task RecordSnapshots_DeduplicatesUnchangedTickets_AndStoresChangedRoute()
    {
        await using var db = CreateDb();
        var service = new CrmFlowTrackingService(db);
        var userId = Guid.NewGuid();

        var first = Ticket(status: "Continue", owner: "1001", assignee: "2002", developer: "0");
        await service.RecordSnapshotsAsync(userId, [first], CancellationToken.None);
        await service.RecordSnapshotsAsync(userId, [first], CancellationToken.None);

        var movedToDevelopment = first with { Status = "Develop", Assignee = "2003", Developer = "3003" };
        await service.RecordSnapshotsAsync(userId, [movedToDevelopment], CancellationToken.None);

        var rows = await db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityType == "CrmTicketFlow" && x.EntityId == first.JobNo)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal("FlowStarted", rows[0].Action);
        Assert.Null(rows[0].BeforeJson);
        Assert.Contains("Continue", rows[0].AfterJson);
        Assert.Equal("FlowChanged", rows[1].Action);
        Assert.Contains("Continue", rows[1].BeforeJson);
        Assert.Contains("Develop", rows[1].AfterJson);
        Assert.Contains("3003", rows[1].AfterJson);
    }

    [Fact]
    public async Task GetHistory_ReturnsNewestFirst_WithActorNameAndNormalizedDeveloper()
    {
        await using var db = CreateDb();
        var user = new ProMaxx2.QA.Domain.Identity.User("qa.tester", "QA Tester", "qa.tester@example.test", "hash");
        var userId = user.UserId;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new CrmFlowTrackingService(db);
        var ticket = Ticket(status: "Open", owner: "1001", assignee: "2002", developer: "0");
        await service.RecordSnapshotsAsync(userId, [ticket], CancellationToken.None);
        await service.RecordSnapshotsAsync(userId, [ticket with { Status = "Test", Developer = "3003" }], CancellationToken.None);

        var history = await service.GetHistoryAsync(ticket.JobNo, CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Equal("FlowChanged", history[0].Action);
        Assert.Equal("QA Tester", history[0].ActorName);
        Assert.Equal("3003", history[0].After.Developer);
        Assert.Equal("Test", history[0].After.Status);
        Assert.Equal("Open", history[0].Before?.Status);
        Assert.Null(history[1].After.Developer);
    }

    [Fact]
    public async Task RecordSnapshots_UsesLatestCaseInsensitiveJobSnapshot()
    {
        await using var db = CreateDb();
        var service = new CrmFlowTrackingService(db);
        var userId = Guid.NewGuid();

        await service.RecordSnapshotsAsync(userId,
        [
            Ticket(jobNo: " BHD-100 ", status: "Open", owner: "1001", assignee: "2002", developer: null),
            Ticket(jobNo: "bhd-100", status: "Continue", owner: "1001", assignee: "2002", developer: null)
        ], CancellationToken.None);

        var history = await service.GetHistoryAsync("BHD-100", CancellationToken.None);

        var entry = Assert.Single(history);
        Assert.Equal("Continue", entry.After.Status);
        Assert.Equal("BHD-100", entry.After.JobNo);
    }

    [Fact]
    public async Task PreviouslyAssigned_FindsJobsHandedBackFromQa_WithoutMatchingPartialCodes()
    {
        await using var db = CreateDb();
        var service = new CrmFlowTrackingService(db);
        var userId = Guid.NewGuid();

        var handedBack = Ticket(jobNo: "BHD691006000009", status: "Continue", owner: "6710", assignee: "6101");
        await service.RecordSnapshotsAsync(userId, [handedBack], CancellationToken.None);
        await service.RecordSnapshotsAsync(userId, [handedBack with { Status = "Test", Assignee = "6710" }], CancellationToken.None);
        await service.RecordSnapshotsAsync(userId, [Ticket(jobNo: "BHD-OTHER", owner: "6710", assignee: "61010")], CancellationToken.None);
        await service.RecordSnapshotsAsync(userId, [Ticket(jobNo: "BHD-OWNER-ONLY", owner: "6101", assignee: "6710")], CancellationToken.None);

        var jobs = await service.GetJobsPreviouslyAssignedToAsync("6101", CancellationToken.None);

        Assert.Equal(new[] { "BHD691006000009" }, jobs.ToArray());
        Assert.True(jobs.Contains("bhd691006000009"));
        Assert.True(await service.WasPreviouslyAssignedToAsync(" bhd691006000009 ", "6101", CancellationToken.None));
        Assert.False(await service.WasPreviouslyAssignedToAsync("BHD-OTHER", "6101", CancellationToken.None));
        Assert.Empty(await service.GetJobsPreviouslyAssignedToAsync(" ", CancellationToken.None));
    }

    [Fact]
    public async Task GetLastQa_ReturnsQaBeforeHandBack_AndSkipsSelfAssignedTickets()
    {
        await using var db = CreateDb();
        var service = new CrmFlowTrackingService(db);
        var userId = Guid.NewGuid();

        var handedBack = Ticket(jobNo: "BHD691006000009", status: "Continue", owner: "6710", assignee: "6101");
        await service.RecordSnapshotsAsync(userId, [handedBack], CancellationToken.None);
        await service.RecordSnapshotsAsync(userId, [handedBack with { Status = "Test", Assignee = "ชัยณุชา ทดสอบ (6710)" }], CancellationToken.None);
        await service.RecordSnapshotsAsync(userId, [Ticket(jobNo: "BHD-SELF", owner: "6101", assignee: "6101")], CancellationToken.None);

        var lastQa = await service.GetLastQaAsync(["bhd691006000009", "BHD-SELF", "BHD-UNKNOWN"], CancellationToken.None);

        Assert.Equal("6101", Assert.Single(lastQa).Value);
        Assert.Equal("6101", lastQa["BHD691006000009"]);
    }

    [Fact]
    public async Task GetLastQa_SkipsDeveloperEvenWhenAssigntoMovedBeforeSysDevelopWasSet()
    {
        // BHD690929000002: QA 6101 → Assignto 4208 (sysDevelop still empty) → sysDevelop 4208
        await using var db = CreateDb();
        var service = new CrmFlowTrackingService(db);
        var userId = Guid.NewGuid();
        var ticket = Ticket(jobNo: "BHD690929000002", owner: "6511", assignee: "6101", developer: null);

        await service.RecordSnapshotsAsync(userId, [ticket], CancellationToken.None);
        await service.RecordSnapshotsAsync(userId, [ticket with { Assignee = "4208" }], CancellationToken.None);
        await service.RecordSnapshotsAsync(userId, [ticket with { Assignee = "4208", Developer = "4208" }], CancellationToken.None);

        var lastQa = await service.GetLastQaAsync(["BHD690929000002"], CancellationToken.None);

        Assert.Equal("6101", lastQa["BHD690929000002"]);
    }

    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static CrmTicketListItem Ticket(
        string jobNo = "BHD-100",
        string status = "Continue",
        string? owner = "1001",
        string? assignee = "2002",
        string? developer = null) => new(
            jobNo,
            "Subject",
            status,
            "HD",
            "Question",
            "Product",
            assignee,
            owner,
            "Customer",
            "2026-10-06T08:00:00Z",
            null,
            "2026-10-06T08:30:00Z",
            "Branch",
            developer,
            null);
}
