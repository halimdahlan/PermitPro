-- Adds the global "general / permit_list_description_words" app setting: how many words of a permit's
-- description the permit list shows before cutting it off with "...". Companies without their own row
-- inherit it; a company can override it in its System Settings.
--
-- Optional: without the row the app uses 20 (the previous fixed value). Safe to run more than once.

-- AppSettings has a filtered index, which requires these (sqlcmd turns QUOTED_IDENTIFIER off by default).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @CategoryId uniqueidentifier =
	(SELECT TOP 1 Id FROM AppSettingCategories WHERE Name = 'general' AND CompanyId IS NULL);

IF @CategoryId IS NULL
	PRINT 'Global "general" settings category not found; nothing added.';
ELSE IF EXISTS (SELECT 1 FROM AppSettings WHERE CompanyId IS NULL AND [Key] = 'permit_list_description_words')
	PRINT 'Setting already exists; nothing added.';
ELSE
BEGIN
	INSERT INTO AppSettings (Id, CategoryId, CompanyId, [Key], DisplayName, Value, DataType, IsEncrypted, SortOrder, IsUsingGlobal)
	VALUES (NEWID(), @CategoryId, NULL, 'permit_list_description_words', 'Permit List Description Length (words)', '20', 'number', 0, 7, 0);

	PRINT 'Added global setting permit_list_description_words = 20.';
END
