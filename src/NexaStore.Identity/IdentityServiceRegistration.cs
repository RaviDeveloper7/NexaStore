// IdentityServiceRegistration.cs — wires the entire Identity layer into DI.
// INTERVIEW: This is the only place that knows ASP.NET Core Identity exists.
// The Application layer depends on IAuthService and ICurrentUserService —
// both interfaces defined in Application, implemented here.
// Swapping to a different auth provider = rewrite this file only.
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NexaStore.Application.Common.Interfaces.Identity;
using NexaStore.Identity.DbContext;
using NexaStore.Identity.Models;
using NexaStore.Identity.Services;
using NexaStore.Identity.Settings;
using System.Text;

namespace NexaStore.Identity;

public static class IdentityServiceRegistration
{
    public static IServiceCollection AddIdentityServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Bind JwtSettings via Options Pattern ---
        // INTERVIEW: Configure<T>() binds the section to the strongly-typed class
        // and registers it as IOptions<JwtSettings> in DI.
        // AuthService receives IOptions<JwtSettings> — never raw IConfiguration.
        services.Configure<JwtSettings>(
            configuration.GetSection(JwtSettings.SectionName));

        // --- Identity DbContext ---
        services.AddDbContext<NexaStoreIdentityDbContext>(options =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sqlOptions => sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null));
        });

        // --- ASP.NET Core Identity ---
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = false;
            options.User.RequireUniqueEmail = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<NexaStoreIdentityDbContext>()
        .AddDefaultTokenProviders();

        // --- JWT Authentication ---
        // Read key directly here for TokenValidationParameters —
        // IOptions<T> is not available before the service provider is built
        var jwtSettings = configuration
            .GetSection(JwtSettings.SectionName)
            .Get<JwtSettings>()
            ?? throw new InvalidOperationException(
                $"'{JwtSettings.SectionName}' configuration section is missing.");

        var key = Encoding.UTF8.GetBytes(jwtSettings.Key);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtSettings.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                options.IncludeErrorDetails = true;
            });

        // --- Application Service Registrations ---
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        return services;
    }
}
