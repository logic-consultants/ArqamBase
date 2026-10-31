UPDATE qryFeeReceiptCurrentYear SET qryFeeReceiptCurrentYear.ReDate = [Forms]![Ledger]![txtCDate], qryFeeReceiptCurrentYear.ReceivedAmount = [TuitionFee]+[ExamsFee]+[Mis_fee]+[AdmissionFee]+[ProspectusFee]+[Fine]+[Other], qryFeeReceiptCurrentYear.Paid = -1, qryFeeReceiptCurrentYear.DateEdited = Date(), qryFeeReceiptCurrentYear.RecipNo = [Forms]![Ledger]![txtStartNo]
WHERE (((qryFeeReceiptCurrentYear.Paid)=False) And ((qryFeeReceiptCurrentYear.CC)=Forms!Ledger!CC));

