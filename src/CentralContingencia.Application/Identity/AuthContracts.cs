using System.ComponentModel.DataAnnotations;
namespace CentralContingencia.Application.Identity;

public static class AccessRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Operator = "Operator";
    public static readonly IReadOnlyList<string> All = Array.AsReadOnly(new[] { Admin, Manager, Operator });
}
public static class AccessPolicies
{
    public const string Administration = "Administration";
    public const string Management = "Management";
    public const string Operations = "Operations";
}
public sealed record LoginRequest(
    [property: Required, EmailAddress, StringLength(254)] string Email,
    [property: Required, StringLength(128)] string Password);
public sealed record ChangePasswordRequest(
    [property: Required, StringLength(128)] string CurrentPassword,
    [property: Required, StringLength(128, MinimumLength = 12)] string NewPassword);
public sealed record CreateUserRequest(
    [property: Required, EmailAddress, StringLength(254)] string Email,
    [property: Required, StringLength(128, MinimumLength = 12)] string Password,
    [property: Required] string Role);
public sealed record AuthenticatedUser(Guid Id, string Email, IReadOnlyList<string> Roles);
public sealed record OperationResult(bool Succeeded, string? Error = null);
public interface IAuthenticationService
{
    Task<bool> LoginAsync(LoginRequest request, string correlationId);
    Task<AuthenticatedUser?> GetUserAsync(Guid id);
    Task LogoutAsync(Guid id, string correlationId);
    Task<OperationResult> ChangePasswordAsync(Guid id, ChangePasswordRequest request, string correlationId);
    Task<OperationResult> CreateUserAsync(Guid actorId, CreateUserRequest request, string correlationId);
}
public interface IAdminProvisioner
{
    Task ProvisionAsync(string email, string password);
}
