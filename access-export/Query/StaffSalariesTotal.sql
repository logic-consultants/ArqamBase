SELECT Sum(Staff.CurrentSalary) AS SumOfCurrentSalary
FROM Staff
WHERE (((Staff.Flag)=-1));

