using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Rinha.Api;

public sealed class ReferenceStore(IConfiguration configuration, ILogger<ReferenceStore> logger) : IDisposable
{
    private const int Dim = Vectorizer.Dim;
    private const int Stride = Knn.Stride;
    private const int HeaderSize = 32;
    private const int Magic = 0x00385152;
    private const int DefaultExpectedVectors = 3_000_000;
    private const string BlobFileName = "references.q8.bin";

    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private unsafe byte* _basePtr;
    private unsafe byte* _centPtr;
    private unsafe int* _offsetsPtr;
    private unsafe byte* _vecPtr;
    private unsafe byte* _labelPtr;
    private int _nprobe;

    public Normalization Normalization { get; private set; } = null!;
    public IReadOnlyDictionary<string, double> MccRisk { get; private set; } = null!;
    public long VectorCount { get; private set; }
    public int CellCount { get; private set; }
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
            Normalization = (await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.Normalization, ct))!;

        await using (var fs = File.OpenRead(Path.Combine(dir, "mcc_risk.json")))
            MccRisk = (await JsonSerializer.DeserializeAsync(fs, AppJsonContext.Default.DictionaryStringDouble, ct))!;
        logger.LogInformation("Normalização e {Count} MCCs carregados (default 0.5).", MccRisk.Count);

        var blobPath = Path.Combine(dir, BlobFileName);
        if (!IsBlobValid(blobPath))
            await BuildBlobAsync(Path.Combine(dir, "references.json.gz"), blobPath, ct);
        else
            logger.LogInformation("Blob IVF válido encontrado em {Path}; reaproveitando.", blobPath);

        OpenBlob(blobPath);

        _nprobe = Math.Clamp(configuration.GetValue("NPROBE", 24), 1, CellCount);
        IsReady = true;
        sw.Stop();
        logger.LogInformation(
            "Índice IVF pronto: {Total:N0} vetores, {Cells} células, nprobe={Nprobe} ({Fraud:N0} fraude / {Legit:N0} legítimo) em {Elapsed}.",
            VectorCount, CellCount, _nprobe, FraudCount, LegitCount, sw.Elapsed);
    }

    public int CountFraudInTop5(ReadOnlySpan<byte> query) => CountFraudInTop5(query, _nprobe);

    public unsafe int CountFraudInTop5(ReadOnlySpan<byte> query, int nprobe)
    {
        int k = CellCount;
        int n = checked((int)VectorCount);
        var centroids = new ReadOnlySpan<byte>(_centPtr, k * Stride);
        var offsets = new ReadOnlySpan<int>(_offsetsPtr, k + 1);
        var vectors = new ReadOnlySpan<byte>(_vecPtr, n * Stride);
        var labels = new ReadOnlySpan<byte>(_labelPtr, n);
        return Knn.CountFraudIvf(query, centroids, offsets, vectors, labels, nprobe);
    }

    public unsafe int CountFraudBruteForce(ReadOnlySpan<byte> query)
    {
        var vectors = new ReadOnlySpan<byte>(_vecPtr, checked((int)(VectorCount * Stride)));
        var labels = new ReadOnlySpan<byte>(_labelPtr, checked((int)VectorCount));
        return Knn.CountFraudInTop5Simd(query, vectors, labels);
    }

    private async Task BuildBlobAsync(string gzPath, string blobPath, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        int hint = configuration.GetValue("EXPECTED_VECTORS", DefaultExpectedVectors);
        int cells = configuration.GetValue("IVF_CELLS", 1024);
        int maxIter = configuration.GetValue("IVF_MAXITER", 10);
        logger.LogInformation("Construindo blob IVF (K={Cells}, maxIter={Iter}) a partir do .gz...", cells, maxIter);

        var vec = new List<byte>(hint * Stride);
        var lab = new List<byte>(hint);
        var qbuf = new byte[Stride];
        await using (var gz = File.OpenRead(gzPath))
        await using (var raw = new GZipStream(gz, CompressionMode.Decompress))
        {
            await foreach (var entry in JsonSerializer.DeserializeAsyncEnumerable(raw, AppJsonContext.Default.ReferenceEntry, ct))
            {
                if (entry is null || entry.Vector.Length != Dim)
                    throw new InvalidDataException($"Entrada de referência inválida (dimensão != {Dim}).");
                Quantizer.Quantize(entry.Vector, qbuf);
                vec.AddRange(qbuf);
                lab.Add(entry.Label == "fraud" ? (byte)1 : (byte)0);
            }
        }
        long count = lab.Count;
        logger.LogInformation("Vetores carregados ({Count:N0}); rodando k-means...", count);

        var ivf = IvfBuilder.Build(CollectionsMarshal.AsSpan(vec), CollectionsMarshal.AsSpan(lab),
            cells, maxIter, seed: 1, log: m => logger.LogInformation("{Msg}", m));

        var tmpPath = blobPath + ".tmp";
        await using (var outFs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            BinaryPrimitives.WriteInt32LittleEndian(header[..4], Magic);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..8], Quantizer.SchemeVersion);
            BinaryPrimitives.WriteInt64LittleEndian(header[8..16], count);
            BinaryPrimitives.WriteInt32LittleEndian(header[16..20], ivf.K);
            BinaryPrimitives.WriteInt32LittleEndian(header[20..24], Stride);
            outFs.Write(header);

            outFs.Write(ivf.CentroidsQ);
            outFs.Write(MemoryMarshal.AsBytes<int>(ivf.CellOffsets));
            outFs.Write(ivf.Vectors);
            outFs.Write(ivf.Labels);
        }
        File.Move(tmpPath, blobPath, overwrite: true);
        sw.Stop();
        logger.LogInformation("Blob IVF construído: {Count:N0} vetores, {Cells} células em {Elapsed}.", count, ivf.K, sw.Elapsed);
    }

    private bool IsBlobValid(string blobPath)
    {
        if (!File.Exists(blobPath)) return false;
        try
        {
            using var fs = File.OpenRead(blobPath);
            Span<byte> header = stackalloc byte[HeaderSize];
            if (fs.Read(header) != HeaderSize) return false;

            int magic = BinaryPrimitives.ReadInt32LittleEndian(header[..4]);
            int version = BinaryPrimitives.ReadInt32LittleEndian(header[4..8]);
            long count = BinaryPrimitives.ReadInt64LittleEndian(header[8..16]);
            int k = BinaryPrimitives.ReadInt32LittleEndian(header[16..20]);
            int stride = BinaryPrimitives.ReadInt32LittleEndian(header[20..24]);

            long expected = HeaderSize + (long)k * Stride + (long)(k + 1) * sizeof(int) + count * Stride + count;
            return magic == Magic && version == Quantizer.SchemeVersion && count > 0 && k > 0
                   && stride == Stride && fs.Length == expected;
        }
        catch
        {
            return false;
        }
    }

    private unsafe void OpenBlob(string blobPath)
    {
        long len = new FileInfo(blobPath).Length;
        _mmf = MemoryMappedFile.CreateFromFile(blobPath, FileMode.Open, mapName: null, 0, MemoryMappedFileAccess.Read);
        _accessor = _mmf.CreateViewAccessor(0, len, MemoryMappedFileAccess.Read);

        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _basePtr = ptr;

        long count = BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(_basePtr + 8, 8));
        int k = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_basePtr + 16, 4));
        VectorCount = count;
        CellCount = k;

        _centPtr = _basePtr + HeaderSize;
        _offsetsPtr = (int*)(_centPtr + (long)k * Stride);
        _vecPtr = (byte*)(_offsetsPtr + (k + 1));
        _labelPtr = _vecPtr + count * Stride;

        long fraud = 0;
        var labels = new ReadOnlySpan<byte>(_labelPtr, checked((int)count));
        for (int i = 0; i < labels.Length; i++) if (labels[i] != 0) fraud++;
        FraudCount = fraud;
        LegitCount = count - fraud;
    }

    private string ResolveResourcesPath()
    {
        var configured = configuration["RESOURCES_PATH"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "resources");
            if (File.Exists(Path.Combine(candidate, "normalization.json"))) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Não encontrei a pasta 'resources' com normalization.json. Defina RESOURCES_PATH ou rode a partir do repositório.");
    }

    public unsafe void Dispose()
    {
        if (_basePtr is not null && _accessor is not null)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _basePtr = null;
        }
        _accessor?.Dispose();
        _mmf?.Dispose();
    }
}
