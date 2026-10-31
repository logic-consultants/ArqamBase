SELECT Staff.TeachersID, Staff.Type, Staff.DateHired, Staff.Flag, Staff.SLDate
FROM Staff
WHERE (((Staff.Type)>13) AND ((Staff.DateHired)<=[Forms]![Report Date Range]![Ending Trans Date]) AND ((Staff.Flag)=-1)) AND (((Staff.Type)<>19));

