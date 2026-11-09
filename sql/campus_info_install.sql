/* School details for printouts: the logo and the letterhead header and footer pictures, kept in CampusInfo. Safe to re-run.
   Logo: printed on the drawn letterhead and on ID cards.
   LetterheadHeader / LetterheadFooter: PNG pictures stretched across the top and bottom of certificates.
   When the header is empty, certificates draw their own letterhead (logo, school name, address and phones).
   Set them in the app: Setup › School information (Admin only). */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.CampusInfo', 'Logo') IS NULL
    ALTER TABLE dbo.CampusInfo ADD Logo varbinary(max) NULL;
IF COL_LENGTH('dbo.CampusInfo', 'LetterheadHeader') IS NULL
    ALTER TABLE dbo.CampusInfo ADD LetterheadHeader varbinary(max) NULL;
IF COL_LENGTH('dbo.CampusInfo', 'LetterheadFooter') IS NULL
    ALTER TABLE dbo.CampusInfo ADD LetterheadFooter varbinary(max) NULL;
GO
