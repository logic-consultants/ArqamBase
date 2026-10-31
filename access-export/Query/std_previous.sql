SELECT Students.[DS#], Students.AdmissionClass, Students.CurrentClass, Students.AdmissionDate, Students.Status
FROM Classes INNER JOIN Students ON Classes.ClassID=Students.CurrentClass
WHERE (((Students.AdmissionDate)<Forms![Report Date Range]![Beginning Trans Date]) And ((Students.Status)=-1));

