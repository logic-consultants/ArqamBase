SELECT Students.CC, Students.CurrentClass, Students.CurrentSection, Family.PhHome, Family.PhOffice, Family.Mobile, Family.Mcell, Family.EmerNo, Students.Status
FROM Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]
WHERE (((Students.Status)=True));

