using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ObservableReleaseReview.Api.Observability;

public static class ServiceCollectionExtensions
{
    private const string MafWorkflowActivitySource = "Microsoft.Agents.AI.Workflows";

    public static IServiceCollection AddReleaseReviewObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? configuration["OpenTelemetry:OtlpEndpoint"]
            ?? throw new InvalidOperationException(
                "Configure OpenTelemetry:OtlpEndpoint or OTEL_EXPORTER_OTLP_ENDPOINT.");

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var otlpEndpoint))
        {
            throw new InvalidOperationException(
                $"The configured OTLP endpoint '{endpoint}' is not an absolute URI.");
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("ObservableReleaseReview"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddSource(MafWorkflowActivitySource)
                .AddOtlpExporter(options => options.Endpoint = otlpEndpoint));

        return services;
    }
}
