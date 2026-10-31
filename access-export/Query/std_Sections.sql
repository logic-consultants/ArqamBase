SELECT ClassSections.CurrentClass, Count(ClassSections.CurrentSection) AS Sections
FROM ClassSections
GROUP BY ClassSections.CurrentClass;

