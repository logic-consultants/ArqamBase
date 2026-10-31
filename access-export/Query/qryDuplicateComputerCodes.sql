SELECT Students.CC, Students.Name
FROM Students
WHERE (((Students.CC) In (SELECT [CC] FROM [Students] As Tmp GROUP BY [CC] HAVING Count(*)>1 )))
ORDER BY Students.CC;

