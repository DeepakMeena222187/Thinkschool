INSERT INTO dbo.TagCategories (Tag, Category)
VALUES
    ('classic', 'Set'),
    ('modern', 'Set'),
    ('motivational', 'Theme'),
    ('technology', 'Theme'),
    ('wisdom', 'Theme');

INSERT INTO dbo.QuoteTags (QuoteId, Tag)
SELECT Id, 'classic' FROM dbo.Quotes WHERE Author = 'Ada Lovelace' AND Text LIKE 'The Analytical Engine has no pretensions%'
UNION ALL
SELECT Id, 'modern' FROM dbo.Quotes WHERE Author = 'Ada Lovelace' AND Text LIKE 'Mathematical science shows%'
UNION ALL
SELECT Id, 'wisdom' FROM dbo.Quotes WHERE Author = 'Ada Lovelace' AND Text LIKE 'That brain of mine%'
UNION ALL
SELECT Id, 'classic' FROM dbo.Quotes WHERE Author = 'Alan Turing' AND Text LIKE 'We can only see a short distance%'
UNION ALL
SELECT Id, 'motivational' FROM dbo.Quotes WHERE Author = 'Alan Turing' AND Text LIKE 'Sometimes it is the people%'
UNION ALL
SELECT Id, 'technology' FROM dbo.Quotes WHERE Author = 'Alan Turing' AND Text LIKE 'A computer would deserve%'
UNION ALL
SELECT Id, 'modern' FROM dbo.Quotes WHERE Author = 'Grace Hopper' AND Text LIKE 'The most dangerous phrase%'
UNION ALL
SELECT Id, 'motivational' FROM dbo.Quotes WHERE Author = 'Grace Hopper' AND Text LIKE 'A ship in port%'
UNION ALL
SELECT Id, 'wisdom' FROM dbo.Quotes WHERE Author = 'Grace Hopper' AND Text LIKE 'One accurate measurement%';
