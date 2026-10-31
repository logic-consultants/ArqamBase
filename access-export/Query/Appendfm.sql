INSERT INTO Staff ( FirstName, FatherName, Education, DateHired, Address, Flag, SLDate, Type )
SELECT Sheet3.Name, Sheet3.[D/O & HName], Sheet3.Qualification, Sheet3.DOJ, Sheet3.Address, Sheet3.Status, Sheet3.SLCDate, Sheet3.Campus
FROM Sheet3;

