SELECT Students.CC, Students.Name, Family.FatherName, Students.CurrentClass, Students.CurrentSection, Students.status
FROM Family INNER JOIN Students ON Family.FormID = Students.FamilyID
WHERE (((Students.CurrentClass)=Forms!tblAttendanceMaster!cboClassFilter) And ((Students.CurrentSection)=Forms!tblAttendanceMaster!cboSectionFilter) And ((Students.status)=True))
ORDER BY Students.CC;

