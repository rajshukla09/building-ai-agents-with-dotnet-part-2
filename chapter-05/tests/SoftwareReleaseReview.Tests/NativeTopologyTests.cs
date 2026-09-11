using Xunit;

namespace SoftwareReleaseReview.Tests;

public sealed class NativeTopologyTests
{
    [Fact]
    public void Sequential_uses_native_builder_in_required_order()
    {
        var source = Source("SequentialReview.cs");
        Assert.Contains("[security.Create(), quality.Create(), architecture.Create(), release.Create()]", source);
        Assert.Contains("AgentWorkflowBuilder.BuildSequential(agents)", source);
    }

    [Fact]
    public void Concurrent_uses_native_fan_out_and_feeds_fan_in_to_release()
    {
        var source = Source("ConcurrentReview.cs");
        Assert.Contains("AgentWorkflowBuilder.BuildConcurrent(independentReviewers)", source);
        Assert.Contains("Independent findings (fan-in)", source);
    }

    [Fact]
    public void Handoff_uses_direct_native_transfers()
    {
        var source = Source("HandoffReview.cs");
        Assert.Contains("CreateHandoffBuilderWith(releaseAgent)", source);
        Assert.Equal(4, source.Split(".WithHandoff(").Length - 1);
        Assert.DoesNotContain("while (", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Group_chat_has_three_participants_and_a_turn_limit()
    {
        var source = Source("GroupChatReview.cs");
        Assert.Contains("[security.Create(), quality.Create(), architecture.Create()]", source);
        Assert.Contains("CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents)", source);
        Assert.Contains("AddParticipants(participants)", source);
        Assert.Contains("MaximumTurns = 6", source);
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
        "SoftwareReleaseReview.Api", "Orchestrations", file));
}
