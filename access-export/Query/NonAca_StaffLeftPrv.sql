SELECT Staff.TeachersID, Staff.Type, Staff.DateHired, Staff.Flag, Staff.SLDate
FROM Staff
WHERE (((Staff.Type)>13) And ((Staff.SLDate)>=DateAdd("m",-1,Forms![Report Date Range]![Beginning Trans Date]) And (Staff.SLDate)<=Forms![Report Date Range]![Beginning Trans Date]));

