using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Domain.Settings;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.UnitTests;

/// <summary>OAuth 2.1 (DCR + PKCE + refresh rotation) ให้ claude.ai Team เชื่อมต่อ MCP แบบ Login รายคน</summary>
public sealed class KnowledgeOAuthTests
{
    private const string Callback = "https://claude.ai/api/mcp/auth_callback";
    private const string Resource = "https://api.example.test/mcp";

    // ---------- PKCE / redirect ----------

    [Fact]
    public void Pkce_S256VerifiesOnlyMatchingVerifier()
    {
        var (verifier, challenge) = NewPkce();
        Assert.True(OAuthSecrets.VerifyPkce(verifier, challenge));
        Assert.False(OAuthSecrets.VerifyPkce(verifier + "x", challenge));
        Assert.False(OAuthSecrets.VerifyPkce("short", challenge));
        Assert.False(OAuthSecrets.VerifyPkce(null, challenge));
        Assert.False(OAuthSecrets.VerifyPkce(new string('a', 42) + "!", challenge));
    }

    [Theory]
    [InlineData(Callback, true)]
    [InlineData("http://localhost:53682/callback", true)]
    [InlineData("http://127.0.0.1:9999/callback", true)]
    [InlineData("https://evil.example/api/mcp/auth_callback", false)]
    [InlineData("https://claude.ai/api/mcp/auth_callback#x", false)]
    [InlineData("https://localhost/callback", false)]
    [InlineData("not a url", false)]
    public void Redirect_Allowlist(string uri, bool allowed)
    {
        using var db = CreateDb();
        Assert.Equal(allowed, OAuth(db).IsAllowedRedirectUri(uri));
    }

    [Fact]
    public void Redirect_LoopbackIgnoresPortOnly()
    {
        Assert.True(OAuthService.RedirectMatches("http://localhost:1111/callback", "http://localhost:2222/callback"));
        Assert.False(OAuthService.RedirectMatches("http://localhost:1111/callback", "http://localhost:2222/other"));
        Assert.False(OAuthService.RedirectMatches("http://localhost:1111/callback", "http://127.0.0.1:1111/callback"));
        Assert.False(OAuthService.RedirectMatches(Callback, Callback + "?x=1"));
        Assert.True(OAuthService.RedirectMatches(Callback, Callback));
    }

    // ---------- metadata ----------

    [Fact]
    public void Metadata_AdvertisesResourceDcrAndS256WithoutCimd()
    {
        using var db = CreateDb();
        var service = OAuth(db);
        var prm = JsonSerializer.Serialize(service.ProtectedResourceMetadata());
        Assert.Contains($"\"resource\":\"{Resource}\"", prm);
        Assert.Contains("\"authorization_servers\":[\"https://api.example.test\"]", prm);
        var asm = JsonSerializer.Serialize(service.AuthorizationServerMetadata());
        Assert.Contains("\"code_challenge_methods_supported\":[\"S256\"]", asm);
        Assert.Contains("\"registration_endpoint\":\"https://api.example.test/oauth/register\"", asm);
        Assert.Contains("\"token_endpoint_auth_methods_supported\":[\"none\"]", asm);
        Assert.DoesNotContain("client_id_metadata_document_supported", asm);
        Assert.Equal("https://api.example.test/.well-known/oauth-protected-resource/mcp", service.ResourceMetadataUrl);
    }

    // ---------- registration ----------

    [Fact]
    public async Task Register_RejectsUnknownRedirectAndConfidentialClients()
    {
        await using var db = CreateDb();
        var service = OAuth(db);
        var bad = await Assert.ThrowsAsync<OAuthException>(() => service.RegisterAsync(Json($$"""{"redirect_uris":["https://evil.example/cb"]}"""), null, default));
        Assert.Equal("invalid_redirect_uri", bad.Error);
        // ขอ client_secret_basic ได้ "none" กลับไป (ไม่ออก secret)
        var confidential = JsonSerializer.Serialize(await service.RegisterAsync(Json($$"""{"redirect_uris":["{{Callback}}"],"token_endpoint_auth_method":"client_secret_basic"}"""), null, default));
        Assert.Contains("\"token_endpoint_auth_method\":\"none\"", confidential);
        Assert.DoesNotContain("client_secret", confidential);
        await Assert.ThrowsAsync<OAuthException>(() => service.RegisterAsync(Json("""{"redirect_uris":[]}"""), null, default));

        var ok = JsonSerializer.Serialize(await service.RegisterAsync(Json($$"""{"client_name":"Claude","redirect_uris":["{{Callback}}"],"grant_types":["authorization_code","refresh_token"],"response_types":["code"],"token_endpoint_auth_method":"none"}"""), "160.79.104.10", default));
        Assert.Contains("\"client_id\":\"qac_", ok);
        Assert.Equal("Claude", (await db.OAuthClients.SingleAsync(x => x.ClientName == "Claude")).ClientName);
    }

    // ---------- full flow ----------

    [Fact]
    public async Task Flow_AuthorizeConsentTokenUseRefresh()
    {
        await using var db = CreateDb();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var (user, principal) = await SeedAsync(db, mcp: true);
        var tokens = Tokens(db, cache);
        var service = OAuth(db, tokens);
        var clientId = await RegisterAsync(service);
        var (verifier, challenge) = NewPkce();

        var start = await service.AuthorizeAsync(Query(clientId, challenge, state: "st-1"), default);
        Assert.Null(start.ErrorPage);
        Assert.StartsWith("https://web.example.test/?oauth=req_", start.RedirectUrl);
        var requestId = Uri.UnescapeDataString(start.RedirectUrl!.Split("oauth=")[1]);

        var consent = (await service.GetConsentAsync(requestId, principal, default))!;
        Assert.Equal(("Claude", "claude.ai", true, true), (consent.ClientName, consent.RedirectHost, consent.McpEnabled, consent.CanApprove));

        var decision = await service.DecideAsync(requestId, principal, user.UserId, approve: true, null, default);
        Assert.StartsWith(Callback + "?code=", decision.RedirectUrl);
        Assert.Contains("state=st-1", decision.RedirectUrl);
        Assert.Contains("iss=https%3A%2F%2Fapi.example.test", decision.RedirectUrl);
        var code = Uri.UnescapeDataString(decision.RedirectUrl.Split("code=")[1].Split('&')[0]);
        Assert.Null(await service.GetConsentAsync(requestId, principal, default)); // ใช้ซ้ำไม่ได้

        var issued = await service.TokenAsync(Form(("grant_type", "authorization_code"), ("client_id", clientId), ("code", code), ("redirect_uri", Callback), ("code_verifier", verifier), ("resource", Resource)), null, default);
        Assert.StartsWith("qao_", issued.AccessToken);
        Assert.StartsWith("qrt_", issued.RefreshToken);
        Assert.Equal(("Bearer", 3600, "knowledge:read"), (issued.TokenType, issued.ExpiresIn, issued.Scope));

        // access token ใช้กับ /mcp ได้ (สิทธิ์ของผู้ใช้คนนี้)
        var authenticated = await tokens.AuthenticateAsync(issued.AccessToken, null, "PersonalAccessToken", default);
        Assert.Equal(user.UserId.ToString(), authenticated!.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(authenticated!.HasClaim("permission", "KNOWLEDGE.MCP"));

        // code ใช้ซ้ำไม่ได้
        var reuse = await Assert.ThrowsAsync<OAuthException>(() => service.TokenAsync(Form(("grant_type", "authorization_code"), ("client_id", clientId), ("code", code), ("redirect_uri", Callback), ("code_verifier", verifier)), null, default));
        Assert.Equal("invalid_grant", reuse.Error);

        // refresh = rotation: refresh/access เดิมใช้ไม่ได้
        var refreshed = await service.TokenAsync(Form(("grant_type", "refresh_token"), ("client_id", clientId), ("refresh_token", issued.RefreshToken)), null, default);
        Assert.NotEqual(issued.AccessToken, refreshed.AccessToken);
        Assert.Null(await tokens.AuthenticateAsync(issued.AccessToken, null, "PersonalAccessToken", default));
        Assert.NotNull(await tokens.AuthenticateAsync(refreshed.AccessToken, null, "PersonalAccessToken", default));
        // refresh token เดิมใช้ซ้ำ = ยกเลิกการเชื่อมต่อทั้งหมด — ทดสอบแยกใน Refresh_ReusedOldRefreshTokenRevokesConnection

        // หน้า "เชื่อมต่อ Claude" เห็นเป็น 1 การเชื่อมต่อแบบ OAuth
        var listed = Assert.Single(await tokens.ListAsync(user.UserId, default));
        Assert.Equal(("OAuth", "Claude (OAuth)", true), (listed.Kind, listed.Name, listed.IsActive));

        // ยกเลิกจากหน้าเว็บ = refresh ใช้ไม่ได้
        await tokens.RevokeAsync(user.UserId, listed.PersonalAccessTokenId, false, null, default);
        await Assert.ThrowsAsync<OAuthException>(() => service.TokenAsync(Form(("grant_type", "refresh_token"), ("client_id", clientId), ("refresh_token", refreshed.RefreshToken)), null, default));
        Assert.Null(await tokens.AuthenticateAsync(refreshed.AccessToken, null, "PersonalAccessToken", default));
    }

    [Fact]
    public async Task Token_WrongVerifierConsumesCode()
    {
        await using var db = CreateDb();
        var (user, principal) = await SeedAsync(db, mcp: true);
        var service = OAuth(db);
        var clientId = await RegisterAsync(service);
        var (_, challenge) = NewPkce();
        var code = await ApprovedCodeAsync(service, clientId, challenge, principal, user.UserId);
        var (otherVerifier, _) = NewPkce();

        Assert.Equal("invalid_grant", (await Assert.ThrowsAsync<OAuthException>(() => service.TokenAsync(Form(("grant_type", "authorization_code"), ("client_id", clientId), ("code", code), ("redirect_uri", Callback), ("code_verifier", otherVerifier)), null, default))).Error);
        Assert.Equal("Exchanged", (await db.OAuthAuthorizations.SingleAsync()).Status);
    }

    [Fact]
    public async Task Token_WrongClientOrRedirectOrResourceRejected()
    {
        await using var db = CreateDb();
        var (user, principal) = await SeedAsync(db, mcp: true);
        var service = OAuth(db);
        var clientId = await RegisterAsync(service);
        var otherClient = await RegisterAsync(service);
        var (verifier, challenge) = NewPkce();
        var code = await ApprovedCodeAsync(service, clientId, challenge, principal, user.UserId);

        Assert.Equal("invalid_grant", (await Assert.ThrowsAsync<OAuthException>(() => service.TokenAsync(Form(("grant_type", "authorization_code"), ("client_id", otherClient), ("code", code), ("redirect_uri", Callback), ("code_verifier", verifier)), null, default))).Error);
        Assert.Equal("invalid_client", (await Assert.ThrowsAsync<OAuthException>(() => service.TokenAsync(Form(("grant_type", "authorization_code"), ("client_id", "qac_unknown"), ("code", code)), null, default))).Error);
        Assert.Equal("unsupported_grant_type", (await Assert.ThrowsAsync<OAuthException>(() => service.TokenAsync(Form(("grant_type", "password"), ("client_id", clientId)), null, default))).Error);
    }

    [Fact]
    public async Task Token_WrongResourceConsumesCode()
    {
        await using var db = CreateDb();
        var (user, principal) = await SeedAsync(db, mcp: true);
        var service = OAuth(db);
        var clientId = await RegisterAsync(service);
        var (verifier, challenge) = NewPkce();
        var code = await ApprovedCodeAsync(service, clientId, challenge, principal, user.UserId);
        IFormCollection Exchange(string resource) => Form(("grant_type", "authorization_code"), ("client_id", clientId), ("code", code), ("redirect_uri", Callback), ("code_verifier", verifier), ("resource", resource));

        Assert.Equal("invalid_target", (await Assert.ThrowsAsync<OAuthException>(() => service.TokenAsync(Exchange("https://evil.example/mcp"), null, default))).Error);
        Assert.Equal("Exchanged", (await db.OAuthAuthorizations.AsNoTracking().SingleAsync()).Status);
        // ลองใหม่ด้วย resource ที่ถูกก็ใช้ code เดิมไม่ได้แล้ว
        Assert.Equal("invalid_grant", (await Assert.ThrowsAsync<OAuthException>(() => service.TokenAsync(Exchange(Resource), null, default))).Error);
    }

    [Fact]
    public async Task Token_ConcurrentExchangeOfSameCodeOnlyOneSucceeds()
    {
        var dbName = Guid.NewGuid().ToString();
        QaDbContext Open() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(dbName).Options);
        string clientId, code;
        await using (var setup = Open())
        {
            var (user, principal) = await SeedAsync(setup, mcp: true);
            var service = OAuth(setup);
            clientId = await RegisterAsync(service);
            var (_, challenge) = NewPkce();
            code = await ApprovedCodeAsync(service, clientId, challenge, principal, user.UserId);
        }
        // จำลองสองคำขอที่อ่านแถวเดียวกันก่อนอีกฝ่ายบันทึก: ทั้งคู่ผ่าน TryExchange ในหน่วยความจำ แต่บันทึกได้แค่คำขอแรก
        await using var first = Open();
        await using var second = Open();
        var a = await first.OAuthAuthorizations.SingleAsync();
        var b = await second.OAuthAuthorizations.SingleAsync();
        Assert.True(a.TryExchange(code, clientId, Callback, DateTime.UtcNow));
        Assert.True(b.TryExchange(code, clientId, Callback, DateTime.UtcNow));
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Refresh_ReusedOldRefreshTokenRevokesConnection()
    {
        await using var db = CreateDb();
        var (user, principal) = await SeedAsync(db, mcp: true);
        var tokens = Tokens(db);
        var service = OAuth(db, tokens);
        var clientId = await RegisterAsync(service);
        var (verifier, challenge) = NewPkce();
        var code = await ApprovedCodeAsync(service, clientId, challenge, principal, user.UserId);
        var issued = await service.TokenAsync(Form(("grant_type", "authorization_code"), ("client_id", clientId), ("code", code), ("redirect_uri", Callback), ("code_verifier", verifier)), null, default);
        IFormCollection Refresh(string token) => Form(("grant_type", "refresh_token"), ("client_id", clientId), ("refresh_token", token));
        var refreshed = await service.TokenAsync(Refresh(issued.RefreshToken), null, default);

        // refresh token เดิม (ถูก rotate ไปแล้ว) ถูกนำมาใช้อีก → ยกเลิกการเชื่อมต่อ token ชุดใหม่ก็ใช้ไม่ได้
        Assert.Equal("invalid_grant", (await Assert.ThrowsAsync<OAuthException>(() => service.TokenAsync(Refresh(issued.RefreshToken), null, default))).Error);
        Assert.NotNull((await db.PersonalAccessTokens.AsNoTracking().SingleAsync()).RevokedAt);
        await Assert.ThrowsAsync<OAuthException>(() => service.TokenAsync(Refresh(refreshed.RefreshToken), null, default));
        Assert.Null(await tokens.AuthenticateAsync(refreshed.AccessToken, null, "PersonalAccessToken", default));
    }

    [Fact]
    public async Task Refresh_ConcurrentRotationOfSameTokenOnlyOneSucceeds()
    {
        var dbName = Guid.NewGuid().ToString();
        QaDbContext Open() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(dbName).Options);
        await using (var setup = Open())
        {
            var (user, principal) = await SeedAsync(setup, mcp: true);
            var service = OAuth(setup);
            var clientId = await RegisterAsync(service);
            var (verifier, challenge) = NewPkce();
            var code = await ApprovedCodeAsync(service, clientId, challenge, principal, user.UserId);
            await service.TokenAsync(Form(("grant_type", "authorization_code"), ("client_id", clientId), ("code", code), ("redirect_uri", Callback), ("code_verifier", verifier)), null, default);
        }
        await using var first = Open();
        await using var second = Open();
        var a = await first.PersonalAccessTokens.SingleAsync();
        var b = await second.PersonalAccessTokens.SingleAsync();
        a.Rotate(DateTime.UtcNow);
        b.Rotate(DateTime.UtcNow);
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Authorize_ErrorsNeverRedirectToUntrustedUri()
    {
        await using var db = CreateDb();
        await SeedAsync(db, mcp: true);
        var service = OAuth(db);
        var clientId = await RegisterAsync(service);
        var (_, challenge) = NewPkce();

        Assert.NotNull((await service.AuthorizeAsync(Query("qac_unknown", challenge), default)).ErrorPage);
        Assert.NotNull((await service.AuthorizeAsync(Query(clientId, challenge, redirect: "http://localhost:1/other"), default)).ErrorPage);

        var noPkce = await service.AuthorizeAsync(Query(clientId, null), default);
        Assert.StartsWith(Callback + "?error=invalid_request", noPkce.RedirectUrl);
        var wrongResource = await service.AuthorizeAsync(Query(clientId, challenge, resource: "https://other.example/mcp"), default);
        Assert.StartsWith(Callback + "?error=invalid_target", wrongResource.RedirectUrl);
        var badScope = await service.AuthorizeAsync(Query(clientId, challenge, scope: "admin"), default);
        Assert.StartsWith(Callback + "?error=invalid_scope", badScope.RedirectUrl);
        // resource ไม่ส่งมา + offline_access = ยอมรับ
        Assert.Contains("?oauth=", (await service.AuthorizeAsync(Query(clientId, challenge, resource: null, scope: "knowledge:read offline_access"), default)).RedirectUrl);
    }

    [Fact]
    public async Task Authorize_DisabledMcpAndMissingPermission()
    {
        await using var db = CreateDb();
        var (user, principal) = await SeedAsync(db, mcp: false);
        var tokens = Tokens(db);
        var service = OAuth(db, tokens);
        var clientId = await RegisterAsync(service);
        var (_, challenge) = NewPkce();
        Assert.StartsWith(Callback + "?error=access_denied", (await service.AuthorizeAsync(Query(clientId, challenge), default)).RedirectUrl);

        var config = await db.AiConfigurations.SingleAsync();
        config.UpdateKnowledge("None", null, null, "", null, false, true);
        await db.SaveChangesAsync();
        tokens.ForgetMcpEnabled(); // Setting Center ล้าง cache สวิตช์ MCP หลังบันทึก
        var start = await service.AuthorizeAsync(Query(clientId, challenge, state: "s"), default);
        var requestId = Uri.UnescapeDataString(start.RedirectUrl!.Split("oauth=")[1]);
        var noPermission = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()), new Claim("permission", "KNOWLEDGE.VIEW")], "jwt"));
        Assert.False((await service.GetConsentAsync(requestId, noPermission, default))!.CanApprove);
        Assert.Equal(403, (await Assert.ThrowsAsync<OAuthException>(() => service.DecideAsync(requestId, noPermission, user.UserId, true, null, default))).StatusCode);

        var denied = await service.DecideAsync(requestId, noPermission, user.UserId, approve: false, null, default);
        Assert.StartsWith(Callback + "?error=access_denied", denied.RedirectUrl);
        Assert.Contains("state=s", denied.RedirectUrl);
        Assert.Equal("Denied", (await db.OAuthAuthorizations.SingleAsync()).Status);
        Assert.NotNull(principal);
    }

    [Fact]
    public async Task Reconnect_RevokesPreviousGrantForSameClient()
    {
        await using var db = CreateDb();
        var (user, principal) = await SeedAsync(db, mcp: true);
        var service = OAuth(db);
        var clientId = await RegisterAsync(service);
        for (var i = 0; i < 2; i++)
        {
            var (verifier, challenge) = NewPkce();
            var code = await ApprovedCodeAsync(service, clientId, challenge, principal, user.UserId);
            await service.TokenAsync(Form(("grant_type", "authorization_code"), ("client_id", clientId), ("code", code), ("redirect_uri", Callback), ("code_verifier", verifier)), null, default);
        }
        var grants = await db.PersonalAccessTokens.Where(x => x.Kind == "OAuth").ToListAsync();
        Assert.Equal(2, grants.Count);
        Assert.Single(grants, x => x.RevokedAt is null);
    }

    [Fact]
    public async Task AccessToken_RejectedForOtherAudience()
    {
        await using var db = CreateDb();
        var (user, _) = await SeedAsync(db, mcp: true);
        var (grant, access, _) = PersonalAccessToken.CreateOAuth(user.UserId, "qac_x", "Claude", "https://other.example/mcp", DateTime.UtcNow);
        db.PersonalAccessTokens.Add(grant);
        await db.SaveChangesAsync();
        Assert.Null(await Tokens(db).AuthenticateAsync(access, null, "PersonalAccessToken", default));
    }

    // ---------- helpers ----------

    private static async Task<string> ApprovedCodeAsync(OAuthService service, string clientId, string challenge, ClaimsPrincipal principal, Guid userId)
    {
        var start = await service.AuthorizeAsync(Query(clientId, challenge), default);
        var requestId = Uri.UnescapeDataString(start.RedirectUrl!.Split("oauth=")[1]);
        var decision = await service.DecideAsync(requestId, principal, userId, true, null, default);
        return Uri.UnescapeDataString(decision.RedirectUrl.Split("code=")[1].Split('&')[0]);
    }

    private static async Task<string> RegisterAsync(OAuthService service)
    {
        var registered = JsonSerializer.SerializeToElement(await service.RegisterAsync(Json($$"""{"client_name":"Claude","redirect_uris":["{{Callback}}","http://localhost:33418/callback"]}"""), null, default));
        return registered.GetProperty("client_id").GetString()!;
    }

    private static async Task<(User User, ClaimsPrincipal Principal)> SeedAsync(QaDbContext db, bool mcp)
    {
        var user = new User("qa1", "QA One", null, "hash");
        var role = new Role("QA_TESTER", "QA Tester");
        var view = new Permission("KNOWLEDGE.VIEW", "v", "KNOWLEDGE");
        var mcpPermission = new Permission("KNOWLEDGE.MCP", "m", "KNOWLEDGE");
        db.AddRange(user, role, view, mcpPermission, new RolePermission(role.RoleId, view.PermissionId), new RolePermission(role.RoleId, mcpPermission.PermissionId), new UserRole(user.UserId, role.RoleId));
        var config = new AiConfiguration("Local", "qwen3", "http://localhost:11434/v1", "", null, true);
        config.UpdateKnowledge("None", null, null, "", null, false, mcp);
        db.Add(config);
        await db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()), new Claim("permission", "KNOWLEDGE.VIEW"), new Claim("permission", "KNOWLEDGE.MCP")], "jwt"));
        return (user, principal);
    }

    private static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = OAuthSecrets.Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, OAuthSecrets.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static IQueryCollection Query(string clientId, string? challenge, string? state = null, string redirect = Callback, string? resource = Resource, string? scope = "knowledge:read")
    {
        var values = new Dictionary<string, StringValues> { ["response_type"] = "code", ["client_id"] = clientId, ["redirect_uri"] = redirect };
        if (challenge is not null) { values["code_challenge"] = challenge; values["code_challenge_method"] = "S256"; }
        if (state is not null) values["state"] = state;
        if (resource is not null) values["resource"] = resource;
        if (scope is not null) values["scope"] = scope;
        return new QueryCollection(values);
    }

    private static IFormCollection Form(params (string Key, string Value)[] values) => new FormCollection(values.ToDictionary(x => x.Key, x => new StringValues(x.Value)));
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Mcp:PublicBaseUrl"] = "https://api.example.test/",
        ["PublicWebBaseUrl"] = "https://web.example.test",
    }).Build();

    private static PersonalAccessTokenService Tokens(QaDbContext db, IMemoryCache? cache = null) => new(db, new IdentityRepository(db), cache ?? new MemoryCache(new MemoryCacheOptions()), Config());
    private static OAuthService OAuth(QaDbContext db, PersonalAccessTokenService? tokens = null) => new(db, Config(), tokens ?? Tokens(db));
    private static QaDbContext CreateDb() => new(new DbContextOptionsBuilder<QaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
