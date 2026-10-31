SELECT Students.[DS#], Students.AdmissionClass, Students.CurrentClass, Students.AdmissionDate, Students.Status
FROM Classes INNER JOIN (Family INNER JOIN Students ON Family.FormID=Students.FamilyID) ON Classes.ClassID=Students.CurrentClass
WHERE (((Students.AdmissionDate)<=Forms![Report Date Range]![Ending Trans Date]) And ((Students.Status)=-1));

