SELECT Family.FormID, Students.Name, Family.FatherName, Students.CurrentClass, Students.CurrentSection, Family.Mobile, Students.Status
FROM Family INNER JOIN Students ON Family.FormID = Students.FamilyID
WHERE (((Family.Mobile) Is Null) AND ((Students.Status)=True));

