using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProMaxx2.QA.Application.Identity;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;
using ProMaxx2.QA.Application.Projects;

namespace ProMaxx2.QA.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<QaDbContext>(o=>o.UseSqlServer(configuration.GetConnectionString("QaDatabase"), sql=>sql.MigrationsAssembly(typeof(QaDbContext).Assembly.FullName)));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.AddScoped<IIdentityRepository,IdentityRepository>(); services.AddSingleton<IPasswordService,PasswordService>(); services.AddScoped<ITokenService,TokenService>();
        services.AddScoped<IProjectRepository,ProjectRepository>();
        return services;
    }
}
