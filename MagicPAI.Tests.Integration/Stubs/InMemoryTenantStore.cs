using Elsa.Common.Multitenancy;
using Elsa.Tenants;

namespace MagicPAI.Tests.Integration.Stubs;

internal sealed class InMemoryTenantStore : ITenantStore
{
    private readonly List<Tenant> _tenants = [];

    public Task<Tenant?> FindAsync(TenantFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(filter.Apply(_tenants.AsQueryable()).FirstOrDefault());

    public Task<Tenant?> FindAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tenants.FirstOrDefault(x => x.Id == id));

    public Task<IEnumerable<Tenant>> FindManyAsync(TenantFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<Tenant>>(filter.Apply(_tenants.AsQueryable()).ToList());

    public Task<IEnumerable<Tenant>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<Tenant>>(_tenants.ToList());

    public Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        _tenants.RemoveAll(x => x.Id == tenant.Id);
        _tenants.Add(tenant);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        _tenants.RemoveAll(x => x.Id == tenant.Id);
        _tenants.Add(tenant);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var removed = _tenants.RemoveAll(x => x.Id == id) > 0;
        return Task.FromResult(removed);
    }

    public Task<long> DeleteAsync(TenantFilter filter, CancellationToken cancellationToken = default)
    {
        var toDelete = filter.Apply(_tenants.AsQueryable()).Select(x => x.Id).ToList();
        foreach (var id in toDelete)
            _tenants.RemoveAll(x => x.Id == id);

        return Task.FromResult((long)toDelete.Count);
    }
}
