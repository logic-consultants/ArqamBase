SELECT Students.Name, Classes.ClassNam, Family.Mcell, Family.Mobile, Family.PhHome, Family.PhOffice, Family.EmerNo
FROM Classes INNER JOIN (Family INNER JOIN Students ON Family.FormID=Students.FamilyID) ON Classes.ClassID=Students.CurrentClass;

