using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProMaxx2.QA.Api.Controllers;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;
using Xunit;

namespace ProMaxx2.QA.UnitTests;

/// <summary>ค่าส่วนตัวของผู้ใช้ (Sidebar ปักหมุด/ล่าสุด) — แยกตาม user ใน token และตามไปทุกเครื่อง</summary>
public sealed class UserPreferencesTests
{
    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static UserPreferencesController Controller(QaDbContext db, Guid userId) => new(db)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "test")) },
        },
    };

    private static SaveUserPreferenceRequest Body(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    [Fact]
    public async Task Get_returns_no_content_before_first_save()
    {
        await using var db = CreateDb();
        var result = await Controller(db, Guid.NewGuid()).Get("sidebar", default);
        Assert.IsType<NoContentResult>(result.Result);
    }

    [Fact]
    public async Task Save_then_get_returns_value_and_overwrites_on_second_save()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var controller = Controller(db, userId);

        Assert.IsType<NoContentResult>(await controller.Save("sidebar", Body("""{"pins":["defects"],"recent":[],"closed":[]}"""), default));
        Assert.IsType<NoContentResult>(await controller.Save("Sidebar", Body("""{"pins":["execution","defects"],"recent":["dashboard"],"closed":["ตั้งค่า"]}"""), default));

        var ok = Assert.IsType<OkObjectResult>((await controller.Get("sidebar", default)).Result);
        var view = Assert.IsType<UserPreferenceView>(ok.Value);
        Assert.Equal(["execution", "defects"], view.Value.GetProperty("pins").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("ตั้งค่า", view.Value.GetProperty("closed")[0].GetString());
        Assert.Equal(1, await db.UserPreferences.CountAsync());
    }

    [Fact]
    public async Task Each_user_sees_only_their_own_value()
    {
        await using var db = CreateDb();
        var alice = Guid.NewGuid();
        await Controller(db, alice).Save("sidebar", Body("""{"pins":["defects"]}"""), default);

        var result = await Controller(db, Guid.NewGuid()).Get("sidebar", default);
        Assert.IsType<NoContentResult>(result.Result);
    }

    [Theory]
    [InlineData("../etc")]
    [InlineData("")]
    [InlineData("has space")]
    public async Task Invalid_key_is_rejected(string key)
    {
        await using var db = CreateDb();
        var result = await Controller(db, Guid.NewGuid()).Save(key, Body("{}"), default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Oversized_value_is_rejected()
    {
        await using var db = CreateDb();
        var big = JsonSerializer.Serialize(new { pins = new[] { new string('x', UserPreference.MaxValueLength) } });
        var result = await Controller(db, Guid.NewGuid()).Save("sidebar", Body(big), default);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, await db.UserPreferences.CountAsync());
    }
}
