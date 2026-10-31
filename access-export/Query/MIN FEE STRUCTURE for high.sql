SELECT Departments.DeptNo, Students.CC, Students.[DS#], Family.FormID, Students.Name, Students.CurrentClass, Students.CurrentSection, Classes.ClassNam, Departments.DepartN, Family.FatherName, Students.AdmissionDate, FeeDefinitation.TuitionFee, Family.Mobile, Family.Mcell, Students.Status
FROM (Family INNER JOIN ((Departments INNER JOIN Classes ON Departments.DeptNo = Classes.DeptNo) INNER JOIN Students ON Classes.ClassID = Students.CurrentClass) ON Family.FormID = Students.FamilyID) INNER JOIN FeeDefinitation ON Students.CC = FeeDefinitation.FK_CC
WHERE (((Students.Status)=True));

