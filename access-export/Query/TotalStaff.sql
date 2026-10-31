SELECT Count(*) AS Total
FROM Staff
WHERE (((Staff.Flag)=-1) AND ((Staff.DateHired)<=[Forms]![Report Date Range]![Beginning Trans Date]));

