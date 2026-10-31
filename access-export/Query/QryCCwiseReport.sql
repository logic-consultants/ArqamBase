SELECT Students.CC, Students.[DS#], Students.FamilyID, Students.AdmissionDate, Students.Name, Family.FatherName, Family.PhHome, Family.PhOffice, Family.Mobile, Family.Mcell, Family.EmerNo, Students.CurrentClass, Students.CurrentSection
FROM Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]
WHERE (((Students.CC)>1532));

