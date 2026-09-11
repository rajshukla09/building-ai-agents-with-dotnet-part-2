using EnterpriseArchitectureAssessment.Api.Agents;
using EnterpriseArchitectureAssessment.Api.Configuration;
using EnterpriseArchitectureAssessment.Api.Mcp.Client;
using EnterpriseArchitectureAssessment.Api.Orchestration;
using EnterpriseArchitectureAssessment.Api.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EnterpriseArchitectureAssessment.Api.DependencyInjection;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEnterpriseAssessment(this IServiceCollection services,IConfiguration configuration)
    {
        services.Configure<MagenticAssessmentOptions>(configuration.GetSection(MagenticAssessmentOptions.SectionName));
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));
        services.AddSingleton<AssessmentEventWriter>();
        services.AddSingleton<IEnterpriseMcpClient,EnterpriseMcpClient>();
        services.AddTransient<ArchitectureAgent>(); services.AddTransient<SecurityAgent>(); services.AddTransient<CostAgent>();
        services.AddTransient<OperationsAgent>(); services.AddTransient<MigrationAgent>(); services.AddTransient<ResearchAgent>();
        services.AddTransient<AssessmentAgentFactory>(x=>x.GetRequiredService<ArchitectureAgent>());
        services.AddTransient<AssessmentAgentFactory>(x=>x.GetRequiredService<SecurityAgent>());
        services.AddTransient<AssessmentAgentFactory>(x=>x.GetRequiredService<CostAgent>());
        services.AddTransient<AssessmentAgentFactory>(x=>x.GetRequiredService<OperationsAgent>());
        services.AddTransient<AssessmentAgentFactory>(x=>x.GetRequiredService<MigrationAgent>());
        services.AddTransient<AssessmentAgentFactory>(x=>x.GetRequiredService<ResearchAgent>());
        services.AddDbContext<AssessmentDbContext>(options => options.UseSqlite(
            configuration.GetConnectionString("Assessments") ?? "Data Source=App_Data/assessments.db"));
        services.AddScoped<IAssessmentRunStore,AssessmentRunRepository>();
        services.AddScoped<IMagenticAssessmentOrchestrator,MagenticAssessmentOrchestrator>();
        services.AddScoped<IAssessmentService,AssessmentService>();
        return services;
    }
}
