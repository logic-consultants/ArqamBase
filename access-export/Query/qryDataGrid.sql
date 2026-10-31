SELECT Departments.departn, Classes.DeptNo, Classes.ClassNam, Family.FormID, Family.FatherName, Family.CurrentAdress, Family.PhHome, Family.PhOffice, Family.Mobile, Family.MotherName, Family.Mcell, Family.EmerNo, Students.CC, Students.AdmissionDate, Students.Name, Students.CurrentClass, Students.CurrentSection, Students.WhatsApp, FeeDefinitation.TuitionFee, Students.Status, Students.Remarks
FROM (Family INNER JOIN ((Classes INNER JOIN Departments ON Classes.DeptNo = Departments.DeptNo) INNER JOIN Students ON Classes.ClassID = Students.CurrentClass) ON Family.FormID = Students.FamilyID) INNER JOIN FeeDefinitation ON Students.CC = FeeDefinitation.FK_CC
WHERE (((Students.Status)=True));

