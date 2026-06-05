using Rinha.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

builder.Services.AddSingleton<ReferenceStore>();

var app = builder.Build();

var store = app.Services.GetRequiredService<ReferenceStore>();
await store.LoadAsync();

app.MapGet("/ready", (ReferenceStore s) =>
    s.IsReady ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapPost("/fraud-score", (FraudScoreRequest _) =>
    Results.Json(new FraudScoreResponse(Approved: true, FraudScore: 0.0),
        AppJsonContext.Default.FraudScoreResponse));

app.Run();
