using A2A;
using DestinationExpert.Api;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
using TravelCoordinator.Api;
using Xunit;

namespace AgentToAgent.Tests;

public sealed class A2AIntegrationTests
{
    [Fact]
    public async Task Independent_agents_discover_exchange_continue_and_handle_outage()
    {
        var destinationUrl = $"http://127.0.0.1:{FreePort()}";
        var coordinatorUrl = $"http://127.0.0.1:{FreePort()}";
        await using var destination = DestinationExpertApplication.Build(["--urls", destinationUrl, "--DemoMode=true", "--Logging:EventLog:LogLevel:Default=None", $"--PublicUrl={destinationUrl}"]);
        await using var coordinator = TravelCoordinatorApplication.Build(["--urls", coordinatorUrl, "--DemoMode=true", "--Logging:EventLog:LogLevel:Default=None", $"--DestinationExpert:BaseUrl={destinationUrl}"]);

        await destination.StartAsync();
        await coordinator.StartAsync();
        using var http = new HttpClient();

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(destinationUrl + "/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(coordinatorUrl + "/health")).StatusCode);

        var resolver = new A2ACardResolver(new Uri(destinationUrl), http);
        var card = await resolver.GetAgentCardAsync();
        Assert.Equal("DestinationExpert", card.Name);
        Assert.Contains(card.SupportedInterfaces, i => i.Url == destinationUrl + "/a2a/destination-expert");
        Assert.Contains(card.Skills, s => s.Id == "destination-advice");

        var firstResponse = await http.PostAsJsonAsync(coordinatorUrl + "/api/travel", new TravelRequest("Ask the destination expert about Jaipur.", null));
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<TravelResult>();
        Assert.NotNull(first); Assert.Equal("Completed", first.Status);
        Assert.Contains("Amber Fort", first.RemoteResponse);
        Assert.Contains(first.RemoteResponse!, first.FinalResponse!);
        Assert.Contains(first.Events, e => e.Operation == "AgentCardDiscovered" && e.Success);
        Assert.Contains(first.Events, e => e.Operation == "A2AMessageSent" && e.Success);
        Assert.Contains(first.Events, e => e.Operation == "A2AResponseReceived" && e.Success);

        var count = await http.GetFromJsonAsync<JsonElement>(destinationUrl + "/diagnostics/invocations");
        Assert.True(count.GetProperty("count").GetInt32() > 0); // The official A2A endpoint received a network request.

        var followUp = await (await http.PostAsJsonAsync(coordinatorUrl + "/api/travel", new TravelRequest("What did it recommend for the second day?", first.ConversationId))).Content.ReadFromJsonAsync<TravelResult>();
        Assert.NotNull(followUp); Assert.Equal(first.ConversationId, followUp.ConversationId);
        Assert.Contains("second day", followUp.RemoteResponse!, StringComparison.OrdinalIgnoreCase);

        await destination.StopAsync();
        var failedResponse = await http.PostAsJsonAsync(coordinatorUrl + "/api/travel", new TravelRequest("Ask about Kyoto.", null));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedResponse.StatusCode);
        var failed = await failedResponse.Content.ReadFromJsonAsync<TravelResult>();
        Assert.Equal("Failed", failed!.Status); Assert.Contains("unavailable", failed.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(coordinatorUrl + "/health")).StatusCode);

        await coordinator.StopAsync();
    }

    private static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
}
