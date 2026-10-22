using CentralContingencia.Application.Identity;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CentralContingencia.Infrastructure.Identity;

public sealed class AuthenticationService(
    UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    CentralContingenciaDbContext db) : IAuthenticationService
{
    private async Task AuditAsync(AuthenticationAction action, Guid? actor, Guid? resource, string correlation)
    {
        db.AuthenticationAuditEvents.Add(new(action, actor, resource, correlation));
        await db.SaveChangesAsync();
    }
    public async Task<bool> LoginAsync(LoginRequest request, string correlationId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var normalizedEmail = users.NormalizeEmail(request.Email.Trim());
        // Serializa tentativas da mesma conta, inclusive de IPs distintos.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({normalizedEmail}, 0))");
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            // Reduz diferença de tempo sem armazenar e-mail ou senha de uma tentativa.
            var dummy = new ApplicationUser();
            _ = users.PasswordHasher.HashPassword(dummy, request.Password);
            await AuditAsync(AuthenticationAction.LoginFailed, null, null, correlationId);
            await transaction.CommitAsync();
            return false;
        }
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        await AuditAsync(result.Succeeded ? AuthenticationAction.LoginSucceeded :
            result.IsLockedOut ? AuthenticationAction.LockedOut : AuthenticationAction.LoginFailed,
            result.Succeeded ? user.Id : null, user.Id, correlationId);
        await transaction.CommitAsync();
        if (!result.Succeeded) return false;
        await signIn.SignInAsync(user, isPersistent: false);
        return true;
    }
    public async Task<AuthenticatedUser?> GetUserAsync(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        return user is null ? null : new(user.Id, user.Email!, (await users.GetRolesAsync(user)).ToArray());
    }
    public async Task LogoutAsync(Guid id, string correlationId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByIdAsync(id.ToString());
        if (user is not null)
        {
            var result = await users.UpdateSecurityStampAsync(user);
            if (!result.Succeeded) throw new InvalidOperationException("Não foi possível invalidar a sessão.");
        }
        await AuditAsync(AuthenticationAction.Logout, id, id, correlationId);
        await transaction.CommitAsync();
        await signIn.SignOutAsync();
    }
    public async Task<OperationResult> ChangePasswordAsync(Guid id, ChangePasswordRequest request, string correlationId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return new(false, "Não foi possível alterar a senha.");
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) return new(false, "Senha atual inválida ou nova senha fora dos requisitos.");
        await AuditAsync(AuthenticationAction.PasswordChanged, id, id, correlationId);
        await transaction.CommitAsync();
        await signIn.SignOutAsync();
        return new(true);
    }
    public async Task<OperationResult> CreateUserAsync(Guid actorId, CreateUserRequest request, string correlationId)
    {
        var actor = await users.FindByIdAsync(actorId.ToString());
        if (actor is null || !await users.IsInRoleAsync(actor, AccessRoles.Admin))
            return new(false, "Acesso negado.");
        if (!AccessRoles.All.Contains(request.Role)) return new(false, "Papel inválido.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = request.Email.Trim(), Email = request.Email.Trim() };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded) return new(false, "Não foi possível criar o usuário. Verifique e-mail e requisitos de senha.");
        var assigned = await users.AddToRoleAsync(user, request.Role);
        if (!assigned.Succeeded) throw new InvalidOperationException("Não foi possível atribuir o papel.");
        await AuditAsync(AuthenticationAction.UserCreated, actorId, user.Id, correlationId);
        await transaction.CommitAsync();
        return new(true);
    }
}
