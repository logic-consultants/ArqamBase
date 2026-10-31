SELECT Family.FormID, Family.FatherName, Family.PhHome, Family.PhOffice, Family.Mobile, Family.MotherName, Family.Mcell, Family.EmerNo, Students.CC, Students.[DS#], Students.Name, Students.AdmissionClass, Students.CurrentClass, Students.CurrentSection, Students.Status, Students.Flag, FeeDefinitation.TuitionFee
FROM (Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]) INNER JOIN FeeDefinitation ON Students.[CC] = FeeDefinitation.[FK_CC];

