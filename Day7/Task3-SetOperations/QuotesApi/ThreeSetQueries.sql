-- Q1: Authors with quotes but no tags (EXCEPT)
SELECT DISTINCT Author FROM dbo.Quotes
EXCEPT
SELECT DISTINCT q.Author FROM dbo.Quotes q
JOIN dbo.QuoteTags qt ON qt.QuoteId = q.Id;

-- Q2: Authors in both 'classic' and 'modern' sets (INTERSECT)
SELECT DISTINCT q.Author FROM dbo.Quotes q
JOIN dbo.QuoteTags qt ON qt.QuoteId = q.Id
WHERE qt.Tag = 'classic'
INTERSECT
SELECT DISTINCT q.Author FROM dbo.Quotes q
JOIN dbo.QuoteTags qt ON qt.QuoteId = q.Id
WHERE qt.Tag = 'modern';

-- Q3: Combined distinct tag list across 'Set' and 'Theme' categories (UNION)
SELECT Tag FROM dbo.TagCategories WHERE Category = 'Set'
UNION
SELECT Tag FROM dbo.TagCategories WHERE Category = 'Theme';
