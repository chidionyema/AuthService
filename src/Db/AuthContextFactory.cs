using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using auth.Services;
using auth.Db;

namespace auth.Db
{
    public class AuthContextFactory : IDesignTimeDbContextFactory<AuthContext>
    {
        public AuthContext CreateDbContext(string[] args)
        {
            // Build configuration from the current directory.
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddJsonFile("appsettings.Production.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            // Get the connection string
            string connectionString = config.GetConnectionString("DefaultConnection") 
                ?? config.GetConnectionString("PostgreSQLConnection")
                ?? throw new InvalidOperationException("No connection string found in configuration");

            Console.WriteLine($"[Info] Using connection string from configuration.");

            // Configure the DbContext options.
            var optionsBuilder = new DbContextOptionsBuilder<AuthContext>();
            optionsBuilder
                .UseNpgsql(connectionString, npgOptions =>
                {
                    npgOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorCodesToAdd: null);
                    npgOptions.CommandTimeout(60);
                })
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .EnableDetailedErrors();

            // Create dependencies for AuthContext
            var loggerFactory = LoggerFactory.Create(builder => 
                builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            var logger = loggerFactory.CreateLogger<AuthContext>();

            var httpContextAccessor = new HttpContextAccessor();
            var currentUserService = new CurrentUserService(httpContextAccessor);
            var hostEnvironment = new DesignTimeHostEnvironment();

            return new AuthContext(
                optionsBuilder.Options,
                currentUserService,
                logger,
                httpContextAccessor,
                config,
                hostEnvironment);
        }
    }

    // Minimal implementation of IHostEnvironment for design time.
    public class DesignTimeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "auth";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(Directory.GetCurrentDirectory());
    }
}