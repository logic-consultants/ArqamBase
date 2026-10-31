INSERT INTO Staff ( FirstName, Type, DateHired, Salary )
SELECT [Teaching staff].Name, [Teaching staff].Desig, [Teaching staff].DOJ, [Teaching staff].Salary
FROM [Teaching staff];

