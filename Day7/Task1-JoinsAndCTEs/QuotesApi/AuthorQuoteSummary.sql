WITH RankedQuotes AS (
    SELECT
        q.Author,
        q.Text,
        q.CreatedAtUtc,
        ROW_NUMBER() OVER (PARTITION BY q.Author ORDER BY q.CreatedAtUtc DESC) AS Rn
    FROM dbo.Quotes AS q
),
QuoteCounts AS (
    SELECT
        q.Author,
        COUNT(*) AS QuoteCount
    FROM dbo.Quotes AS q
    GROUP BY q.Author
)
SELECT
    qc.Author,
    qc.QuoteCount,
    rq.Text AS MostRecentQuoteText,
    rq.CreatedAtUtc AS MostRecentQuoteDate
FROM QuoteCounts AS qc
JOIN RankedQuotes AS rq
    ON rq.Author = qc.Author
    AND rq.Rn = 1
ORDER BY QuoteCount DESC;
