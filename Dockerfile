FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY src/Rinha.Api/Rinha.Api.csproj src/Rinha.Api/
RUN dotnet restore src/Rinha.Api/Rinha.Api.csproj
COPY src/ src/
RUN dotnet publish src/Rinha.Api/Rinha.Api.csproj -c Release -r linux-x64 \
    -p:PublishAot=true -o /app/publish

COPY resources/normalization.json resources/mcc_risk.json resources/references.json.gz /app/resources/
RUN RESOURCES_PATH=/app/resources /app/publish/Rinha.Api build-blob

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish/Rinha.Api ./
COPY --from=build /app/resources/normalization.json /app/resources/mcc_risk.json /app/resources/references.q8.bin ./resources/

ENV RESOURCES_PATH=/app/resources \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["./Rinha.Api"]
