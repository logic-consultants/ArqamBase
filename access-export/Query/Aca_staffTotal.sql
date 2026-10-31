SELECT Staff.TeachersID, Staff.Type, Staff.DateHired, Staff.Flag, Staff.SLDate
FROM Staff
WHERE (((Staff.Type)>13) AND ((Staff.DateHired)<=[Forms]![Report Date Range]![Ending Trans Date]) AND ((Staff.SLDate)>=#1/1/2008#)) OR (((Staff.SLDate) Is Null));

