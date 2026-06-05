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
        Span<float> query = stackalloc float[Vectorizer.Dim];
        Vectorizer.Vectorize(req, referenceStore.Normalization, referenceStore.RiskFor, query);

        int fraud = referenceStore.CountFraudInTop5(query);
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
