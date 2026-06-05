using Rinha.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

builder.Services.AddSingleton<ReferenceStore>();

var app = builder.Build();

var store = app.Services.GetRequiredService<ReferenceStore>();
await store.LoadAsync();

app.MapGet("/ready", (ReferenceStore referenceStore) =>
    referenceStore.IsReady ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapPost("/fraud-score", (FraudScoreRequest req, ReferenceStore referenceStore) =>
{
    try
    {
        Span<float> quantizerFloat = stackalloc float[Vectorizer.Dim];
        Vectorizer.Vectorize(req, referenceStore.Normalization, referenceStore.RiskFor, quantizerFloat);
        Span<byte> quantizerByte = stackalloc byte[Vectorizer.Dim];
        Quantizer.Quantize(quantizerFloat, quantizerByte);

        int fraud = referenceStore.CountFraudInTop5(quantizerByte);
        double score = fraud / 5.0;
        return Results.Json(new FraudScoreResponse(score < 0.6, score),
            AppJsonContext.Default.FraudScoreResponse);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Falha ao pontuar transação {Id}; devolvendo fallback seguro.", req.Id);
        return Results.Json(new FraudScoreResponse(Approved: true, FraudScore: 0.0),
            AppJsonContext.Default.FraudScoreResponse);
    }
});

app.Run();
