SELECT [UNPAID FeeReceipt].CC, Sum([UNPAID FeeReceipt].[TOTAL]) AS PendingAmount
FROM [UNPAID FeeReceipt]
GROUP BY [UNPAID FeeReceipt].CC;

