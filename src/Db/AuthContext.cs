using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using auth.Services;
namespace auth.Db
{
    public class AuthContext : IdentityDbContext<User>, IHealthCheck
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger<AuthContext> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _hostEnvironment;

        // Entity Sets
        public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();

        public AuthContext(
            DbContextOptions<AuthContext> options,
            ICurrentUserService currentUserService,
            ILogger<AuthContext> logger,
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration,
            IHostEnvironment hostEnvironment) : base(options)
        {
            _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _hostEnvironment = hostEnvironment ?? throw new ArgumentNullException(nameof(hostEnvironment));
            
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
            ChangeTracker.LazyLoadingEnabled = false;
            // Removed CascadeDeleteTiming as it's not available in this EF Core version.
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseNpgsql(
                    _configuration.GetConnectionString("PostgreSQLConnection"),
                    npgOptions =>
                    {
                        npgOptions.EnableRetryOnFailure(
                            maxRetryCount: 5,
                            maxRetryDelay: TimeSpan.FromSeconds(30),
                            errorCodesToAdd: null);
                        npgOptions.CommandTimeout(60);
                    });
            }

            if (_hostEnvironment.IsDevelopment())
            {
                optionsBuilder.EnableDetailedErrors();
                optionsBuilder.EnableSensitiveDataLogging();
                optionsBuilder.LogTo(
                    msg => _logger.LogInformation(msg),
                    new[] { DbLoggerCategory.Database.Command.Name },
                    LogLevel.Information);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Configure PostgreSQL xmin concurrency tokens for Product and Order
          
            // UserProfile Configuration (ensure LastLogin exists)
            modelBuilder.Entity<UserProfile>(entity =>
            {
                entity.HasIndex(up => up.UserId)
                      .IsUnique()
                      .HasFilter("\"UserId\" IS NOT NULL");
                
                entity.Property(up => up.LastLogin)
                      .HasDefaultValueSql("NOW()");
            });

            

            modelBuilder.Entity<RevokedToken>(entity =>
            {
                entity.HasIndex(rt => rt.Token)
                    .IsUnique()
                    .HasDatabaseName("IX_RevokedTokens_Token");
                
                entity.HasIndex(rt => rt.RevokedAt)
                    .HasDatabaseName("IX_RevokedTokens_RevokedAt");
                
                entity.HasIndex(rt => rt.ExpiresAt)
                    .HasDatabaseName("IX_RevokedTokens_ExpiresAt");
                
                entity.Property(rt => rt.RevokedAt)
                    .HasDefaultValueSql("NOW()");
                
                // Optional: Configure relationship with User if needed
                entity.HasOne(rt => rt.User)
                    .WithMany()
                    .HasForeignKey(rt => rt.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            if (_hostEnvironment.IsDevelopment())
            {
               // SeedDevelopmentData(modelBuilder);
            }
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            
            try
            {
                AuditEntities();
                var result = await base.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Concurrency conflict detected. User: {UserId}", _currentUserService.UserId);
                throw new ConcurrencyException("Data conflict detected. Please refresh and try again.", ex);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Unique constraint violation. User: {UserId}", _currentUserService.UserId);
                throw new DataUpdateException("Duplicate entry detected. Please check your data.", ex);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogCritical(ex, "Database error. Path: {Path}",
                    _httpContextAccessor.HttpContext?.Request.Path);
                throw;
            }
        }

        private void AuditEntities()
        {
            var now = DateTime.UtcNow;
            var userId = _currentUserService.UserId ?? "system";
            var ipAddress = _currentUserService.ClientIp ?? "unknown";

            foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userId;
                    entry.Entity.CreatedFromIp = ipAddress;
                }

                if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
                {
                    entry.Entity.LastModifiedDate = now;
                    entry.Entity.LastModifiedBy = userId;
                    entry.Entity.ModifiedFromIp = ipAddress;
                }
            }
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var data = new Dictionary<string, object>
            {
                { "connections", Database.GetDbConnection().ConnectionString },
                { "pending_migrations", (await Database.GetPendingMigrationsAsync(cancellationToken)).Count() }
            };

            try
            {
                if (!await Database.CanConnectAsync(cancellationToken))
                    return HealthCheckResult.Unhealthy("Cannot connect", data: data);

                await Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
                return HealthCheckResult.Healthy("OK", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed");
                return HealthCheckResult.Unhealthy("Connection failure", ex, data);
            }
        }

      
    }
}