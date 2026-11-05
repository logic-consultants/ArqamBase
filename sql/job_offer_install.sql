/* Job offer letters. Safe to re-run.
   1. Staff gets the details the letter asks for that were not kept before.
   2. tblJobOfferTemplates holds the wording an Admin has changed: one row for a post, or the row with no post for the
      general letter. A post with no row uses the general letter, and with no general row the school's sample wording. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.Staff', 'Caste') IS NULL
    ALTER TABLE dbo.Staff ADD
        Caste         nvarchar(50) NULL,
        Religion      nvarchar(50) NULL,
        CNIC          nvarchar(20) NULL,
        EmergencyNo   nvarchar(30) NULL,
        StPerk        money        NULL,
        FuelAllowance money        NULL;
GO

IF OBJECT_ID('dbo.tblJobOfferTemplates') IS NULL
BEGIN
    CREATE TABLE dbo.tblJobOfferTemplates (
        TemplateID  int            IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblJobOfferTemplates PRIMARY KEY,
        StaffTypeID int            NULL CONSTRAINT FK_tblJobOfferTemplates_StaffTypes REFERENCES dbo.StaffTypes (ID),
        Heading     nvarchar(100)  NOT NULL,
        CampusLine  nvarchar(100)  NOT NULL,
        Quote       nvarchar(200)  NOT NULL,
        Title       nvarchar(100)  NOT NULL,
        Intro       nvarchar(500)  NOT NULL,
        Terms       nvarchar(max)  NOT NULL,
        Signatory   nvarchar(100)  NOT NULL,
        Acceptance  nvarchar(1000) NOT NULL,
        UpdatedBy   nvarchar(50)   NULL,
        UpdatedOn   datetime       NULL
    );
    -- One letter for each post, and one general letter (no post).
    CREATE UNIQUE INDEX UX_tblJobOfferTemplates_StaffType ON dbo.tblJobOfferTemplates (StaffTypeID);
END
GO
