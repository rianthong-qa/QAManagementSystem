using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProMaxx2.QA.Application.Identity;
using ProMaxx2.QA.Domain.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;

namespace ProMaxx2.QA.Infrastructure.Identity;

public sealed class IdentityRepository(QaDbContext db) : IIdentityRepository
{
    public Task<User?> FindByUsernameAsync(string username, CancellationToken ct) => db.Users.SingleOrDefaultAsync(x => x.Username == username, ct);
    public async Task<AuthenticatedUser?> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (user is null) return null;
        var roles = await db.UserRoles.Where(x=>x.UserId==userId).Select(x=>x.Role.RoleCode).ToArrayAsync(ct);
        var permissions = await db.UserRoles.Where(x=>x.UserId==userId).SelectMany(x=>x.Role.RolePermissions).Select(x=>x.Permission.PermissionCode).Distinct().ToArrayAsync(ct);
        return new(user.UserId, user.Username, user.DisplayName, user.Email, roles, permissions);
    }
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class PasswordService : IPasswordService
{
    private const int Iterations = 210_000;
    public string Hash(string password) { var salt=RandomNumberGenerator.GetBytes(16); var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,HashAlgorithmName.SHA256,32); return $"PBKDF2-SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}"; }
    public bool Verify(string password, string passwordHash)
    {
        var parts=passwordHash.Split('$'); if(parts.Length!=4 || parts[0]!="PBKDF2-SHA256" || !int.TryParse(parts[1],out var iterations)) return false;
        try { var salt=Convert.FromBase64String(parts[2]); var expected=Convert.FromBase64String(parts[3]); var actual=Rfc2898DeriveBytes.Pbkdf2(password,salt,iterations,HashAlgorithmName.SHA256,expected.Length); return CryptographicOperations.FixedTimeEquals(actual,expected); } catch(FormatException) { return false; }
    }
}

public sealed class JwtOptions { public const string Section="Jwt"; public string Issuer {get;init;}="ProMaxx2.QA"; public string Audience {get;init;}="ProMaxx2.QA.Web"; public string Key {get;init;}=string.Empty; public int ExpiresMinutes {get;init;}=60; }
public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    public LoginResponse Create(AuthenticatedUser user)
    {
        var value=options.Value; var claims=new List<Claim>{new(JwtRegisteredClaimNames.Sub,user.UserId.ToString()),new(JwtRegisteredClaimNames.UniqueName,user.Username),new("display_name",user.DisplayName)};
        claims.AddRange(user.Roles.Select(x=>new Claim(ClaimTypes.Role,x))); claims.AddRange(user.Permissions.Select(x=>new Claim("permission",x)));
        var expires=DateTime.UtcNow.AddMinutes(value.ExpiresMinutes); var token=new JwtSecurityToken(value.Issuer,value.Audience,claims,expires:expires,signingCredentials:new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(value.Key)),SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), value.ExpiresMinutes*60, user);
    }
}
