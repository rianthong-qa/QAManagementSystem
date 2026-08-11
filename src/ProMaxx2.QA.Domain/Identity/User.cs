namespace ProMaxx2.QA.Domain.Identity;

public sealed class User
{
    private User() { }
    public User(string username, string displayName, string? email, string passwordHash)
    {
        UserId = Guid.NewGuid();
        Username = username.Trim();
        DisplayName = displayName.Trim();
        Email = email?.Trim();
        PasswordHash = passwordHash;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public ICollection<UserRole> UserRoles { get; private set; } = [];

    public void RecordLogin() => LastLoginAt = DateTime.UtcNow;
    public void Deactivate() { IsActive = false; UpdatedAt = DateTime.UtcNow; }
    public void Activate() { IsActive = true; UpdatedAt = DateTime.UtcNow; }
    public void Update(string displayName,string? email) { if(string.IsNullOrWhiteSpace(displayName))throw new ArgumentException("Display name is required.");DisplayName=displayName.Trim();Email=email?.Trim();UpdatedAt=DateTime.UtcNow; }
    public void ResetPassword(string passwordHash) { PasswordHash=passwordHash;UpdatedAt=DateTime.UtcNow; }
}

public sealed class Role
{
    private Role() { }
    public Role(string code, string name, string? description = null) { RoleId = Guid.NewGuid(); RoleCode = code; RoleName = name; Description = description; IsActive = true; }
    public Guid RoleId { get; private set; }
    public string RoleCode { get; private set; } = string.Empty;
    public string RoleName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public ICollection<UserRole> UserRoles { get; private set; } = [];
    public ICollection<RolePermission> RolePermissions { get; private set; } = [];
    public void Update(string name,string? description){if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Role name is required.");RoleName=name.Trim();Description=description?.Trim();}
}

public sealed class Permission
{
    private Permission() { }
    public Permission(string code, string name, string? moduleArea) { PermissionId = Guid.NewGuid(); PermissionCode = code; PermissionName = name; ModuleArea = moduleArea; }
    public Guid PermissionId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;
    public string PermissionName { get; private set; } = string.Empty;
    public string? ModuleArea { get; private set; }
    public ICollection<RolePermission> RolePermissions { get; private set; } = [];
}

public sealed class UserRole
{
    private UserRole() { }
    public UserRole(Guid userId, Guid roleId) { UserId = userId; RoleId = roleId; CreatedAt = DateTime.UtcNow; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public User User { get; private set; } = null!;
    public Role Role { get; private set; } = null!;
}

public sealed class RolePermission
{
    private RolePermission() { }
    public RolePermission(Guid roleId, Guid permissionId) { RoleId = roleId; PermissionId = permissionId; }
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }
    public Role Role { get; private set; } = null!;
    public Permission Permission { get; private set; } = null!;
}
