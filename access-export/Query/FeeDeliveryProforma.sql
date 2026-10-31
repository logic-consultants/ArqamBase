SELECT DISTINCTROW Family.FormID, Family.FatherName, Family.Mobile, Count(Students.CC) AS [First Of CC], First(Students.Name) AS [First Of Name], First(Students.CurrentClass) AS HighestClass, Family.Mobile
FROM Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]
WHERE (((Students.Status)=True))
GROUP BY Family.FormID, Family.FatherName, Family.Mobile, Family.Mobile;

