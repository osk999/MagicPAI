using Medallion.Threading;
using Medallion.Threading.Postgres;

namespace MagicPAI.Server.Bridge;

public static class PostgresDistributedLockFactory
{
    public static IDistributedLockProvider Create(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("A PostgreSQL connection string is required for distributed locking.");

        return new PostgresDistributedSynchronizationProvider(connectionString, options =>
        {
            options.KeepaliveCadence(TimeSpan.FromMinutes(5));
            options.UseMultiplexing();
        });
    }
}
