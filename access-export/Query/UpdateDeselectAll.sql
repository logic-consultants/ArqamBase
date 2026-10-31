UPDATE Students SET Students.Flag = 0
WHERE (((Students.CurrentClass)=Forms!frmClassPromote!cboClass) And ((Students.Status)=True));

