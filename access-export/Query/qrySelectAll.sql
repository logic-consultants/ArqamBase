UPDATE Students SET Students.Flag = -1
WHERE (((Students.CurrentClass)=Forms!frmClassPromote!cboClass) And ((Students.Status)=True));

