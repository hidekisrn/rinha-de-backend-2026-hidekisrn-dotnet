# Rinha de Backend 2026 — solução em .NET 10

Detecção de fraude por **busca vetorial (KNN, k=5)** sob restrições severas: **1 CPU / 350 MB** somados de todos os containers, ramping de 1 → 900 req/s contra um dataset de **3.000.000 vetores de 14 dimensões**.

Esta solução combina: **Native AOT**, vetorização **SIMD**, **quantização int8** dos vetores, **memory-mapped file** compartilhado pelas instâncias e **IVF (Inverted File Index)** com k-means para reduzir o custo por consulta de O(N) para ~O(√N).

> **Foco pedagógico.** Implementação feita como exercício de aprendizado dos conceitos do desafio (KNN, ANN, quantização, restrições de CPU/memória, p99). Não é uma corrida pelo topo do ranking.

---

## Resultado

| Métrica | Valor |
|---|---|
| **final_score** | **~+1.900** (variação ~150 entre rodadas) |
| p99 | ~190–270 ms |
| HTTP errors | 0 |
| failure_rate | 0,45% (corte é em 15%) |
| FP / FN | ~133 / 109 em ~53.700 reqs respondidas |
| RAM usada (3 containers) | ~52 MB de 350 MB |
| CPU usada | ~95–100% do limite de 1.0 (gargalo confirmado) |

Trajetória: a força-bruta SIMD (M3) dava `final_score = −6000` (piso absoluto, p99 estourava o timeout). A introdução do IVF foi a virada — **Δ ≈ +7.900 pontos**.

---

## Arquitetura

```
┌──────────┐
│  cliente │
└────┬─────┘
     │ HTTP :9999
     ▼
┌──────────┐   round-robin     ┌────────────────────────┐
│  nginx   ├──────────────────►│ api1  (.NET 10 AOT)    │
│   (LB)   │                   └────────────┬───────────┘
│ 0.20 CPU │                                │
│  50 MB   ├──────────────────►┌────────────▼───────────┐
└──────────┘                   │ api2  (.NET 10 AOT)    │
                               └────────────┬───────────┘
                                            │
                                            ▼
                                  references.q8.bin
                                    (mmap read-only,
                                    compartilhado entre
                                    processos via page cache)
                                    K=1024 células IVF
                                       49 MB no blob
```

- **2 instâncias da API + 1 nginx** (mínimo exigido pela spec).
- **Sem banco de dados.** O índice está no próprio binário (mmap).
- **nginx puro** (round-robin, sem lógica de negócio — proibido pela spec).

### Pipeline de uma requisição

```
POST /fraud-score → vetorizar (14 dims) → quantizar (int8, padding p/ 16B)
                  → IVF: comparar com 1024 centroides
                  → escolher as 4 células mais próximas (nprobe=4)
                  → varrer ~12.000 vetores em SIMD (Vector128 byte)
                  → top-5 vizinhos → fraud_score = fraudes/5
                  → approved = score < 0.6 → response (~200 µs no caso médio)
```

---

## Decisões técnicas principais

| Decisão | Por quê |
|---|---|
| **.NET 10 Minimal API + Native AOT** | Menor footprint de runtime (cabe nos 350 MB), cold start ~instantâneo, sem reflexão (compatível com source-gen JSON). |
| **JSON via source generator** com `[JsonPropertyName]` explícito | snake_case do payload + AOT-safe. Aprendi por bug: `PropertyNamingPolicy` do contexto **não é aplicada** quando inserido em `JsonSerializerOptions(Web)` do Minimal API. |
| **Quantização int8** (mapa linear `[-1,1] → [0,255]`) | Reduz dataset de 168 MB → **42 MB**. Custo medido contra força-bruta float: **0,30%** de decisões diferentes. O sentinela `-1` vira byte 0 — sem branch no laço quente. |
| **Stride de 16 bytes** (14 dims + 2 padding zero) | Permite `Vector128<byte>.Load` sem máscara nem branch. Padding zero não contamina a distância (query também é zero-padded). |
| **Memory-mapped file** read-only | O blob de 49 MB existe **uma vez** na memória física (page cache do SO) compartilhado pelas duas APIs. |
| **IVF (Inverted File Index)** com K=1024, nprobe=4 | k-means no build da imagem; consulta varre só ~12k de 3M vetores (recall ~99,9% vs força-bruta). Escolhido sobre HNSW (mais complexo) e VP-Tree (ganho incerto em D=14). |
| **`DOTNET_gcServer=1`** (Server GC) | Reduziu p99 em ~40 ms vs Workstation GC; alocação por requisição é ~zero (`stackalloc`), então GC influencia pouco, mas o que influencia ajuda. |
| **Fallback "200 com palpite seguro"** em vez de `try/catch` 500 | Erro HTTP tem **peso 5** na pontuação (vs 1 para FP, 3 para FN) — degradar para um palpite custa menos que falhar. |

> O registro completo (ADRs, alternativas avaliadas, números medidos) está em `plano/07-decisoes.md` (local, não versionado).

---

## Como rodar

### Pré-requisitos

- **Docker** com suporte a `linux/amd64` (em Mac arm64 use `--platform linux/amd64`)
- Para o teste de carga: **k6** local (ou usar o `docker-compose.yml` do `test/` no repo oficial do desafio)
- `resources/references.json.gz` baixado do [repo oficial](https://github.com/zanfranceschi/rinha-de-backend-2026)

### Build da imagem

O Dockerfile é multi-stage e faz **três coisas no build**:
1. Compila o binário Native AOT (`linux-x64`)
2. Roda o k-means sobre os 3M vetores (~3,5 min)
3. Empacota só o binário + JSONs + blob na imagem final (188 MB)

```bash
docker build -t rinha-fraud:latest .
```

### Subir o stack

```bash
docker compose up -d
# espera readiness
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:9999/ready  # → 200
```

### Testar (smoke + carga oficial)

```bash
# smoke rápido (5 reqs)
cd <repo-da-spec>
docker compose --profile smoke up

# teste oficial (ramping até 900 req/s)
docker compose --profile test up
# ou se tiver k6 local:
k6 run test/test.js
# resultado em test/results.json
```

### Desenvolvimento local

```bash
# rodar fora do Docker (precisa do .NET 10 SDK)
RESOURCES_PATH="$(pwd)/resources" ASPNETCORE_URLS="http://localhost:9999" \
  dotnet run --project src/Rinha.Api -c Release

# rodar testes (xUnit)
dotnet test
```

> **Atenção:** `dotnet run` em dev usa o `Properties/launchSettings.json` e ignora `ASPNETCORE_URLS`. Em produção (Dockerfile) isso não acontece.

---

## Estrutura do código

```
src/Rinha.Api/
├── Program.cs           Endpoints /ready e /fraud-score, modo "build-blob"
├── Models.cs            DTOs do request/response + JsonSerializerContext (source-gen)
├── Vectorizer.cs        Transformação payload → vetor 14 dims (puro, testável)
├── Quantizer.cs         Mapa linear float → int8
├── Knn.cs               KNN: DistanceSimd, ScanBlock, CountFraudIvf, escalar p/ teste
├── IvfBuilder.cs        k-means + reordenação por célula (build-time)
└── ReferenceStore.cs    Build/carga do blob via mmap; orquestra a query IVF

tests/Rinha.Tests/       xUnit, 15 testes:
├── VectorizerTests      Valida as 14 dims contra os 2 vetores de exemplo da spec
├── QuantizerTests       Monotonicidade, sentinela, extremos
├── KnnTests             Top-5 + contagem de fraudes em dataset pequeno
├── SimdKnnTests         SIMD == escalar (fuzz aleatório 50×2000)
├── IvfTests             nprobe=K reproduz força-bruta bit-a-bit (invariante)
└── *Measurement.cs      Harness de medição (gated por RUN_MEASUREMENT=1)

Dockerfile               Multi-stage AOT + build do blob
docker-compose.yml       2 APIs + nginx, limites 1.0 CPU / 350 MB
nginx.conf               LB round-robin puro, default Alpine
```

---

## O que tentei e **não** deu certo

Documentando os experimentos negativos — eles foram tão valiosos quanto os positivos:

| Tentativa | Resultado | Lição |
|---|---|---|
| Tunar nginx (proxy_buffering off, workers 4096, keepalive 512) | **−555 pontos** | "Best practices" não são universais. Pra respostas <100 bytes em alto volume, defaults Alpine venceram. |
| IVF com K=4096 (próximo do "ótimo teórico" √(nprobe·N)) | −393 pontos | A teoria pura de operações ignora **cache miss** e **branch prediction**. Modelos guiam, medições decidem. |
| Cortar CPU do nginx para dar mais às APIs (0.10/0.45/0.45) | **−1006 pontos** | Métrica importante é o **pico** de CPU, não a média. nginx ia para ~16% sob carga, asfixiou com 0.10. |
| `nprobe=2` (mais agressivo) | −82 pontos | Recall caiu mais do que ganhou em latência. `nprobe=4` foi o ponto ótimo. |

---

## Stack

- **.NET 10** (Minimal API + Native AOT + source-gen JSON)
- **SIMD portável** (`System.Runtime.Intrinsics.Vector128`) — funciona em x64 (SSE/AVX) e arm64 (NEON)
- **k-means** customizado (para construir o índice IVF, no build da imagem)
- **nginx Alpine** como load balancer
- **xUnit** para testes

Tudo embutido no binário AOT — sem dependências externas em runtime, sem banco, sem cache, sem mensageria.

---

## Próximos passos (não-implementados)

- **HNSW** como substituto do IVF — provavelmente o maior salto restante de p99.
- **Re-ranking float dos top-N do IVF** — manter velocidade alta com precisão maior.
- **Reescrita em Rust / Go / Zig** — comparar runtime sem GC sob as mesmas restrições.

---

## Licença

MIT — ver [LICENSE](LICENSE).

## Autor

Sergio Ricardo Hideki Nisikava — [@hidekisrn](https://github.com/hidekisrn)
