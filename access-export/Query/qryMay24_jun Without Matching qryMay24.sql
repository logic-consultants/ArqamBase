SELECT qryMay24_jun.[Sr#], qryMay24_jun.IssueDate, qryMay24_jun.ReDate, qryMay24_jun.Period, qryMay24_jun.TuitionFee, qryMay24_jun.CC
FROM qryMay24_jun LEFT JOIN qryMay24 ON qryMay24_jun.[TuitionFee] = qryMay24.[TuitionFee]
WHERE (((qryMay24.TuitionFee) Is Null));

