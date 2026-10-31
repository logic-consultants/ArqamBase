UPDATE Feerecipt SET Feerecipt.Fine = 100
WHERE (((Feerecipt.IssueDate)<#9/30/2012#) And ((Feerecipt.Paid)=0));

