SELECT DISTINCTROW Family.FormID, Family.FatherName, Family.Mobile, Family.Mcell, First(Students.CC) AS [First Of CC], First(Students.Name) AS [First Of Name], Max(Students.CurrentClass) AS [Max Of CurrentClass], First(Students.CurrentSection) AS [First Of CurrentSection]
FROM Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]
WHERE (((Students.Status)=True))
GROUP BY Family.FormID, Family.FatherName, Family.Mobile, Family.Mcell;

