SELECT Students.CC AS [Name Prefix], Students.Name AS [First Name], Family.FatherName AS [Last Name], Family.FormID AS [Name Suffix], Students.Birthdate AS Birthday, Students.AdmissionDate AS [Event 1 - Value], Family.[NIC#] AS [Custom Field 1 - Value], Family.CurrentAdress AS [Address 1 - Street], Family.PermanentAddress AS [Address 1 - Extended Address], Family.Mcell AS [Phone 2 - Value], Family.PhHome AS [Phone 3 - Value], Family.PhOffice AS [Phone 4 - Value], "92" & Right([Mobile],10) AS [Phone 1 - Value], Students.sex AS Gender, Classes.ClassNam AS [Custom Field 2- Value], Departments.DepartN AS [Custom Field 3- Value], Students.Remarks AS Notes
FROM (Family INNER JOIN ((Classes INNER JOIN Students ON Classes.ClassID = Students.CurrentClass) INNER JOIN FeeDefinitation ON Students.CC = FeeDefinitation.FK_CC) ON Family.FormID = Students.FamilyID) INNER JOIN Departments ON Classes.DeptNo = Departments.DeptNo
WHERE (((Students.Status)=True))
ORDER BY Students.CC;

