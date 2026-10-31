SELECT Count(*) AS Total
FROM Students
WHERE (((Students.AdmissionDate)>=DateAdd("m",-1,Forms![Report Date Range]![Beginning Trans Date]) And (Students.AdmissionDate)<=Forms![Report Date Range]![Beginning Trans Date]));

