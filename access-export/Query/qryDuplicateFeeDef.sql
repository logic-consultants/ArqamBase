SELECT [Total Data query].[CC], [Total Data query].[FormID], [Total Data query].[DS#], [Total Data query].[Name], [Total Data query].[FatherName], [Total Data query].[AdmissionDate], [Total Data query].[Total]
FROM [Total Data query]
WHERE ((([Total Data query].[CC]) In (SELECT [CC] FROM [Total Data query] As Tmp GROUP BY [CC] HAVING Count(*)>1 )))
ORDER BY [Total Data query].[CC];

