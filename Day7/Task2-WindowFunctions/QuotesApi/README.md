# QuoteRunningCountAndGap.sql

Same QuotesApi app as [Day7/Task1-JoinsAndCTEs/QuotesApi](../../Task1-JoinsAndCTEs/QuotesApi) (no feature changes) — this task is just a SQL script to run against the existing database.

- `ROW_NUMBER() OVER (PARTITION BY Author ORDER BY CreatedAtUtc)` numbers each author's quotes in chronological order, giving a running count (`QuoteNumber`) that resets to 1 for every new author.
- `LAG(CreatedAtUtc) OVER (PARTITION BY Author ORDER BY CreatedAtUtc)` looks back to the previous row within the same author's partition, so `DATEDIFF(day, ..., CreatedAtUtc)` gives the gap in days since that author's previous quote (`DaysSincePrevious`).
- For each author's first quote (`QuoteNumber = 1`), `LAG` has no prior row to return within the partition, so it evaluates to `NULL` and `DaysSincePrevious` is correctly `NULL` rather than a wrong or default value.
