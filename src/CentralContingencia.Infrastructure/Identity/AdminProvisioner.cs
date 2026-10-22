using System.ComponentModel.DataAnnotations;
using CentralContingencia.Application.Identity;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CentralContingencia.Infrastructure.Identity;

public sealed class AdminProvisioner(
    UserManager<ApplicationUser> users, RoleManager<IdentityRole<Guid>> roles,
    CentralContingenciaDbContext db) : IAdminProvisioner
{
    public async Task ProvisionAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email) ||
            string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 128)
            throw new InvalidOperationException("Informe e-mail válido e senha de 12 a 128 caracteres.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Serializa todas as execuções do bootstrap neste banco.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7061001)");
        if ((await users.GetUsersInRoleAsync(AccessRoles.Admin)).Count != 0)
            throw new InvalidOperationException("Um administrador já existe; provisionamento recusado.");
        foreach (var name in AccessRoles.All)
            if (!await roles.RoleExistsAsync(name))
            {
                var roleResult = await roles.CreateAsync(new IdentityRole<Guid>(name));
                if (!roleResult.Succeeded) throw new InvalidOperationException("Não foi possível criar os papéis.");
            }
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = email.Trim(), UserName = email.Trim() };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded) throw new InvalidOperationException("Provisionamento recusado. Verifique e-mail e requisitos de senha.");
        if (!(await users.AddToRoleAsync(user, AccessRoles.Admin)).Succeeded)
            throw new InvalidOperationException("Não foi possível atribuir Admin.");
        db.AuthenticationAuditEvents.Add(new(AuthenticationAction.AdminProvisioned, user.Id, user.Id, Guid.NewGuid().ToString("N")));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
