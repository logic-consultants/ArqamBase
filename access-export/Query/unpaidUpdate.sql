UPDATE Students INNER JOIN Feerecipt ON Students.CC = Feerecipt.CC SET Feerecipt.Fine = [Forms]![Feeupdate]![txtUpdateFine]
WHERE (((Feerecipt.Fine)=0) AND ((Feerecipt.Paid)=False) AND ((Students.Status)=True));

