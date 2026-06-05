# Submission — Rinha de Backend 2026

Esta branch contém **apenas** os arquivos necessários para a Engine da Rinha executar o
teste de carga. Código-fonte, documentação e infraestrutura de build estão na branch
[`main`](https://github.com/hidekisrn/rinha-de-backend-2026-hidekisrn-dotnet/tree/main).

- **Imagem pública:** [`hidekisrn/rinha-fraud-dotnet:v1`](https://hub.docker.com/r/hidekisrn/rinha-fraud-dotnet) (linux/amd64)
- **Stack:** nginx (LB round-robin) + 2 × .NET 10 Native AOT
- **Limites:** 1.0 CPU + 350 MB no total
- **Endpoint:** `POST /fraud-score` na porta `9999`

```bash
docker compose up -d
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:9999/ready  # → 200
```
