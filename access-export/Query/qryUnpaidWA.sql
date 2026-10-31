SELECT "92" & Right([Mobile],10) AS CellNo, [UNPAID FeeReceipt].CC, [UNPAID FeeReceipt].Name AS StudentName, [UNPAID FeeReceipt].FatherName AS FatherName, [UNPAID FeeReceipt].ClassNam, Count(*) AS PendingMonths, Sum([UNPAID FeeReceipt].TOTAL) AS Amount, [UNPAID FeeReceipt].Flag
FROM [UNPAID FeeReceipt]
WHERE ((([UNPAID FeeReceipt].DeletedOn)<Date() Or ([UNPAID FeeReceipt].DeletedOn) Is Null))
GROUP BY "92" & Right([Mobile],10), [UNPAID FeeReceipt].CC, [UNPAID FeeReceipt].Name, [UNPAID FeeReceipt].FatherName, [UNPAID FeeReceipt].ClassNam, [UNPAID FeeReceipt].Flag
HAVING (((Count(*))>=[Forms]![frmUnpaidWhatsApp]![txtMonthCount]));

