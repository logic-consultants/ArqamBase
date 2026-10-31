SELECT Students.CC, Students.[DS#], Students.AdmissionDate, Students.AdmissionClass, Students.FamilyID, Students.Name, Family.FatherName, Family.Mobile, Family.CurrentAdress
FROM Classes INNER JOIN (Family INNER JOIN Students ON Family.FormID = Students.FamilyID) ON Classes.ClassID = Students.CurrentClass;

