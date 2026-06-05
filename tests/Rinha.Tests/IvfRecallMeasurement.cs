using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Rinha.Api;
using Xunit.Abstractions;

namespace Rinha.Tests;

public class IvfRecallMeasurement(ITestOutputHelper output)
{
    private const int Dim = Vectorizer.Dim;

    [Fact]
    public async Task Medir_Recall_IVF_por_nprobe()
    {
        if (Environment.GetEnvironmentVariable("RUN_MEASUREMENT") != "1") return;

        var spec = Environment.GetEnvironmentVariable("SPEC_DIR") ?? "/Users/sergio/Projects/rinha-de-backend-2026";
        var resDir = Environment.GetEnvironmentVariable("RES_DIR")
            ?? "/Users/sergio/Projects/rinha-de-backend-2026-hidekisrn-dotnet/resources";
        var testDataPath = Path.Combine(spec, "test", "test-data.json");
        int n = int.TryParse(Environment.GetEnvironmentVariable("MEASURE_N"), out var v) ? v : 3000;

        if (!File.Exists(testDataPath)) { output.WriteLine($"[skip] {testDataPath} ausente"); return; }

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RESOURCES_PATH"] = resDir,
            ["IVF_CELLS"] = Environment.GetEnvironmentVariable("IVF_CELLS") ?? "1024",
            ["NPROBE"] = "1",
        }).Build();
        using var lf = LoggerFactory.Create(b => b.AddSimpleConsole());
        using var store = new ReferenceStore(cfg, lf.CreateLogger<ReferenceStore>());
        await store.LoadAsync();
        output.WriteLine($"Índice: {store.VectorCount:N0} vetores, {store.CellCount} células.");

        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var testFile = JsonSerializer.Deserialize<TestFile>(File.ReadAllText(testDataPath), opts)!;
        var sample = testFile.Entries.Take(n).ToArray();

        var queries = new byte[sample.Length][];
        var bruteFraud = new int[sample.Length];
        var expectedApprove = new bool[sample.Length];
        for (int i = 0; i < sample.Length; i++)
        {
            var qb = new byte[Knn.Stride];
            Span<float> qf = stackalloc float[Dim];
            Vectorizer.Vectorize(sample[i].Request, store.Normalization, store.RiskFor, qf);
            Quantizer.Quantize(qf, qb);
            queries[i] = qb;
            bruteFraud[i] = store.CountFraudBruteForce(qb);
            expectedApprove[i] = sample[i].ExpectedApproved;
        }

        output.WriteLine($"=== Recall IVF por nprobe (N={sample.Length}, K={store.CellCount}) ===");
        output.WriteLine("nprobe | == brute | decisão!=brute | FP | FN | acerto vs gabarito");
        var lines = new List<string> { $"IVF recall (N={sample.Length}, K={store.CellCount})" };
        foreach (int nprobe in new[] { 1, 4, 8, 16, 32, 64, 128 })
        {
            if (nprobe > store.CellCount) break;
            long sameAsBrute = 0, decisionDiff = 0, fp = 0, fn = 0, correct = 0;
            for (int i = 0; i < sample.Length; i++)
            {
                int ivf = store.CountFraudInTop5(queries[i], nprobe);
                if (ivf == bruteFraud[i]) sameAsBrute++;
                bool ivfApprove = ivf / 5.0 < 0.6;
                bool bruteApprove = bruteFraud[i] / 5.0 < 0.6;
                if (ivfApprove != bruteApprove) decisionDiff++;
                if (ivfApprove == expectedApprove[i]) correct++;
                else if (expectedApprove[i]) fp++;
                else fn++;
            }
            int N = sample.Length;
            var line = $"{nprobe,6} | {100.0 * sameAsBrute / N,7:F2}% | {decisionDiff,4} ({100.0 * decisionDiff / N:F2}%) | {fp,3} | {fn,3} | {100.0 * correct / N:F2}%";
            output.WriteLine(line);
            lines.Add(line);
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "m4-ivf-recall.txt"), lines);
    }

    private sealed record TestFile([property: JsonPropertyName("entries")] TestEntry[] Entries);
    private sealed record TestEntry(
        [property: JsonPropertyName("request")] FraudScoreRequest Request,
        [property: JsonPropertyName("expected_approved")] bool ExpectedApproved,
        [property: JsonPropertyName("expected_fraud_score")] double ExpectedFraudScore);
}
