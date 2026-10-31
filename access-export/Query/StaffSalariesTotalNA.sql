SELECT Sum(Staff.CurrentSalary) AS SalTotal
FROM Staff
WHERE (((Staff.Flag)=-1) AND ((Staff.Type)<=13));

