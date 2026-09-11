using Azure;
using Azure.AI.OpenAI;
using EnterpriseArchitectureAssessment.Api.Agents;
using EnterpriseArchitectureAssessment.Api.Configuration;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Specialized;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace EnterpriseArchitectureAssessment.Api.Orchestration;

public sealed class MagenticAssessmentOrchestrator(IEnumerable<AssessmentAgentFactory> factories,
                                                    IConfiguration configuration,
                                                    IOptions<MagenticAssessmentOptions> options)
    : IMagenticAssessmentOrchestrator
{
    public const string InstructionVersion = "chapter-04-v2";
    public async Task<NativeMagenticResult> RunAsync(string objective, CancellationToken cancellationToken)
    {
        var chat = new AzureOpenAIClient(
                new Uri(configuration["AzureOpenAI:Endpoint"]!),
                new AzureKeyCredential(configuration["AzureOpenAI:ApiKey"]!))
            .GetChatClient(configuration["AzureOpenAI:DeploymentName"]!);

        AIAgent managerAgent = chat.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "ArchitectureAssessmentManager",
            Description = "Plans and replans architecture assessments.",
            ChatOptions = new() { Instructions = ManagerInstructions }
        });
        var participants = factories.Select(x => x.Create()).ToArray();

        // MagenticWorkflowBuilder is the native C# Magentic API in the installed Workflows package.
        Workflow workflow = new MagenticWorkflowBuilder(managerAgent)
            .AddParticipants(participants)
            .WithName("Enterprise Architecture Magentic Assessment")
            .WithDescription("Plans, delegates, evaluates, and replans an enterprise architecture assessment.")
            .RequirePlanSignoff(false)
            .WithMaxRounds(options.Value.MaxTurns)
            .WithMaxStalls(options.Value.MaxStalls)
            .WithMaxResets(options.Value.MaxResets)
            .Build();
        var execution = await InProcessExecution.RunAsync(
            workflow,
            objective,
            cancellationToken: cancellationToken);

        AgentInvocationContext.Reset();

        // MAF 1.15 exposes the workflow event envelope, but not Magentic's private ledger as a
        // public typed plan model. Preserve the concrete runtime event names and safely extract
        // message text from ChatMessage/TextContent payloads. Calling Data.ToString() here is
        // incorrect: collection/message payloads stringify as type names (and occasionally ".").
        var nativeEvents = execution.NewEvents
            .Select(x => new NativeMagenticEvent(
                x.GetType().Name,
                NativeEventText.ReadExecutor(x),
                NativeEventText.Read(x.GetType().GetProperty("Data")?.GetValue(x))))
            .ToArray();
        var finalMessage = nativeEvents
            .Where(x => x.RuntimeType == nameof(WorkflowOutputEvent))
            .Select(x => x.Text)
            .LastOrDefault(NativeEventText.IsMeaningful);
        return new NativeMagenticResult(finalMessage, nativeEvents);
    }

    private const string ManagerInstructions = """
        You are the Magentic assessment manager. Form a plan from the objective, delegate only useful specialists, evaluate progress,
        revise the plan when evidence changes scope, reconcile conflicts, and finish early when sufficient. Never use a fixed agent order.
        Emit intentional audit summaries as separate lines using PLAN, DELEGATION, PROGRESS, REVISION, CONFLICT, QUESTION, and COMPLETE.
        A PLAN or REVISION contains semicolon-delimited tasks. Do not reveal private chain-of-thought. The final recommendation must cite evidence,
        trade-offs, open questions, and confidence. Do not assume full microservices are desirable; let specialist evidence determine direction.
        """;
}

internal static class NativeEventText
{
    public static bool IsMeaningful(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim() != "." && !value.StartsWith("System.", StringComparison.Ordinal);

    public static string? ReadExecutor(object value)
    {
        foreach (var name in new[] { "ExecutorId", "ExecutorName", "AgentName" })
        {
            var text = value.GetType().GetProperty(name)?.GetValue(value)?.ToString();
            if (IsMeaningful(text)) return text;
        }
        return null;
    }

    public static string? Read(object? value)
    {
        var parts = new List<string>();
        Collect(value, parts, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        return parts.Count == 0 ? null : string.Join("\n", parts.Distinct());
    }

    private static void Collect(object? value, List<string> parts, HashSet<object> seen, int depth)
    {
        if (value is null || depth > 8) return;
        if (value is string text)
        {
            if (IsMeaningful(text)) parts.Add(text.Trim());
            return;
        }
        if (!value.GetType().IsValueType && !seen.Add(value)) return;
        if (value is System.Collections.IEnumerable sequence)
        {
            foreach (var item in sequence) Collect(item, parts, seen, depth + 1);
            return;
        }
        foreach (var name in new[] { "Text", "Content", "Contents", "Message", "Messages", "Result", "Value" })
        {
            var property = value.GetType().GetProperty(name);
            if (property is not null && property.GetIndexParameters().Length == 0)
                Collect(property.GetValue(value), parts, seen, depth + 1);
        }
    }
}
