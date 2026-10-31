SELECT DISTINCT IIf(IsNull([mobile]),"923336647377","92" & Right([Mobile],10)) AS cellno, Family.FatherName AS v1, Students.Name AS v2, Students.CC AS v3, Students.CurrentClass AS v4, Students.CurrentSection AS v5
FROM Family INNER JOIN Students ON Family.FormID = Students.FamilyID
WHERE (((Students.Status)=True));

