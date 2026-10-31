SELECT Students.CC, Students.Name, Classes.ClassNam, Sections.Section, Feerecipt.Period, Feerecipt.Other, Feerecipt.RecipNo, Feerecipt.Paid, Students.CurrentClass, Family.Mobile
FROM Family INNER JOIN (Sections INNER JOIN (Classes INNER JOIN (Students INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC) ON Classes.ClassID = Students.CurrentClass) ON Sections.SectionID = Students.CurrentSection) ON Family.FormID = Students.FamilyID
WHERE (((Feerecipt.Other)<>0) AND ((Feerecipt.Paid)=False))
ORDER BY Students.CurrentClass;

