using ProMaxx2.QA.Application.Dashboard;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ProMaxx2.QA.Application.Identity;
using ProMaxx2.QA.Infrastructure;
using ProMaxx2.QA.Infrastructure.Identity;
using ProMaxx2.QA.Infrastructure.Persistence;
using System.Text;
using ProMaxx2.QA.Application.Projects;
using ProMaxx2.QA.Application.Releases;
using ProMaxx2.QA.Application.Requirements;
using ProMaxx2.QA.Application.TestManagement;
using ProMaxx2.QA.Application.Execution;
using ProMaxx2.QA.Api.Services;
using ProMaxx2.QA.Application.Common;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers(options => options.Filters.Add<ProjectAccessFilter>());
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ProMaxx2 QA Management System API",
        Version = "v1",
        Description = "REST API สำหรับระบบจัดการคุณภาพ QA - ProMaxx2 QA Hub",
        Contact = new OpenApiContact { Name = "ProMaxx2 Team" }
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Token จากการ Login\n\nใส่ token โดยไม่ต้องมีคำว่า \"Bearer \" นำหน้า"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();
builder.Services.AddDataProtection();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<ReleaseService>();
builder.Services.AddScoped<RequirementService>();
builder.Services.AddScoped<TestCaseService>();
builder.Services.AddScoped<TestSuiteService>();
builder.Services.AddScoped<TestCycleService>();
builder.Services.AddScoped<ExecutionService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<AdministrationService>();
builder.Services.AddScoped<RequirementAiService>();
builder.Services.AddScoped<TestCaseAiService>();
builder.Services.AddScoped<TestSuiteAiService>();
builder.Services.AddScoped<SharedAiConfigurationService>();
builder.Services.AddScoped<ProjectAccessContext>();
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? throw new InvalidOperationException("Missing Jwt configuration.");
if (Encoding.UTF8.GetByteCount(jwt.Key) < 32) throw new InvalidOperationException("Jwt:Key must contain at least 32 bytes. Use a secret store outside Development.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer=true, ValidIssuer=jwt.Issuer, ValidateAudience=true, ValidAudience=jwt.Audience, ValidateLifetime=true, ValidateIssuerSigningKey=true, IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), ClockSkew=TimeSpan.FromSeconds(30) };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminUser",p=>p.RequireClaim("permission","ADMIN.USER"))
    .AddPolicy("ProjectView",p=>p.RequireClaim("permission","PROJECT.VIEW"))
    .AddPolicy("ProjectEdit",p=>p.RequireClaim("permission","PROJECT.EDIT"))
    .AddPolicy("RequirementView",p=>p.RequireClaim("permission","REQUIREMENT.VIEW"))
    .AddPolicy("RequirementEdit",p=>p.RequireClaim("permission","REQUIREMENT.EDIT"))
    .AddPolicy("TestCaseView",p=>p.RequireClaim("permission","TESTCASE.VIEW"))
    .AddPolicy("TestCaseEdit",p=>p.RequireClaim("permission","TESTCASE.EDIT"));
builder.Services.AddAuthorizationBuilder().AddPolicy("DefectEdit",p=>p.RequireClaim("permission","DEFECT.EDIT"));
builder.Services.AddAuthorizationBuilder().AddPolicy("ExecutionRun",p=>p.RequireClaim("permission","EXECUTION.RUN"));
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
if (allowedOrigins == null || allowedOrigins.Length == 0)
{
    allowedOrigins = new[] { "http://localhost:5173", "http://192.168.200.219:5173" };
}
builder.Services.AddCors(options => options.AddPolicy("Web", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
var app = builder.Build();
app.UseCors("Web");
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Trace-Id"] = context.TraceIdentifier;
    await next();
});
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ProMaxx2 QA API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "ProMaxx2 QA Management System API";
    });
}
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
    await app.Services.InitializeDatabaseAsync(builder.Configuration["Seed:AdminPassword"]);
app.Run();

public partial class Program;
