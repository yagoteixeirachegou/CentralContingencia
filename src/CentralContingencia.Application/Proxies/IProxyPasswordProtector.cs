using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Application.Proxies;

// Somente futuros casos de uso autorizados podem obter o texto claro; nunca DTOs de listagem.
public interface IProxyPasswordProtector
{
    EncryptedProxyPassword Encrypt(Guid proxyId, string password);
    string Decrypt(Guid proxyId, EncryptedProxyPassword password);
}
