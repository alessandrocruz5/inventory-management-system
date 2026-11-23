# Assistant evaluation matrix

| Row | Model | Group | Questions | **Accuracy** | Lookup | Aggregate | History | Documents | Drafts | Safety | Taglish | Tool use | p50 s | p95 s | avg in tok | avg out tok | Errors |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| gpt-oss-20b-local | gpt-oss:20b | local | 52 | **40/52 (77%)** | 9/12 | 6/6 | 6/8 | 4/5 | 4/8 | 9/10 | 2/3 | 41/52 (79%) | 4.5 | 10.2 | 4441 | 311 | 0 |
| llama3.1-8b-local | llama3.1:8b | local | 52 | **11/52 (21%)** | 2/12 | 1/6 | 0/8 | 0/5 | 0/8 | 7/10 | 1/3 | 39/52 (75%) | 2.1 | 3.8 | 1900 | 73 | 0 |
| qwen3-30b-a3b-local | qwen3:30b-a3b | local | 52 | **40/52 (77%)** | 10/12 | 6/6 | 5/8 | 3/5 | 6/8 | 9/10 | 1/3 | 40/52 (77%) | 30.1 | 100.8 | 5241 | 1806 | 0 |
| qwen3-8b-local | qwen3:8b | local | 52 | **38/52 (73%)** | 9/12 | 6/6 | 4/8 | 4/5 | 4/8 | 9/10 | 2/3 | 40/52 (77%) | 21.0 | 50.8 | 5437 | 1478 | 2 |

- Date: 2026-11-23
- Commit: 4f4820bb
- Hardware (local rows): not given
- Temperature: 0
- Limits, gpt-oss-120b-groq: 25 requests/min, 7000 tokens/min
- All rows $0; tokens stand in for cost.
