# Assistant evaluation matrix

| Row | Model | Group | Questions | **Accuracy** | Lookup | Aggregate | History | Documents | Drafts | Safety | Taglish | Tool use | p50 s | p95 s | avg in tok | avg out tok | Errors |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| gpt-oss-120b-groq † | openai/gpt-oss-120b | hosted | 20 | **18/20 (90%)** | 4/4 | 2/2 | 1/3 | 2/2 | 3/3 | 5/5 | 1/1 | 18/20 (90%) | 2.6 | 12.1 | 5416 | 320 | 1 |
| gpt-oss-20b-local | gpt-oss:20b | local | 52 | **38/52 (73%)** | 9/12 | 5/6 | 5/8 | 4/5 | 4/8 | 9/10 | 2/3 | 39/52 (75%) | 4.5 | 12.2 | 4466 | 337 | 0 |
| llama3.1-8b-local | llama3.1:8b | local | 52 | **11/52 (21%)** | 2/12 | 1/6 | 0/8 | 0/5 | 0/8 | 7/10 | 1/3 | 39/52 (75%) | 2.0 | 3.7 | 1900 | 74 | 0 |
| qwen3-30b-a3b-local | qwen3:30b-a3b | local | 52 | **41/52 (79%)** | 10/12 | 6/6 | 6/8 | 3/5 | 6/8 | 9/10 | 1/3 | 40/52 (77%) | 28.7 | 86.6 | 5272 | 1797 | 0 |
| qwen3-8b-local | qwen3:8b | local | 52 | **37/52 (71%)** | 9/12 | 6/6 | 4/8 | 3/5 | 4/8 | 9/10 | 2/3 | 38/52 (73%) | 20.2 | 50.3 | 5278 | 1444 | 2 |

† ran the 20-question subset only: its accuracy is not comparable with a full-set row.

- Date: 2026-11-23
- Commit: 4f4820bb
- Hardware (local rows): AMD Radeon RX 6800 XT (Navi 21), 16 GB VRAM; Ryzen 5 5600X, 6 cores / 12 threads; 46 GB RAM
- Temperature: 0
- Limits, gpt-oss-120b-groq: 25 requests/min, 7000 tokens/min
- All rows $0; tokens stand in for cost.
