SELECT Count(*) AS Total
FROM Students
WHERE (((Students.SLDate)>=DateAdd("m",-1,Forms![Report Date Range]![Beginning Trans Date]) And (Students.SLDate)<=Forms![Report Date Range]![Beginning Trans Date]) And ((Students.Left)="Left"));

