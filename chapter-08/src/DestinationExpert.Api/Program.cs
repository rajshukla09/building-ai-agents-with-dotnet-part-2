using A2A;
using A2A.AspNetCore;
using Azure.AI.OpenAI;
using DestinationExpert.Api;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ClientModel;

var app = DestinationExpertApplication.Build(args);
await app.RunAsync();

namespace DestinationExpert.Api
{
    public static class DestinationExpertApplication
    {
        public static WebApplication Build(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var agent = CreateAgent(builder.Configuration);
            builder.Services.AddSingleton(agent);
            builder.Services.AddSingleton<InvocationCounter>();
            builder.AddA2AServer(agent);

            var app = builder.Build();
            var publicUrl = builder.Configuration["PublicUrl"] ?? "http://localhost:5181";
            var card = new AgentCard
            {
                Name = "DestinationExpert",
                Description = "A Microsoft Agent Framework agent specializing in attractions, activities, and local travel considerations.",
                Version = "1.0.0",
                SupportedInterfaces = [new AgentInterface { Url = publicUrl.TrimEnd('/') + "/a2a/destination-expert", ProtocolBinding = ProtocolBindingNames.HttpJson, ProtocolVersion = "0.3" }],
                Capabilities = new AgentCapabilities { Streaming = false },
                DefaultInputModes = ["text/plain"], DefaultOutputModes = ["text/plain"],
                Skills = [new A2A.AgentSkill { Id = "destination-advice", Name = "Destination advice", Description = "Recommends attractions, activities, and practical local considerations.", Tags = ["travel", "destinations", "activities"] }]
            };

            app.MapGet("/health", () => Results.Ok(new { service = "DestinationExpert", status = "ready" }));
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.StartsWithSegments("/a2a/destination-expert"))
                    context.RequestServices.GetRequiredService<InvocationCounter>().Increment();
                await next();
            });
            app.MapGet("/diagnostics/invocations", (InvocationCounter counter) => new { count = counter.Count });
            app.MapWellKnownAgentCard(card);
            app.MapA2AHttpJson(agent, "/a2a/destination-expert");
            return app;
        }

        private static AIAgent CreateAgent(IConfiguration configuration)
        {
            if (configuration.GetValue<bool>("DemoMode"))
                return new DemoDestinationChatClient().AsAIAgent("DestinationExpert", Instructions);

            var endpoint = configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is required.");
            var key = configuration["AzureOpenAI:ApiKey"] ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is required.");
            var deployment = configuration["AzureOpenAI:DeploymentName"] ?? throw new InvalidOperationException("AzureOpenAI:DeploymentName is required.");
            return new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(key)).GetChatClient(deployment).AsIChatClient().AsAIAgent("DestinationExpert", Instructions);
        }

        private const string Instructions = "You are a destination expert. Give concise, practical advice about attractions, activities, local transport, timing, and cultural considerations. Do not book travel or invent current prices.";
    }

    public sealed class InvocationCounter { private int _count; public int Count => _count; public void Increment() => Interlocked.Increment(ref _count); }

    internal sealed class DemoDestinationChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("chapter18-demo", new Uri("http://localhost"), "destination-demo");
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = string.Join(" ", messages.Select(m => m.Text));
            var answer = text.Contains("second day", StringComparison.OrdinalIgnoreCase)
                ? "For the second day, I recommended Amber Fort early, then Panna Meena ka Kund and the old-city markets."
                : "For Jaipur, prioritize the City Palace and Jantar Mantar on day one, Amber Fort early on day two, and local crafts plus a relaxed old-city walk on day three. Allow extra road time and dress respectfully at religious sites.";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        { var response = await GetResponseAsync(messages, options, cancellationToken); yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text); }
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == typeof(ChatClientMetadata) ? Metadata : serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}

public partial class Program;
