using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Rinha.Api;

public sealed class ReferenceStore(IConfiguration configuration, ILogger<ReferenceStore> logger)
{
    private const int Dim = Vectorizer.Dim;
    private const int DefaultExpectedVectors = 3_000_000;
    private float[] _vectors = [];
    private bool[] _isFraud = [];

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
        logger.LogInformation("Normalização e {Count} MCCs carregados (default 0.5).", MccRisk.Count);

        int expected = configuration.GetValue("EXPECTED_VECTORS", DefaultExpectedVectors);
        var vectors = new List<float>(expected * Dim);
        var isFraud = new List<bool>(expected);

        await using var gz = File.OpenRead(Path.Combine(dir, "references.json.gz"));
        await using var raw = new GZipStream(gz, CompressionMode.Decompress);
        await foreach (var entry in JsonSerializer.DeserializeAsyncEnumerable(raw, AppJsonContext.Default.ReferenceEntry, ct))
        {
            if (entry is null || entry.Vector.Length != Dim)
                throw new InvalidDataException($"Entrada de referência inválida (dimensão != {Dim}).");

            vectors.AddRange(entry.Vector);
            isFraud.Add(entry.Label == "fraud");
        }

        _vectors = [.. vectors];
        _isFraud = [.. isFraud];

        VectorCount = isFraud.Count;
        FraudCount = isFraud.Count(f => f);
        LegitCount = VectorCount - FraudCount;
        IsReady = true;
        sw.Stop();

        logger.LogInformation(
            "Dataset carregado: {Total:N0} vetores ({Fraud:N0} fraude / {Legit:N0} legítimo) em {Elapsed}. RAM ~{Mb} MB no array de vetores.",
            VectorCount, FraudCount, LegitCount, sw.Elapsed, (_vectors.Length * sizeof(float)) / (1024 * 1024));
    }

    public int CountFraudInTop5(ReadOnlySpan<float> query)
    {
        var vectors = _vectors;
        var isFraud = _isFraud;
        int n = isFraud.Length;

        Span<float> bestDist = stackalloc float[5];
        Span<bool> bestFraud = stackalloc bool[5];
        bestDist.Fill(float.MaxValue);
        int worst = 0;

        for (int i = 0; i < n; i++)
        {
            int off = i * Dim;
            float s = 0f;
            for (int j = 0; j < Dim; j++)
            {
                float diff = query[j] - vectors[off + j];
                s += diff * diff;
            }

            if (s < bestDist[worst])
            {
                bestDist[worst] = s;
                bestFraud[worst] = isFraud[i];
                worst = 0;
                for (int k = 1; k < 5; k++)
                    if (bestDist[k] > bestDist[worst]) worst = k;
            }
        }

        int fraud = 0;
        for (int k = 0; k < 5; k++)
            if (bestFraud[k]) fraud++;
        return fraud;
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
