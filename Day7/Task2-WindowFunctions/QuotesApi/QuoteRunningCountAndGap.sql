SELECT
    q.Author,
    q.Text,
    q.CreatedAtUtc,
    ROW_NUMBER() OVER (PARTITION BY q.Author ORDER BY q.CreatedAtUtc) AS QuoteNumber,
    DATEDIFF(
        day,
        LAG(q.CreatedAtUtc) OVER (PARTITION BY q.Author ORDER BY q.CreatedAtUtc),
        q.CreatedAtUtc
    ) AS DaysSincePrevious
FROM dbo.Quotes AS q
ORDER BY q.Author, q.CreatedAtUtc;
