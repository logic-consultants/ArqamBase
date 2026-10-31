SELECT HifzCompleted.TeacherID, Count(*) AS Total
FROM HifzCompleted
WHERE (((HifzCompleted.Date)>=DateAdd("m",-1,Forms![Report Date Range]![Beginning Trans Date]) And (HifzCompleted.Date)<=Forms![Report Date Range]![Beginning Trans Date]))
GROUP BY HifzCompleted.TeacherID;

