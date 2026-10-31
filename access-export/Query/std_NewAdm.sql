SELECT Students.[DS#], Students.AdmissionClass, Students.CurrentClass, Students.AdmissionDate
FROM Classes INNER JOIN (Family INNER JOIN Students ON Family.FormID=Students.FamilyID) ON Classes.ClassID=Students.CurrentClass
WHERE (((Students.AdmissionDate)>=Forms![Report Date Range]![Beginning Trans Date] And (Students.AdmissionDate)<=Forms![Report Date Range]![Ending Trans Date]));

