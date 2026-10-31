SELECT Students.CC, Students.FamilyID, Students.AdmissionDate, Students.Name, Students.CurrentClass, Students.CurrentSection, Family.FatherName, Students.BirthDate, Students.Status
FROM Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]
WHERE (((Students.Status)=True));

