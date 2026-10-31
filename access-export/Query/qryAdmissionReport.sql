SELECT Family.FormID, Students.CC, Students.AdmissionDate, Students.Name, Family.FatherName, Family.Mobile, Students.CurrentClass, Students.CurrentSection, FeeDefinitation.TuitionFee
FROM (Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]) INNER JOIN FeeDefinitation ON Students.[CC] = FeeDefinitation.[FK_CC]
WHERE (((Students.AdmissionDate) Between [Forms]![Classselect]![txtBdate] And [Forms]![Classselect]![txtEdate]));

