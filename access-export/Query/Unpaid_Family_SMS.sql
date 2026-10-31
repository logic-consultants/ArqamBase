SELECT Students.FamilyID, Family.FatherName, Feerecipt.Paid, Feerecipt.Description, Family.Mobile, Students.CC, Students.Status, Students.Name
FROM (Family INNER JOIN Students ON Family.[FormID] = Students.[FamilyID]) INNER JOIN Feerecipt ON Students.[CC] = Feerecipt.[CC]
GROUP BY Students.FamilyID, Family.FatherName, Feerecipt.Paid, Feerecipt.Description, Family.Mobile, Students.CC, Students.Status, Students.Name
HAVING (((Feerecipt.Paid)=False) AND ((Students.Status)=True));

