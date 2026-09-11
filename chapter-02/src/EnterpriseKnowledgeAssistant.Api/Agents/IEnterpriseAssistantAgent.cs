using EnterpriseKnowledgeAssistant.Api.Contracts;

namespace EnterpriseKnowledgeAssistant.Api.Agents;

public interface IEnterpriseAssistantAgent
{
    Task<AssistantQueryResponse> QueryAsync(string query, CancellationToken cancellationToken = default);
}
