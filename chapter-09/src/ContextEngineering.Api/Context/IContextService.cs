using ContextEngineering.Api.Models;

namespace ContextEngineering.Api.Context;

public interface IContextService
{
    Task<ContextBundle> GetContextAsync(
        ContextRequest request,
        CancellationToken cancellationToken);
}
