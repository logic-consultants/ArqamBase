SELECT Classes.ClassID, Classes.ClassNam, Count(Students.cc) AS CountOfcc
FROM Classes INNER JOIN Students ON Classes.ClassID = Students.CurrentClass
WHERE (((Students.Status)=True))
GROUP BY Classes.ClassID, Classes.ClassNam;

