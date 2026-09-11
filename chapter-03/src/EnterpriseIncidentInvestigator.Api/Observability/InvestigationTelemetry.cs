using System.Diagnostics;

namespace EnterpriseIncidentInvestigator.Api.Observability;

public static class InvestigationTelemetry
{
    public const string SourceName = "EnterpriseIncidentInvestigator";
    public static readonly ActivitySource Source = new(SourceName);
}
