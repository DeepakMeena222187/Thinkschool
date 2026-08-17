# ThreeSetQueries.sql

Same QuotesApi app as [Day7/Task2-WindowFunctions/QuotesApi](../../Task2-WindowFunctions/QuotesApi) (no feature changes) — this task adds two small standalone tables and three set-operator queries to run against the existing database.

## Schema deviation

The app's real `Quote` model (see [Models/Quote.cs](Models/Quote.cs)) has no tagging concept. `QuoteTags` and `TagCategories` (defined in [CreateTagTables.sql](CreateTagTables.sql), seeded by [SeedTags.sql](SeedTags.sql)) are two small tables added purely for this exercise so there's something to run `EXCEPT`/`INTERSECT`/`UNION` against — they are not wired into the EF Core app, its `DbContext`, or any migration. This is a documented schema deviation, the same way the Day 5 tasks documented deviations from the app's real model.

## Query-by-query

- **Q1 uses `EXCEPT`** because it's a set subtraction: all distinct authors minus the distinct authors who appear in the tagged-quotes set. `EXCEPT` returns exactly "left set, minus anything that also shows up in the right set," which is what "authors with quotes but no tags" means — no join/`NULL`-check gymnastics needed. Katherine Johnson is the author this surfaces, since her one quote has no rows in `QuoteTags`.
- **Q2 uses `INTERSECT`** because the requirement is authors present in *both* the `'classic'`-tagged-author set and the `'modern'`-tagged-author set. `INTERSECT` returns only rows common to both SELECTs, which maps directly onto "in both sets" — an `INNER JOIN` between the two subsets would work too, but `INTERSECT` states the "both sets" intent in one operator instead of a join condition. Ada Lovelace is the only author who lands in both sets (she has a `'classic'`-tagged quote and a separate `'modern'`-tagged quote); Alan Turing has a `'classic'` quote but none tagged `'modern'`, and Grace Hopper has a `'modern'` quote but none tagged `'classic'`, so neither survives the intersection.
- **Q3 uses `UNION` (not `UNION ALL`)** because the two category slices (`'Set'`, `'Theme'`) are already disjoint by tag in this seed data, but the point of the query is a combined *distinct* tag list — if the same tag were ever miscategorized into both buckets, `UNION` collapses it to one row instead of `UNION ALL` producing a duplicate. `UNION`'s implicit dedup is the correct default whenever the result is meant to represent a set of tags rather than a count of rows.
