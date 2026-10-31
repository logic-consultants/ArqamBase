SELECT Departments.DepartN, Family.FatherName, Family.CurrentAdress, Students.[DS#], Students.Name, Students.BirthDate, Students.AdmissionDate, Students.Sex, Students.AdmissionClass, Students.CurrentClass, Departments.DeptNo, Students.CC, Students.Status
FROM (Departments INNER JOIN Classes ON Departments.DeptNo = Classes.DeptNo) INNER JOIN (Family INNER JOIN Students ON Family.FormID = Students.FamilyID) ON Classes.ClassID = Students.CurrentClass
WHERE (((Students.Status)=True));

