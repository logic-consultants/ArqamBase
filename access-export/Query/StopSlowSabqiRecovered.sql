SELECT Sabqi.SbqiID, Sabqi.StartDate, Sabqi.EndDate, Sabqi.Type, Sabqi.TeacherID, Sabqi.[Stududent ID], Sabqi.Remarks, Sabqi.Recovered, Sabqi.Approved
FROM Sabqi
WHERE (((Sabqi.StartDate)>=Forms![Report Date Range]![Beginning Trans Date]) And ((Sabqi.Recovered)=-1));

