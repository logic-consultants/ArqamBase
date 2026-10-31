SELECT DISTINCTROW Family.FormID, Family.FatherName, Students.[DS#], Students.Name, Students.CurrentClass, Students.CurrentSection, Students.Status, Classes.DeptNo, Students.CC
FROM Family INNER JOIN (Classes INNER JOIN Students ON Classes.ClassID = Students.CurrentClass) ON Family.FormID = Students.FamilyID
WHERE (((Students.CurrentClass)=[Forms]![Classselect]![Text0]) AND ((Students.CurrentSection)=[Forms]![Classselect]![Text2]) AND ((Students.Status)=True));

