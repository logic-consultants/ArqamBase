SELECT Family.FormID, Students.CC, Students.AdmissionDate, Students.Name, Family.FatherName, Family.Mobile, Students.CurrentClass, Students.CurrentSection, FeeDefinitation.TuitionFee, Students.SLDate, Students.remarks, Students.ReasonLeftSchool
FROM (Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]) INNER JOIN FeeDefinitation ON Students.[CC] = FeeDefinitation.[FK_CC]
WHERE (((Students.SLDate) Between [Forms]![Classselect]![txtBdate] And [Forms]![Classselect]![txtEdate]));

