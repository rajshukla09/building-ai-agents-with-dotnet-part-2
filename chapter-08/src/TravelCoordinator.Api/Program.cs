using A2A;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ClientModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using TravelCoordinator.Api;

var app = TravelCoordinatorApplication.Build(args);
await app.RunAsync();

namespace TravelCoordinator.Api
{
    public static class TravelCoordinatorApplication
    {
        public static WebApplication Build(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddHttpClient("a2a");
            builder.Services.AddSingleton(CreateCoordinatorAgent(builder.Configuration));
            builder.Services.AddSingleton<CoordinatorService>();
            var app = builder.Build();
            app.UseDefaultFiles(); app.UseStaticFiles();
            app.MapGet("/health", () => Results.Ok(new { service = "TravelCoordinator", status = "ready" }));
            app.MapPost("/api/travel", async (TravelRequest request, CoordinatorService service, CancellationToken ct) =>
            {
                var result = await service.RunAsync(request, ct);
                return result.Status == "Failed" ? Results.Json(result, statusCode: 503) : Results.Ok(result);
            });
            app.MapFallbackToFile("index.html");
            return app;
        }

        private static AIAgent CreateCoordinatorAgent(IConfiguration configuration)
        {
            if (configuration.GetValue<bool>("DemoMode")) return new DemoCoordinatorChatClient().AsAIAgent("TravelCoordinator", "Synthesize the remote destination expert's advice into a concise final recommendation.");
            var endpoint = configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is required.");
            var key = configuration["AzureOpenAI:ApiKey"] ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is required.");
            var deployment = configuration["AzureOpenAI:DeploymentName"] ?? throw new InvalidOperationException("AzureOpenAI:DeploymentName is required.");
            return new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(key)).GetChatClient(deployment).AsIChatClient().AsAIAgent("TravelCoordinator", "You coordinate travel advice. Use the destination expert response supplied in the prompt, identify it as remote advice, and produce a concise final recommendation.");
        }
    }

    public sealed record TravelRequest(string Message, string? ConversationId);
    public sealed record TimelineEvent(string Operation, DateTimeOffset Timestamp, long DurationMs, bool Success, string? Detail = null);
    public sealed record CardView(string Name, string Description, string Version, string Endpoint, string[] Skills);
    public sealed record TravelResult(string Status, string ConversationId, string UserRequest, string? RemoteRequest, string? RemoteResponse, string? FinalResponse, CardView? AgentCard, long DurationMs, IReadOnlyList<TimelineEvent> Events, string? Error = null);

    public sealed class CoordinatorService(IHttpClientFactory clients, IConfiguration configuration, AIAgent coordinator, ILoggerFactory loggerFactory)
    {
        private readonly ConcurrentDictionary<string, RemoteConversation> _conversations = new();

        public async Task<TravelResult> RunAsync(TravelRequest request, CancellationToken ct)
        {
            var total = Stopwatch.StartNew(); var events = new List<TimelineEvent>();
            var conversationId = string.IsNullOrWhiteSpace(request.ConversationId) ? Guid.NewGuid().ToString("N") : request.ConversationId;
            Add("RequestReceived", true, request.Message);
            string? remoteRequest = null; string? remoteResponse = null; CardView? cardView = null;
            try
            {
                var remoteBase = new Uri(configuration["DestinationExpert:BaseUrl"] ?? "http://localhost:5181");
                var http = clients.CreateClient("a2a");
                var sw = Stopwatch.StartNew(); Add("AgentCardDiscoveryStarted", true, remoteBase.ToString());
                var resolver = new A2ACardResolver(remoteBase, http, logger: loggerFactory.CreateLogger<A2ACardResolver>());
                var card = await resolver.GetAgentCardAsync(ct); sw.Stop();
                var endpoint = card.SupportedInterfaces.First().Url;
                cardView = new(card.Name, card.Description, card.Version, endpoint, card.Skills.Select(s => s.Name).ToArray());
                events.Add(new("AgentCardDiscovered", DateTimeOffset.UtcNow, sw.ElapsedMilliseconds, true, card.Name));

                var conversation = await GetConversationAsync(conversationId!, card, http, ct);
                remoteRequest = request.Message;
                Add("A2AMessageSent", true, endpoint); Add("RemoteAgentStarted", true, card.Name);
                sw.Restart(); var response = await conversation.Agent.RunAsync(remoteRequest, conversation.Session, cancellationToken: ct); sw.Stop();
                remoteResponse = response.Text;
                events.Add(new("A2AResponseReceived", DateTimeOffset.UtcNow, sw.ElapsedMilliseconds, true, remoteResponse));

                sw.Restart();
                var final = await coordinator.RunAsync($"User request:\n{request.Message}\n\nDestinationExpert response received over A2A:\n{remoteResponse}", cancellationToken: ct); sw.Stop();
                events.Add(new("CoordinatorResponseGenerated", DateTimeOffset.UtcNow, sw.ElapsedMilliseconds, true)); Add("Completed", true);
                return new("Completed", conversationId!, request.Message, remoteRequest, remoteResponse, final.Text, cardView, total.ElapsedMilliseconds, events);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Add("Failed", false, $"DestinationExpert is unavailable or the A2A exchange failed: {ex.Message}");
                return new("Failed", conversationId!, request.Message, remoteRequest, remoteResponse, null, cardView, total.ElapsedMilliseconds, events, "DestinationExpert is unavailable. The coordinator is still running; start the remote service and try again.");
            }
            void Add(string op, bool success, string? detail = null) => events.Add(new(op, DateTimeOffset.UtcNow, total.ElapsedMilliseconds, success, detail));
        }

        private async Task<RemoteConversation> GetConversationAsync(string id, AgentCard card, HttpClient http, CancellationToken ct)
        {
            if (_conversations.TryGetValue(id, out var existing)) return existing;
            var agent = card.AsAIAgent(http, loggerFactory: loggerFactory);
            var created = new RemoteConversation(agent, await agent.CreateSessionAsync(ct));
            return _conversations.GetOrAdd(id, created);
        }
        private sealed record RemoteConversation(AIAgent Agent, AgentSession Session);
    }

    internal sealed class DemoCoordinatorChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("chapter18-demo", new Uri("http://localhost"), "coordinator-demo");
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); var input = messages.Last().Text; var remote = input[(input.IndexOf("DestinationExpert response", StringComparison.Ordinal) + "DestinationExpert response received over A2A:".Length)..].Trim(); return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Final recommendation, using DestinationExpert's A2A advice: {remote}"))); }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        { var response = await GetResponseAsync(messages, options, cancellationToken); yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text); }
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType == typeof(ChatClientMetadata) ? Metadata : serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}

public partial class Program;
