SELECT [Total Data query].Name, [Total Data query].FatherName, [Total Data query].cellno, "" AS Email, Classes.ClassNam AS [Group Name], [Total Data query].CC, [Total Data query].FormID
FROM Classes INNER JOIN [Total Data query] ON Classes.ClassID = [Total Data query].CurrentClass
WHERE ((([Total Data query].Status)=True));

