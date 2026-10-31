SELECT Sum([CurrentSalary]) AS SalTotal
FROM Staff
WHERE (((Staff.Type)>13) AND ((Staff.Flag)=-1));

