PARAMETERS FileName Value;
SELECT [UNPAID FeeReceipt].cellno, [UNPAID FeeReceipt].FatherName AS v1, [UNPAID FeeReceipt].Name AS v2, Count(*) AS v3, "" AS v4, "" AS v5 INTO (Text;FMT=Delimited;HDR=Yes;DATABASE=X:\smslists\;) unpaid.csv
FROM [UNPAID FeeReceipt]
GROUP BY [UNPAID FeeReceipt].cellno, [UNPAID FeeReceipt].FatherName, [UNPAID FeeReceipt].Name, "";

