using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Proxy.Models
{
    /// <summary>
    /// <see cref="DbContext"/> For flip tracking
    /// </summary>
    public class ProxyDbContext : DbContext
    {
        public DbSet<ApiKey> ApiKeys { get; set; }

        /// <summary>
        /// Creates a new instance of <see cref="ProxyDbContext"/>
        /// </summary>
        /// <param name="options"></param>
        public ProxyDbContext(DbContextOptions<ProxyDbContext> options)
        : base(options)
        {
        }

        /// <summary>
        /// Configures additional relations and indexes
        /// </summary>
        /// <param name="modelBuilder"></param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ApiKey>(entity =>
            {
                entity.HasIndex(e => new { e.LastUsed });
            });
        }
    }
}