namespace EnterpriseIncidentInvestigator.Api.Contracts;

public sealed record InvestigateIncidentRequest(string Incident, Guid? RunId = null);
public sealed record EvidenceItem(string Source, string Detail);
public sealed record ToolInvocation(int Sequence, string Tool, long DurationMilliseconds, bool Succeeded,
                                    string Server = "demo", string? ErrorSummary = null);
public sealed record AgentFinding(string AgentName, string Summary, IReadOnlyList<EvidenceItem> Evidence,
                                  double Confidence, string? SuggestedNextInvestigation,
                                  IReadOnlyList<ToolInvocation> ToolInvocations);
public sealed record AgentInvocation(int Sequence, string Agent, long DurationMilliseconds, bool Succeeded,
                                     string ReasonSelected);
public sealed record RootCauseConclusion(string Summary, string RootCause, double Confidence, string Recommendation);
public sealed record InvestigationResponse(string Summary, string RootCause, double Confidence, string Recommendation,
                                           IReadOnlyList<AgentInvocation> AgentInvocations,
                                           IReadOnlyList<AgentFinding> Findings, bool InvestigationLimitReached,
                                           string TerminationReason, Guid RunId = default);

public sealed record InvestigationRunSummary(Guid RunId, string Incident, string Status, DateTimeOffset StartedAt,
                                             DateTimeOffset? CompletedAt, long? DurationMilliseconds,
                                             IReadOnlyList<string> AgentsUsed);
public sealed record InvestigationEventView(Guid RunId, int Sequence, string EventType, DateTimeOffset Timestamp,
                                            string? Agent, int? AgentInvocationSequence, string? ToolServer,
                                            string? Tool, int? ToolSequence, string Status, string? Reason,
                                            string? InputContextSummary, string? FindingSummary,
                                            string? SuggestedNextInvestigation, double? Confidence,
                                            long? DurationMilliseconds, string? ErrorSummary);
public sealed record InvestigationRunView(InvestigationRunSummary Run, IReadOnlyList<InvestigationEventView> Events,
                                          InvestigationResponse? Result, int MaxAgentInvocations,
                                          int MaxToolCallsPerAgent);
