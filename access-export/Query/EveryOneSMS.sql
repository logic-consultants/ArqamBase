SELECT [Total Data query].cellno, [Total Data query].FatherName AS v1, "" AS v2, "" AS v3, "" AS v4, "" AS v5 INTO (Text;FMT=Delimited;HDR=Yes;DATABASE=X:\data\;) everyone.csv
FROM [Total Data query]
WHERE ((([Total Data query].Mobile) Is Not Null))
GROUP BY [Total Data query].cellno, [Total Data query].FatherName;

