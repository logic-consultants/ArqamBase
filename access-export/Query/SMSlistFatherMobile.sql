SELECT DISTINCT IIf(IsNull([mobile]),"923336647377","92" & Right([Mobile],10)) AS cellno, Family.FatherName AS v1, Last(Students.Name) AS LastOfName
FROM Family INNER JOIN Students ON Family.FormID = Students.FamilyID
WHERE (((Students.Status)=True))
GROUP BY IIf(IsNull([mobile]),"923336647377","92" & Right([Mobile],10)), Family.FatherName;

