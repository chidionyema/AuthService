using auth.Contracts;
using auth.Db;
using auth.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using auth.Infrastructure.Repository.Interfaces;
using auth.Infrastructure.Repository;
using auth.Infrastructure.HealthChecks;
using System.Threading.Tasks;
using auth.Infrastructure;

namespace auth.Extensions
{
    public static class InfrastructureExtensions
    {
        public static IServiceCollection AddInfrastructureServices(
            this IServiceCollection services,
            IConfiguration config,
            ILogger logger)
        {
            bool isTestEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Test";

            // 1. Identity
            services.AddIdentity<User, IdentityRole>(options =>
            {
                // Password settings
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = true;
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 1;

                // Lockout settings
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                // User settings
                options.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
                options.User.RequireUniqueEmail = true;

                // Sign-in settings
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedPhoneNumber = false;
            })
            .AddEntityFrameworkStores<AuthContext>()
            .AddDefaultTokenProviders();

            // 2. Database Contexts and Repositories
            services.AddDatabaseServices(config, isTestEnvironment);

            // 3. Health Checks
          //  services.AddHealthChecks()
           //     .AddDbContextCheck<AuthContext>("database")
           //     .AddCheck<DatabaseHealthCheck>("database_detailed");

            return services;
        }

        #region Database Contexts and Repositories

        private static IServiceCollection AddDatabaseServices(this IServiceCollection services, IConfiguration config, bool isTestEnvironment)
        {
            services.AddDbContext<AuthContext>((sp, options) =>
            {
                var dbLogger = sp.GetRequiredService<ILogger<AuthContext>>();
                
                string connectionString;
                
                if (isTestEnvironment)
                {
                    dbLogger.LogInformation("[DBContext-auth] Configuring for test environment.");
                    connectionString = config.GetConnectionString("TestDatabase") 
                        ?? config.GetConnectionString("DefaultConnection")
                        ?? throw new InvalidOperationException("No test database connection string found");
                }
                else
                {
                    dbLogger.LogInformation("[DBContext-auth] Configuring for non-test environment.");
                    connectionString = config.GetConnectionString("DefaultConnection")
                        ?? config.GetConnectionString("PostgreSQLConnection")
                        ?? throw new InvalidOperationException("No database connection string found");
                }

                options.UseNpgsql(connectionString, npgOptions =>
                {
                    npgOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorCodesToAdd: null);
                    npgOptions.CommandTimeout(60);
                });

                // Enable detailed errors and sensitive data logging in development
                var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                if (environment == "Development")
                {
                    options.EnableDetailedErrors();
                    options.EnableSensitiveDataLogging();
                }
            });

            // Register repositories
            services.AddScoped<IUserRepository, UserRepository>();
            
            return services;
        }

        #endregion
    }
}