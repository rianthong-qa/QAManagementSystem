using ProMaxx2.QA.Domain.Identity;

namespace ProMaxx2.QA.Application.Identity;

public sealed record LoginRequest(string Username, string Password);
public sealed record AuthenticatedUser(Guid UserId, string Username, string DisplayName, string? Email, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions);
public sealed record LoginResponse(string AccessToken, int ExpiresIn, AuthenticatedUser User);

public interface IIdentityRepository
{
    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken);
    Task<AuthenticatedUser?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface ITokenService
{
    LoginResponse Create(AuthenticatedUser user);
}

public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException() : base("ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง") { }
}

public sealed class AuthenticationService(IIdentityRepository repository, IPasswordService passwords, ITokenService tokens)
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password)) throw new InvalidCredentialsException();
        var user = await repository.FindByUsernameAsync(request.Username.Trim(), cancellationToken);
        if (user is null || !user.IsActive || !passwords.Verify(request.Password, user.PasswordHash)) throw new InvalidCredentialsException();
        user.RecordLogin();
        await repository.SaveChangesAsync(cancellationToken);
        var profile = await repository.GetProfileAsync(user.UserId, cancellationToken) ?? throw new InvalidCredentialsException();
        return tokens.Create(profile);
    }
}
