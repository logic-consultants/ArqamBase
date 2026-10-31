SELECT FeeDefinitation.FK_CC
FROM FeeDefinitation
WHERE (((FeeDefinitation.FK_CC) In (SELECT [FK_CC] FROM [FeeDefinitation] As Tmp GROUP BY [FK_CC] HAVING Count(*)>1 )))
ORDER BY FeeDefinitation.FK_CC;

