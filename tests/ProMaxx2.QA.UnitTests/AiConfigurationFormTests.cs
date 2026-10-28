using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>Setting Center → AI Configuration: บันทึก opencode ไม่มีคีย์ และ runtime จากค่าในฟอร์มสำหรับปุ่ม "ทดสอบการเชื่อมต่อ"</summary>
public sealed class AiConfigurationFormTests
{
    [Fact]
    public async Task Save_opencode_without_api_key_is_allowed()
    {
        await using var db = CreateDb();
        var view = await CreateService(db).SaveAsync("opencode", "beam-free", "https://opencode.ai/zen/go/v1", null, true, false, CancellationToken.None);

        Assert.Equal("opencode", view.Provider);
        Assert.False(view.HasApiKey);
    }

    [Fact]
    public async Task Form_runtime_reuses_saved_key_only_for_the_same_provider()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        await service.SaveAsync("Anthropic", "claude-sonnet-5-5", null, "sk-ant-saved", true, false, CancellationToken.None);

        var same = await service.ResolveFormRuntimeAsync("Anthropic", "claude-opus-5-5", null, null, CancellationToken.None);
        Assert.Equal("sk-ant-saved", same.ApiKey);
        Assert.Equal("claude-opus-5-5", same.Model);

        var typed = await service.ResolveFormRuntimeAsync("Anthropic", "claude-opus-5-5", null, " sk-ant-new ", CancellationToken.None);
        Assert.Equal("sk-ant-new", typed.ApiKey);

        await Assert.ThrowsAsync<AiNotConfiguredException>(() => service.ResolveFormRuntimeAsync("Google", "gemini-3.5-flash", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Form_runtime_validates_model_and_base_url()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ResolveFormRuntimeAsync("Local", " ", "http://localhost:11434/v1", null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ResolveFormRuntimeAsync("Local", "qwen3", "localhost:11434", null, CancellationToken.None));
        var local = await service.ResolveFormRuntimeAsync("Local", "qwen3", "http://localhost:11434/v1/", null, CancellationToken.None);
        Assert.Equal("http://localhost:11434/v1", local.BaseUrl);
        Assert.Equal("", local.ApiKey);
    }

    private static SharedAiConfigurationService CreateService(QaDbContext db) => new(db, new EphemeralDataProtectionProvider(), new ConfigurationBuilder().Build(), new NoHttp());
    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("ไม่ควรเรียก HTTP");
    }
}
