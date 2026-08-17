CREATE TABLE dbo.QuoteTags (
    QuoteId INT NOT NULL,
    Tag NVARCHAR(50) NOT NULL,
    CONSTRAINT PK_QuoteTags PRIMARY KEY (QuoteId, Tag),
    CONSTRAINT FK_QuoteTags_Quotes FOREIGN KEY (QuoteId) REFERENCES dbo.Quotes (Id)
);

CREATE TABLE dbo.TagCategories (
    Tag NVARCHAR(50) NOT NULL PRIMARY KEY,
    Category NVARCHAR(50) NOT NULL
);
