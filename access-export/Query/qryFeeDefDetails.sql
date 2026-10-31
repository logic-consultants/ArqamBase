SELECT Students.Status, Feedefinition.FK_CC, Students.admissiondate, *
FROM Students INNER JOIN Feedefinition ON Students.CC = Feedefinition.FK_CC
WHERE (((Students.Status)=True) AND ((Feedefinition.FK_CC)<2591) AND ((Students.admissiondate)<#1/1/2026#));

