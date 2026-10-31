SELECT Students.CC, Students.Name, Family.FatherName, Students.CurrentClass, Students.Status
FROM Family INNER JOIN Students ON Family.FormID = Students.FamilyID
WHERE (((Students.CurrentClass)<4) AND ((Students.Status)=True));

