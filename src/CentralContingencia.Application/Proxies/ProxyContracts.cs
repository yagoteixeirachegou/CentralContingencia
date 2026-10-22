using CentralContingencia.Domain.Audit;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Application.Proxies;

public sealed class CreateProxyRequest
{
    public string Host { get; init; } = "";
    public int Port { get; init; }
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string Region { get; init; } = "";
    public string Status { get; init; } = "Disabled";
    public DateTimeOffset? ExpiresAt { get; init; }
    public override string ToString() => "[create proxy request]";
}
public sealed class UpdateProxyRequest
{
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Region { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public override string ToString() => "[update proxy request]";
}
public sealed record ProxyResponse(Guid Id, string Host, int Port, string UsernameMasked,
    string Status, string Region, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool Available);
// Único DTO com texto claro; só sai do endpoint de revelação (Admin, auditado). ToString nunca imprime os valores.
public sealed record ProxyCredentials(Guid Id, string Host, int Port, string Username, string Password)
{
    public override string ToString() => "[proxy credentials]";
}
public sealed record ProxyPage(IReadOnlyList<ProxyResponse> Items, int Page, int PageSize, int Total);
public sealed record ProxyActor(Guid Id, IReadOnlyList<string> Roles);
public sealed record ProxyQuery(int Page = 1, int PageSize = 20, string? Host = null, int? Port = null,
    string? Region = null, string? Status = null, DateTimeOffset? ExpiresBefore = null,
    string Sort = "createdAt", string Direction = "desc");
public sealed record ValidatedProxyQuery(int Page, int PageSize, ProxyHost? Host, int? Port,
    string? Region, ProxyStatus? Status, DateTimeOffset? ExpiresBefore, string Sort, bool Descending, bool AvailableOnly);
public enum ProxyFailure { Validation, Forbidden, NotFound, Conflict, EncryptionUnavailable }
public sealed class ProxyOperationException(ProxyFailure failure) : Exception("Operação de proxy recusada.")
{
    public ProxyFailure Failure { get; } = failure;
}
public interface IProxyStore
{
    Task<ProxyPage> QueryAsync(ValidatedProxyQuery query, DateTimeOffset now, CancellationToken token);
    Task<Proxy?> FindAsync(Guid id, CancellationToken token);
    Task<bool> HasActiveAllocationAsync(Guid id, DateTimeOffset now, CancellationToken token);
    // Grava proxy + auditoria atomicamente; false representa conflito concorrente.
    Task<bool> SaveAsync(Proxy proxy, ProxyAuditEvent audit, bool isNew, CancellationToken token);
    Task AddAuditAsync(ProxyAuditEvent audit, CancellationToken token);
}
public interface IProxyManagementService
{
    Task<ProxyPage> ListAsync(ProxyActor actor, ProxyQuery query, bool availableOnly, CancellationToken token);
    Task<ProxyResponse> GetAsync(ProxyActor actor, Guid id, CancellationToken token);
    Task<ProxyResponse> CreateAsync(ProxyActor actor, CreateProxyRequest request, string correlationId, CancellationToken token);
    Task<ProxyResponse> UpdateAsync(ProxyActor actor, Guid id, UpdateProxyRequest request, string correlationId, CancellationToken token);
    Task<ProxyCredentials> RevealCredentialsAsync(ProxyActor actor, Guid id, string correlationId, CancellationToken token);
}
