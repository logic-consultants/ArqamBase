SELECT Students.[DS#], Students.AdmissionClass, Students.CurrentClass, Students.SLDate, Students.Left AS Transfered
FROM Family INNER JOIN (Classes INNER JOIN Students ON Classes.ClassID=Students.CurrentClass) ON Family.FormID=Students.FamilyID
WHERE (((Students.SLDate)>=Forms![Report Date Range]![Beginning Trans Date] And (Students.SLDate)<=Forms![Report Date Range]![Ending Trans Date]) And ((Students.Left)="Transfer"));

