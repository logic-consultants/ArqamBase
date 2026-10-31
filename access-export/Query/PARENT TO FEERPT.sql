SELECT Family.FormID, Family.FatherName, Students.CC, Students.[DS#], Students.Name, Students.CurrentClass, Students.CurrentSection, Students.Status, Classes.DeptNo, Students.Remarks, Students.AdmissionDate, Students.DeletedOn
FROM Family INNER JOIN (Classes INNER JOIN Students ON Classes.ClassID = Students.CurrentClass) ON Family.FormID = Students.FamilyID
WHERE (((Students.CC) Like "*" & Forms!Ledger!txtFamFilter & "*") And ((Students.Status)=True));

