# AuthorQuoteSummary.sql

Same QuotesApi app as [Day5/Task6-Resilience/QuotesApi](../../../Day5/Task6-Resilience/QuotesApi) (no feature changes) — this task is just a SQL script to run against the existing database.

- Uses a CTE (`RankedQuotes`) with `ROW_NUMBER()` instead of a correlated subquery in the SELECT list because the window function ranks every row in a single pass over `Quotes`, whereas a correlated subquery would re-scan `Quotes` once per outer row (once per author) to find the latest quote.
- `Author` is denormalized directly onto `Quotes` (see [Models/Quote.cs](Models/Quote.cs)) — there is no independent `Authors` table, so every `Author` in `QuoteCounts` is guaranteed a matching `Rn = 1` row in `RankedQuotes`, and the final join is a plain `JOIN`. The zero-quote-author edge case doesn't apply here. Follow-up: if `Authors` existed as its own table, the join would need to start from `Authors` with `LEFT JOIN`s to `QuoteCounts`/`RankedQuotes` so zero-quote authors aren't dropped.
