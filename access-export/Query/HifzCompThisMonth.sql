SELECT HifzCompleted.HifzCompID, HifzCompleted.Date, HifzCompleted.TeacherID, HifzCompleted.StudentID
FROM HifzCompleted
WHERE (((HifzCompleted.Date)>=Forms![Report Date Range]![Beginning Trans Date] And (HifzCompleted.Date)<=Forms![Report Date Range]![Ending Trans Date]));

