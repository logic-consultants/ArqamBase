PARAMETERS Updto DateTime;
SELECT "92" & Right([Mobile],10) AS [Mobile Number], [UNPAID FeeReceipt].CC, [UNPAID FeeReceipt].Name AS [First Name], [UNPAID FeeReceipt].FatherName AS [Father Name], [UNPAID FeeReceipt].DeletedOn, "Unpaid" AS [Group], Count(*) AS [Pending Months], "" AS [Optional Field 4], Sum([UNPAID FeeReceipt].TOTAL) AS SumOfTOTAL, [UNPAID FeeReceipt].ClassNam
FROM [UNPAID FeeReceipt]
WHERE ((([UNPAID FeeReceipt].IssueDate)<[Updto]))
GROUP BY "92" & Right([Mobile],10), [UNPAID FeeReceipt].CC, [UNPAID FeeReceipt].Name, [UNPAID FeeReceipt].FatherName, [UNPAID FeeReceipt].DeletedOn, [UNPAID FeeReceipt].ClassNam
HAVING ((([UNPAID FeeReceipt].DeletedOn)<Date() Or ([UNPAID FeeReceipt].DeletedOn) Is Null));

