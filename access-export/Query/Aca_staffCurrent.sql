SELECT Staff.TeachersID, Staff.Type, Staff.DateHired, Staff.Flag, Staff.SLDate
FROM Staff
WHERE (((Staff.Type)>13) And ((Staff.DateHired)<=Forms![Report Date Range]![Ending Trans Date]) And ((Staff.Flag)=-1));

