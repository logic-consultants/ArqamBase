UPDATE Feerecipt SET Feerecipt.CollectionType = 1
WHERE (((Feerecipt.CollectionType) Is Null) AND ((Feerecipt.IssueDate)<=#6/25/2021#));

