UPDATE Students SET Students.CurrentClass = [Forms]![frmClassPromote]![cboPromotedto], Students.PromotionDate = Date()
WHERE (((Students.CurrentClass)=Forms!frmClassPromote!cboClass) And ((Students.Flag)=True) And ((Students.Status)=True));

