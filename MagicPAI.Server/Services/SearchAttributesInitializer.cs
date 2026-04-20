// MagicPAI.Server/Services/SearchAttributesInitializer.cs
// Idempotent Temporal search attribute registration per temporal.md §12.7.
// The Phase 1 Day 1 guide registered these once via the Temporal CLI; this
// hosted service keeps them registered across fresh-install deployments.
using Temporalio.Api.Enums.V1;
using Temporalio.Api.OperatorService.V1;
using Temporalio.Client;

namespace MagicPAI.Server.Services;

public class SearchAttributesInitializer : IHostedService
{
    private readonly ITemporalClient _client;
    private readonly IConfiguration _cfg;
    private readonly ILogger<SearchAttributesInitializer> _log;

    public SearchAttributesInitializer(
        ITemporalClient client,
        IConfiguration cfg,
        ILogger<SearchAttributesInitializer> log)
    {
        _client = client;
        _cfg = cfg;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var ns = _cfg["Temporal:Namespace"] ?? "magicpai";

        var required = new (string Name, IndexedValueType Type)[]
        {
            ("MagicPaiAiAssistant", IndexedValueType.Text),
            ("MagicPaiModel", IndexedValueType.Text),
            ("MagicPaiWorkflowType", IndexedValueType.Text),
            ("MagicPaiSessionKind", IndexedValueType.Text),
            ("MagicPaiCostUsdBucket", IndexedValueType.Int),
        };

        try
        {
            var service = _client.OperatorService;
            foreach (var (name, type) in required)
            {
                try
                {
                    var request = new AddSearchAttributesRequest
                    {
                        Namespace = ns
                    };
                    request.SearchAttributes.Add(name, type);
                    await service.AddSearchAttributesAsync(request);
                    _log.LogInformation(
                        "Registered Temporal search attribute {Name}={Type}", name, type);
                }
                catch (Temporalio.Exceptions.RpcException ex)
                    when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.AlreadyExists)
                {
                    // Already registered — fine.
                }
                catch (Temporalio.Exceptions.RpcException ex)
                    when (ex.Code == Temporalio.Exceptions.RpcException.StatusCode.InvalidArgument &&
                          ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    // Some servers return InvalidArgument on re-registration.
                }
            }
        }
        catch (Exception ex)
        {
            // Temporal might be unreachable during dev/offline; we log and carry on
            // so unit/integration tests can boot the host without a live server.
            _log.LogWarning(ex,
                "Temporal search attribute registration skipped (namespace={Ns})", ns);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
