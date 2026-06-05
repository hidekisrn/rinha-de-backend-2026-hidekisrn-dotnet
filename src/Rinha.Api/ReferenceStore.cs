using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Rinha.Api;

public sealed class ReferenceStore(IConfiguration configuration, ILogger<ReferenceStore> logger)
{
    private const string DefaultMccRisk = "0.5";
    public Normalization Normalization { get; private set; } = null!;
    public IReadOnlyDictionary<string, double> MccRisk { get; private set; } = null!;
    public long VectorCount { get; private set; }
    public long FraudCount { get; private set; }
    public long LegitCount { get; private set; }
    public bool IsReady { get; private set; }

    public double RiskFor(string mcc) => MccRisk.TryGetValue(mcc, out var r) ? r : 0.5;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var dir = ResolveResourcesPath();
        logger.LogInformation("Carregando recursos de {Dir}", dir);

        await using (var fs = File.OpenRead(Path.Combine(dir, "normalization.json")))
        {
            Normalization = (await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.Normalization, ct))!;
        }

        await using (var fs = File.OpenRead(Path.Combine(dir, "mcc_risk.json")))
        {
            MccRisk = (await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.DictionaryStringDouble, ct))!;
        }
        logger.LogInformation("Normalização e {Count} MCCs carregados (default {Default}).", MccRisk.Count, DefaultMccRisk);

        long count = 0, fraud = 0;
        await using var gz = File.OpenRead(Path.Combine(dir, "references.json.gz"));
        await using var raw = new GZipStream(gz, CompressionMode.Decompress);
        await foreach (var entry in JsonSerializer.DeserializeAsyncEnumerable(raw, AppJsonContext.Default.ReferenceEntry, ct))
        {
            count++;
            if (entry is { Label: "fraud" }) fraud++;
        }

        VectorCount = count;
        FraudCount = fraud;
        LegitCount = count - fraud;
        IsReady = true;
        sw.Stop();

        logger.LogInformation(
            "Dataset carregado: {Total:N0} vetores ({Fraud:N0} fraude / {Legit:N0} legítimo) em {Elapsed}.",
            VectorCount, FraudCount, LegitCount, sw.Elapsed);
    }

    private string ResolveResourcesPath()
    {
        var configured = configuration["RESOURCES_PATH"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "resources");
            if (File.Exists(Path.Combine(candidate, "normalization.json")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Não encontrei a pasta 'resources' com normalization.json. " +
            "Defina RESOURCES_PATH ou rode a partir do repositório.");
    }
}
