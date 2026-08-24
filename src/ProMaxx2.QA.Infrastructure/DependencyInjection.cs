using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProMaxx2.QA.Application.Identity;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.Releases;
using ProMaxx2.QA.Application.Governance;
using ProMaxx2.QA.Application.Requirements;
using ProMaxx2.QA.Application.TestManagement;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Application.Dashboard;
using ProMaxx2.QA.Application.Automation;

namespace ProMaxx2.QA.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<QaDbContext>(o=>o.UseSqlServer(configuration.GetConnectionString("QaDatabase"), sql=>sql.MigrationsAssembly(typeof(QaDbContext).Assembly.FullName)));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.AddScoped<IIdentityRepository,IdentityRepository>(); services.AddSingleton<IPasswordService,PasswordService>(); services.AddScoped<ITokenService,TokenService>();
        services.AddScoped<IProjectRepository,ProjectRepository>();
        services.AddScoped<IReleaseRepository,ReleaseRepository>();
        services.AddScoped<IRequirementRepository,RequirementRepository>();
        services.AddScoped<ITestCaseRepository,TestCaseRepository>();
        services.AddScoped<ITestSuiteRepository,TestSuiteRepository>();
        services.AddScoped<ITestCycleRepository,TestCycleRepository>();
        services.AddScoped<IExecutionRepository,ExecutionRepository>();
        services.AddScoped<IAdministrationRepository,AdministrationRepository>();
        services.AddScoped<IDashboardRepository,DashboardRepository>();
        services.AddScoped<IRiskAcceptanceRepository,RiskAcceptanceRepository>();
        services.AddScoped<IReleaseSignoffRepository,ReleaseSignoffRepository>();
        services.AddScoped<IAutomationRepository,AutomationRepository>();
        services.AddScoped<Identity.ProjectAccessService>();
        return services;
    }
}
