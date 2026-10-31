SELECT Staff.TeachersID, Staff.Type, Staff.DateHired, Staff.Flag, Staff.SLDate
FROM Staff
WHERE (((Staff.Type)>13) AND ((Staff.Flag)=0) AND ((Staff.SLDate)>=#1/1/2008#));

