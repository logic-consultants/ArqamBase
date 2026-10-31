SELECT RoundRpt.[MonthID], RoundRpt.[Sectionvisited], RoundRpt.[ModelLecture], RoundRpt.[BestInmannerClass], RoundRpt.[BestInmannerSec], RoundRpt.[BestinMannerTeacher], RoundRpt.[BestinAcademicClass], RoundRpt.[BestinAcademicSection], RoundRpt.[BestinAcademicTeacher], RoundRpt.[BestinDecorationClass], RoundRpt.[BestinDecorationSection], RoundRpt.[BestinDecorationTeacher], RoundRpt.[BestStaff], RoundRpt.[BeginingDate], RoundRpt.[EndingDate]
FROM RoundRpt
WHERE (((RoundRpt.BeginingDate)>=Forms![Report Date Range]![Beginning Trans Date]) And ((RoundRpt.EndingDate)<=Forms![Report Date Range]![Ending Trans Date]));

