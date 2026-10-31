SELECT Students.[DS#], Students.AdmissionClass, Students.PreviousClass, Students.PromotionDate, Students.CurrentClass
FROM Classes INNER JOIN (Family INNER JOIN Students ON Family.FormID=Students.FamilyID) ON Classes.ClassID=Students.CurrentClass
WHERE (((Students.PromotionDate)>=Forms![Report Date Range]![Beginning Trans Date] And (Students.PromotionDate)<=Forms![Report Date Range]![Ending Trans Date]));

