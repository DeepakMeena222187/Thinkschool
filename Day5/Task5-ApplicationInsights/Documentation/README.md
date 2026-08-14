# Task 5 — Application Insights

KQL query:

```
requests
| where timestamp > ago(30m)
| summarize count(), p50=percentile(duration, 50), p99=percentile(duration, 99) by name
| order by p99 desc
```

Observation:

GET /health had the highest p99 latency at approximately 420 ms, higher than GET /api/collections at approximately 301 ms even though /api/collections performs the database query. GET /health/live remained under 4 ms at p99 because it does not touch the database.
