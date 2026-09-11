namespace EnterpriseKnowledgeAssistant.Api.Agents;

public sealed record ModelTool(string Name, string Description, Func<string, CancellationToken, Task<string>> InvokeAsync);

public interface IEnterpriseModelRunner
{
    Task<string> RunAsync(string query, IReadOnlyList<ModelTool> tools, CancellationToken cancellationToken);
}
