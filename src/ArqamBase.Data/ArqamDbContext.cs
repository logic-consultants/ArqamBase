using Microsoft.EntityFrameworkCore;
using Microsoft.Win32.SafeHandles;

namespace ArqamBase.Data;

public class ArqamDbContext(DbContextOptions<ArqamDbContext> options) : DbContext(options)
{
    public DbSet<School> Schools => Set<School>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<ConversationType> ConversationTypes => Set<ConversationType>();
    public DbSet<AttendanceStatus> AttendanceStatuses => Set<AttendanceStatus>();
    public DbSet<CalendarDay> Calendar => Set<CalendarDay>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Family> Families => Set<Family>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<BloodGroup> BloodGroups => Set<BloodGroup>();
    public DbSet<StudentDocument> StudentDocuments => Set<StudentDocument>();
    public DbSet<Campus> Campuses => Set<Campus>();
    public DbSet<FeeChallan> FeeChallans => Set<FeeChallan>();
    public DbSet<CollectionType> CollectionTypes => Set<CollectionType>();
    public DbSet<FeeReceiptFile> FeeReceiptFiles => Set<FeeReceiptFile>();
    public DbSet<FeeDefinition> FeeDefinitions => Set<FeeDefinition>();
    public DbSet<FeeIncrease> FeeIncreases => Set<FeeIncrease>();
    public DbSet<AttendanceRegister> AttendanceRegisters => Set<AttendanceRegister>();
    public DbSet<StudentAttendance> StudentAttendance => Set<StudentAttendance>();
    public DbSet<StaffMember> Staff => Set<StaffMember>();
    public DbSet<StaffType> StaffTypes => Set<StaffType>();
    public DbSet<StaffAttendance> StaffAttendance => Set<StaffAttendance>();
    public DbSet<StaffSchedule> StaffSchedules => Set<StaffSchedule>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<MessageLog> MessageLogs => Set<MessageLog>();
    public DbSet<GoogleOAuth> GoogleOAuth => Set<GoogleOAuth>();
    public DbSet<UserScope> UserScopes => Set<UserScope>();
    public DbSet<SalaryHead> SalaryHeads => Set<SalaryHead>();
    public DbSet<SalaryLine> SalaryLines => Set<SalaryLine>();
    public DbSet<StaffAdvance> StaffAdvances => Set<StaffAdvance>();
    public DbSet<StaffDocument> StaffDocuments => Set<StaffDocument>();
    public DbSet<JobOfferTemplate> JobOfferTemplates => Set<JobOfferTemplate>();

    /// <summary>
    /// Fee owed per student (CC → amount): the total of the student's unpaid challans in Feerecipt, as the Access ledger's "Unpaid TOTAL".
    /// </summary>
    public Dictionary<int, int> UnpaidBalances() =>
        Database.SqlQuery<StudentBalance>($"""
            SELECT CC AS StudentId,
                   SUM(ISNULL(TuitionFee, 0) + ISNULL(ExamsFee, 0) + ISNULL(Mis_fee, 0) + ISNULL(AdmissionFee, 0)
                       + ISNULL(Fine, 0) + ISNULL(Other, 0) + ISNULL(ProspectusFee, 0)) AS Balance
            FROM dbo.Feerecipt
            WHERE Paid = 0 AND ISNULL(isDeleted, 0) = 0 AND CC IS NOT NULL
            GROUP BY CC
            """)
            .ToDictionary(b => b.StudentId, b => b.Balance);

    /// <summary>
    /// Monthly fee per student (CC → tuition fee) from the fee definition (FeeDefinitation). If a student has more than one, the newest counts.
    /// </summary>
    public Dictionary<int, int> MonthlyFees() =>
        Database.SqlQuery<StudentBalance>($"""
            SELECT FK_CC AS StudentId, ISNULL(TuitionFee, 0) AS Balance
            FROM (SELECT FK_CC, TuitionFee, ROW_NUMBER() OVER (PARTITION BY FK_CC ORDER BY FeeDefId DESC) AS N
                  FROM dbo.FeeDefinitation WHERE ISNULL(isDeleted, 0) = 0 AND FK_CC IS NOT NULL) d
            WHERE N = 1
            """)
            .ToDictionary(b => b.StudentId, b => b.Balance);

    /// <summary>One student's monthly fee from the newest fee definition (null if none).</summary>
    public int? MonthlyFeeOf(int cc) =>
        Database.SqlQuery<int?>($"""
            SELECT TOP 1 TuitionFee AS Value FROM dbo.FeeDefinitation
            WHERE FK_CC = {cc} AND ISNULL(isDeleted, 0) = 0 ORDER BY FeeDefId DESC
            """).AsEnumerable().FirstOrDefault();

    /// <summary>Who last changed a row and when, from the audit log (null if never changed or the audit log is not installed).</summary>
    public LastChange? LastChangeOf(string table, int key) =>
        Database.SqlQuery<LastChange>($"""
            SELECT TOP 1 COALESCE(WindowsUser, SqlLogin) AS ChangedBy, ChangedAt
            FROM audit.AuditLog WHERE TableName = {table} AND KeyValue = {key}
            ORDER BY ChangedAt DESC
            """).AsEnumerable().FirstOrDefault();

    /// <summary>A student's CC and an amount (used by the queries above).</summary>
    private sealed class StudentBalance
    {
        public int StudentId { get; set; }
        public int Balance { get; set; }
    }

    /// <summary>
    /// Creates a context. Connections are opened with the given Windows credentials (when a token is passed)
    /// and tell SQL Server which app user is working, for the audit triggers.
    /// </summary>
    public static ArqamDbContext Create(string connectionString, string appUser, SafeAccessTokenHandle? windowsToken = null) =>
        new(new DbContextOptionsBuilder<ArqamDbContext>()
            .UseSqlServer(connectionString)
            .AddInterceptors(new SessionContextInterceptor(appUser, windowsToken))
            .Options);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<School>(e =>
        {
            e.ToTable("Branches");
            e.Property(x => x.Id).HasColumnName("BrID");
            e.Property(x => x.Code).HasColumnName("BrCode").HasMaxLength(50);
            e.Property(x => x.Name).HasColumnName("Address").HasMaxLength(150);
            MapSoftDelete(e);
        });

        b.Entity<Department>(e =>
        {
            e.ToTable("Departments");
            e.Property(x => x.Id).HasColumnName("DeptNo");
            e.Property(x => x.Name).HasColumnName("DepartN").HasMaxLength(50);
            e.Property(x => x.SchoolId).HasColumnName("Br_ID");
            e.Property(x => x.AccountNumber).HasMaxLength(255);
            e.Property(x => x.CampusDivision).HasMaxLength(255);
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            MapSoftDelete(e);
        });

        b.Entity<SchoolClass>(e =>
        {
            e.ToTable("Classes");
            e.Property(x => x.Id).HasColumnName("ClassID");
            e.Property(x => x.Name).HasColumnName("ClassNam").HasMaxLength(11);
            e.Property(x => x.DepartmentId).HasColumnName("DeptNo");
            e.Property(x => x.SortName).HasColumnName("ClassFltr").HasMaxLength(255);
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            MapSoftDelete(e);
        });

        b.Entity<Section>(e =>
        {
            e.ToTable("Sections");
            e.Property(x => x.Id).HasColumnName("SectionID");
            e.Property(x => x.Name).HasColumnName("Section").HasMaxLength(50);
            e.Property(x => x.ClassId).HasColumnName("ClassID");
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            MapSoftDelete(e);
        });

        b.Entity<ConversationType>(e =>
        {
            e.ToTable("tblConvoTypes");
            e.Property(x => x.Id).HasColumnName("ConvoTypeID");
            e.Property(x => x.Name).HasColumnName("ConvoType").HasMaxLength(255);
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            MapSoftDelete(e);
        });

        b.Entity<AttendanceStatus>(e =>
        {
            e.ToTable("tblAttendanceStatus");
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasColumnName("StatusCode").HasColumnType("char(1)");
            e.Property(x => x.Name).HasColumnName("StatusName").HasMaxLength(30);
        });

        b.Entity<AttendanceRegister>(e =>
        {
            e.ToTable("tblAttendanceRegister", t => t.HasTrigger("trg_Audit_tblAttendanceRegister"));
            e.Property(x => x.Id).HasColumnName("RegisterID");
            e.Property(x => x.Date).HasColumnName("AttDate").HasColumnType("date");
            e.Property(x => x.TakenBy).HasMaxLength(128);
            e.Property(x => x.TakenOn).HasColumnType("datetime2(0)");
            e.Property(x => x.Remarks).HasMaxLength(500);
            e.HasIndex(x => new { x.Date, x.ClassId, x.SectionId }).IsUnique();
            e.HasMany(x => x.Marks).WithOne().HasForeignKey(x => x.RegisterId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StudentAttendance>(e =>
        {
            e.ToTable("tblStudentAttendance", t => t.HasTrigger("trg_Audit_tblStudentAttendance"));
            e.Property(x => x.Id).HasColumnName("AttendanceID");
            e.Property(x => x.StudentId).HasColumnName("CC");
            e.Property(x => x.Status).HasColumnName("StatusCode").HasColumnType("char(1)");
            e.Property(x => x.Remarks).HasMaxLength(500);
        });

        b.Entity<StaffMember>(e =>
        {
            e.ToTable("Staff", t => t.HasTrigger("trg_Audit_Staff"));
            e.Property(x => x.Id).HasColumnName("TeachersID");
            e.Property(x => x.Salute).HasMaxLength(255);
            e.Property(x => x.Name).HasColumnName("FirstName").HasMaxLength(50);
            e.Property(x => x.NameUrdu).HasColumnName("StaffNameUrdu").HasMaxLength(255);
            e.Property(x => x.FatherName).HasMaxLength(50);
            e.Property(x => x.TypeId).HasColumnName("Type");
            e.Property(x => x.Caste).HasMaxLength(50);
            e.Property(x => x.Religion).HasMaxLength(50);
            e.Property(x => x.Cnic).HasColumnName("CNIC").HasMaxLength(20);
            e.Property(x => x.EmergencyNo).HasMaxLength(30);
            e.Property(x => x.StPerk).HasColumnType("money");
            e.Property(x => x.FuelAllowance).HasColumnType("money");
            e.Property(x => x.Designation).HasMaxLength(50);
            e.Property(x => x.Address).HasMaxLength(255);
            e.Property(x => x.HomePhone).HasMaxLength(10);
            e.Property(x => x.CellNo).HasMaxLength(18);
            e.Property(x => x.Email).HasMaxLength(100);
            e.Property(x => x.BirthDate).HasColumnName("Birthdate");
            e.Property(x => x.Education).HasMaxLength(50);
            e.Property(x => x.Subjects).HasMaxLength(50);
            e.Property(x => x.MaritalStatus).HasMaxLength(50);
            e.Property(x => x.Salary).HasColumnType("money");
            e.Property(x => x.LastIncrement).HasColumnType("money");
            e.Property(x => x.SecDeduction).HasColumnType("money");
            e.Property(x => x.Scf).HasColumnName("SCF").HasColumnType("money");
            e.Property(x => x.BranchId).HasColumnName("BranchID");
            e.Property(x => x.PayMethod).HasMaxLength(20);
            e.Property(x => x.BankAccount).HasMaxLength(50);
            e.Property(x => x.LeftOn).HasColumnName("SLDate");
            e.Property(x => x.LeftReason).HasColumnName("Reason").HasMaxLength(50);
            e.Property(x => x.InterviewRemarks).HasColumnName("RemarksofInterview").HasMaxLength(255);
            e.Property(x => x.Reference).HasMaxLength(255);
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            MapSoftDelete(e);
        });

        b.Entity<SalaryHead>(e =>
        {
            e.ToTable("tblSalariesHead");
            e.Property(x => x.Id).HasColumnName("SalHeadID");
            e.Property(x => x.Period).HasMaxLength(6);
            e.Property(x => x.BankChequeNo).HasMaxLength(30);
            e.Property(x => x.CashChequeNo).HasMaxLength(30);
            e.Property(x => x.PreparedBy).HasMaxLength(128);
            e.Property(x => x.PreparedOn).HasColumnType("datetime2(0)");
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.HeadId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SalaryLine>(e =>
        {
            e.ToTable("tblSalaries");
            e.Property(x => x.Id).HasColumnName("SalID");
            e.Property(x => x.HeadId).HasColumnName("fk_SalHeadID");
            e.Property(x => x.StaffId).HasColumnName("fk_EmpID");
            e.Property(x => x.SalaryDate).HasColumnName("SalDate");
            e.Property(x => x.Sheet).HasColumnName("SalarySheet");
            e.Property(x => x.BasicSalary).HasColumnType("money");
            e.Property(x => x.AbsentLeave).HasColumnName("L/Abs");
            e.Property(x => x.Incentive).HasColumnType("money");
            e.Property(x => x.Deduction).HasColumnType("money");
            e.Property(x => x.Scf).HasColumnName("SCF").HasColumnType("money");
            e.Property(x => x.Security).HasColumnType("money");
            e.Property(x => x.AmountPaid).HasColumnType("money");
            e.Property(x => x.Advance).HasColumnType("money");
            e.Property(x => x.AdvanceDeducted).HasColumnType("money");
            e.Property(x => x.AdvanceBalance).HasColumnName("Adv_Bal").HasColumnType("money");
            e.Property(x => x.PayMethod).HasColumnName("Bank").HasMaxLength(255);
            e.Property(x => x.BankAccount).HasMaxLength(255);
            e.Property(x => x.RemainingSecurity).HasMaxLength(20);
            e.Property(x => x.Verify).HasMaxLength(30);
        });

        b.Entity<JobOfferTemplate>(e =>
        {
            e.ToTable("tblJobOfferTemplates");
            e.Property(x => x.Id).HasColumnName("TemplateID");
            e.Property(x => x.StaffTypeId).HasColumnName("StaffTypeID");
            e.Property(x => x.UpdatedOn).HasColumnType("datetime");
        });

        b.Entity<StaffDocument>(e =>
        {
            e.ToTable("tblStaffDocuments");
            e.Property(x => x.Id).HasColumnName("DocID");
            e.Property(x => x.StaffId).HasColumnName("TeachersID");
            e.Property(x => x.DocType).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.FilePath).HasMaxLength(400);
            e.Property(x => x.OriginalFileName).HasMaxLength(260);
            e.Property(x => x.Notes).HasMaxLength(255);
            e.Property(x => x.CreatedBy).HasMaxLength(50);
            e.Property(x => x.DeletedBy).HasMaxLength(50);
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            e.Ignore(x => x.TypeText);
            e.Ignore(x => x.FileName);
            e.HasOne<StaffMember>().WithMany().HasForeignKey(x => x.StaffId);
            MapSoftDelete(e);
        });

        b.Entity<StaffAdvance>(e =>
        {
            e.ToTable("tblStaffAdvance");
            e.Property(x => x.Id).HasColumnName("AdvanceID");
            e.Property(x => x.Date).HasColumnName("AdvanceDate").HasColumnType("date");
            e.Property(x => x.StaffId).HasColumnName("TeachersID");
            e.Property(x => x.Amount).HasColumnType("money");
            e.Property(x => x.FirstPeriod).HasMaxLength(6);
            e.Property(x => x.Witness1).HasMaxLength(100);
            e.Property(x => x.Witness2).HasMaxLength(100);
            e.Property(x => x.Witness3).HasMaxLength(100);
            e.Property(x => x.PaidThrough).HasMaxLength(100);
            e.Property(x => x.RecommendedBy).HasMaxLength(100);
            e.Property(x => x.ApprovedBy).HasMaxLength(100);
            e.Property(x => x.Remarks).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(128);
            e.Ignore(x => x.Installment);
            MapSoftDelete(e);
        });

        b.Entity<StaffType>(e =>
        {
            e.ToTable("StaffTypes");
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.Kind).HasColumnName("Type");
            e.Property(x => x.Name).HasColumnName("Detail");
            MapSoftDelete(e);
        });

        b.Entity<MessageTemplate>(e =>
        {
            e.ToTable("tblMessageTemplates");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasColumnName("TemplateKey").HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.UpdatedBy).HasMaxLength(128);
            e.Property(x => x.UpdatedOn).HasColumnType("datetime2(0)");
        });

        b.Entity<GoogleOAuth>(e =>
        {
            e.ToTable("tblGoogleOAuth");
            e.Property(x => x.Id).HasColumnName("OAuthID").ValueGeneratedNever();
            e.Property(x => x.GoogleAccount).HasMaxLength(255);
            e.Property(x => x.ClientId).HasColumnName("ClientID").HasMaxLength(255);
            e.Property(x => x.TokenType).HasMaxLength(255);
            e.Property(x => x.ExpiresAt).HasColumnType("datetime");
            e.Property(x => x.LastUpdated).HasColumnType("datetime");
        });

        b.Entity<MessageLog>(e =>
        {
            e.ToTable("tblMessageLog");
            e.Property(x => x.Id).HasColumnName("LogID");
            e.Property(x => x.SentOn).HasColumnType("datetime2(0)");
            e.Property(x => x.SentBy).HasMaxLength(128);
            e.Property(x => x.Kind).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.RefKey).HasMaxLength(50);
            e.Property(x => x.PersonId).HasColumnName("PersonID");
            e.Property(x => x.PersonName).HasMaxLength(150);
            e.Property(x => x.Channel).HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.SentTo).HasMaxLength(200);
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.Detail).HasMaxLength(500);
        });

        b.Entity<StaffSchedule>(e =>
        {
            e.ToTable("tblStaffSchedule");
            e.Property(x => x.Id).HasColumnName("ScheduleID");
            e.Property(x => x.Name).HasColumnName("ScheduleName").HasMaxLength(50);
            e.Property(x => x.CheckIn).HasColumnType("time(0)");
            e.Property(x => x.CheckOut).HasColumnType("time(0)");
            e.Property(x => x.From).HasColumnName("EffectiveFrom").HasColumnType("date");
            e.Property(x => x.To).HasColumnName("EffectiveTo").HasColumnType("date");
            e.Property(x => x.UpdatedBy).HasMaxLength(128);
            e.Property(x => x.UpdatedOn).HasColumnType("datetime2(0)");
        });

        b.Entity<StaffAttendance>(e =>
        {
            e.ToTable("tblStaffAttendance");
            e.Property(x => x.Id).HasColumnName("StaffAttendanceID");
            e.Property(x => x.Date).HasColumnName("AttDate").HasColumnType("date");
            e.Property(x => x.StaffId).HasColumnName("TeachersID");
            e.Property(x => x.Status).HasColumnName("StatusCode").HasColumnType("char(1)");
            e.Property(x => x.TimeIn).HasColumnType("time(0)");
            e.Property(x => x.TimeOut).HasColumnType("time(0)");
            e.Property(x => x.Remarks).HasMaxLength(500);
            e.Property(x => x.TakenBy).HasMaxLength(128);
            e.Property(x => x.TakenOn).HasColumnType("datetime2(0)");
            e.HasIndex(x => new { x.Date, x.StaffId }).IsUnique();
        });

        b.Entity<UserScope>(e =>
        {
            e.ToTable("tblUserScope");
            e.Property(x => x.Id).HasColumnName("ScopeID");
            e.Property(x => x.UserName).HasMaxLength(50);
            e.Property(x => x.DepartmentId).HasColumnName("DepartmentID");
            e.Property(x => x.ClassId).HasColumnName("ClassID");
            e.Property(x => x.SectionId).HasColumnName("SectionID");
        });

        b.Entity<CalendarDay>(e =>
        {
            e.ToTable("tblSchoolCalendar");
            e.HasKey(x => x.Date);
            e.Property(x => x.Date).HasColumnName("CalDate").HasColumnType("date");
            e.Property(x => x.Reason).HasMaxLength(200);
        });

        b.Entity<AppUser>(e =>
        {
            e.ToTable("AppUsers");
            e.HasKey(x => x.UserName);
            e.Property(x => x.UserName).HasMaxLength(50);
            e.Property(x => x.FullName).HasMaxLength(100);
            e.Property(x => x.RoleName).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.PasswordHash).HasMaxLength(32);
            e.Property(x => x.PasswordSalt).HasMaxLength(16);
            e.Property(x => x.LockedUntil).HasColumnType("datetime2(0)");
            e.Property(x => x.LastSignIn).HasColumnType("datetime2(0)");
            e.Ignore(x => x.Role);
            e.Ignore(x => x.HasPassword);
            e.Ignore(x => x.IsLocked);
            e.Ignore(x => x.PasswordState);
        });

        MapStudents(b);
    }

    private static void MapStudents(ModelBuilder b)
    {
        b.Entity<Student>(e =>
        {
            e.ToTable("Students", t => t.HasTrigger("trg_Audit_Students"));
            e.Property(x => x.Id).HasColumnName("CC");
            e.Property(x => x.DsNo).HasColumnName("DS#");
            e.Property(x => x.FamilyId).HasColumnName("FamilyID");
            e.Property(x => x.Name).HasMaxLength(255);
            e.Property(x => x.NameUrdu).HasColumnName("StudentNameUrdu").HasMaxLength(255);
            e.Property(x => x.Sex).HasMaxLength(255);
            e.Property(x => x.BirthDate).HasColumnType("datetime");
            e.Property(x => x.BirthPlace).HasMaxLength(255);
            e.Property(x => x.FormB).HasMaxLength(50);
            e.Property(x => x.BloodGroupId).HasColumnName("fk_BloodGroup").HasConversion<double?>();
            e.Property(x => x.AdmissionDate).HasColumnType("datetime");
            e.Property(x => x.AdmissionClass).HasMaxLength(255);
            e.Property(x => x.ClassId).HasColumnName("CurrentClass");
            e.Property(x => x.SectionId).HasColumnName("CurrentSection");
            e.Property(x => x.OnRoll).HasColumnName("Status");
            e.Property(x => x.LeavingType).HasColumnName("Left").HasMaxLength(255);
            e.Property(x => x.LeavingDate).HasColumnName("SLDate").HasColumnType("date");
            e.Property(x => x.LeavingReason).HasColumnName("ReasonLeftSchool").HasMaxLength(255);
            e.Property(x => x.PromotionDate).HasMaxLength(255);
            e.Property(x => x.PicturePath).HasColumnName("Picture");
            e.Property(x => x.Fee).HasColumnType("money");
            e.Property(x => x.Email).HasMaxLength(255);
            e.Property(x => x.WhatsApp).HasMaxLength(255);
            e.Property(x => x.Remarks).HasMaxLength(255);
            e.Ignore(x => x.FatherName);
            e.Ignore(x => x.Mobile);
            e.Ignore(x => x.MotherMobile);
            e.Ignore(x => x.EmergencyNo);
            e.Ignore(x => x.StatusText);
            e.Ignore(x => x.StatusChoice);
            e.HasOne(x => x.Family).WithMany(f => f.Students).HasForeignKey(x => x.FamilyId).IsRequired(false);
            e.HasMany(x => x.Conversations).WithOne().HasForeignKey(c => c.StudentId).IsRequired(false);
            MapSoftDelete(e);
        });

        b.Entity<Family>(e =>
        {
            e.ToTable("Family", t => t.HasTrigger("trg_Audit_Family"));
            e.Property(x => x.Id).HasColumnName("FormID");
            e.Property(x => x.FatherName).HasMaxLength(255);
            e.Property(x => x.FatherNameUrdu).HasMaxLength(255);
            e.Property(x => x.Cnic).HasColumnName("NIC#").HasMaxLength(255);
            e.Property(x => x.Caste).HasMaxLength(255);
            e.Property(x => x.FatherEducation).HasColumnName("FathersEdu").HasMaxLength(255);
            e.Property(x => x.Profession).HasMaxLength(255);
            e.Property(x => x.BusinessName).HasMaxLength(255);
            e.Property(x => x.Mobile).HasMaxLength(255);
            e.Property(x => x.PhoneHome).HasColumnName("PhHome").HasMaxLength(255);
            e.Property(x => x.PhoneOffice).HasColumnName("PhOffice").HasMaxLength(255);
            e.Property(x => x.MotherName).HasMaxLength(255);
            e.Property(x => x.MotherMobile).HasColumnName("Mcell").HasMaxLength(255);
            e.Property(x => x.MotherEducation).HasColumnName("MotherEdu").HasMaxLength(255);
            e.Property(x => x.CurrentAddress).HasColumnName("CurrentAdress").HasMaxLength(255);
            e.Property(x => x.PermanentAddress).HasMaxLength(255);
            e.Property(x => x.EmergencyName).HasColumnName("EmerContactName").HasMaxLength(255);
            e.Property(x => x.EmergencyNo).HasColumnName("EmerNo").HasMaxLength(255);
            e.Property(x => x.Nationality).HasMaxLength(255);
            e.Property(x => x.Notes).HasColumnName("FamilyNotes");
            e.Ignore(x => x.ChildrenOnRoll);
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            MapSoftDelete(e);
        });

        b.Entity<Conversation>(e =>
        {
            e.ToTable("tblConvo");
            e.Property(x => x.Id).HasColumnName("ConvoID");
            e.Property(x => x.StudentId).HasColumnName("fk_CC");
            e.Property(x => x.Date).HasColumnName("ConvoDate").HasColumnType("datetime");
            e.Property(x => x.Time).HasColumnName("ConvoTime").HasColumnType("datetime");
            e.Property(x => x.TypeId).HasColumnName("ConvoType");
            e.Property(x => x.Transcript).HasColumnName("ConvoTranscript");
            e.Property(x => x.Resolution).HasColumnType("ntext");
            e.Property(x => x.EnteredBy).HasMaxLength(255);
            e.Property(x => x.ResolvedBy).HasMaxLength(255);
            e.Property(x => x.Notes).HasMaxLength(255);
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            MapSoftDelete(e);
        });

        b.Entity<StudentDocument>(e =>
        {
            e.ToTable("tblStudentDocuments");
            e.Property(x => x.Id).HasColumnName("DocID");
            e.Property(x => x.StudentId).HasColumnName("CC");
            e.Property(x => x.DocType).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.FilePath).HasMaxLength(400);
            e.Property(x => x.OriginalFileName).HasMaxLength(260);
            e.Property(x => x.Notes).HasMaxLength(255);
            e.Property(x => x.CreatedBy).HasMaxLength(50);
            e.Property(x => x.DeletedBy).HasMaxLength(50);
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            e.Ignore(x => x.TypeText);
            e.Ignore(x => x.FileName);
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId);
            MapSoftDelete(e);
        });

        b.Entity<BloodGroup>(e =>
        {
            e.ToTable("tblBloodGroups");
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.Name).HasColumnName("BloodGroup");
        });

        b.Entity<Campus>(e =>
        {
            e.ToTable("CampusInfo");
            e.Property(x => x.Id).HasColumnName("CampusID");
            e.Property(x => x.Name).HasColumnName("CampusName").HasMaxLength(100);
            e.Property(x => x.Address).HasMaxLength(100);
            e.Property(x => x.Phone1).HasColumnName("Ph1").HasMaxLength(100);
            e.Property(x => x.Phone2).HasColumnName("Ph2").HasMaxLength(100);
            e.Property(x => x.Phone3).HasColumnName("Ph3").HasMaxLength(100);
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            e.Ignore(x => x.Phones);
            MapSoftDelete(e);
        });

        b.Entity<FeeChallan>(e =>
        {
            e.ToTable("Feerecipt", t => t.HasTrigger("trg_Audit_Feerecipt"));
            e.Property(x => x.Id).HasColumnName("Sr#");
            e.Property(x => x.StudentId).HasColumnName("CC");
            e.Property(x => x.Period).HasMaxLength(50);
            e.Property(x => x.IssueDate).HasColumnType("date");
            e.Property(x => x.DueDate).HasColumnType("date");
            e.Property(x => x.RegistrationFee).HasColumnName("Mis_fee");
            e.Property(x => x.ReceivedOn).HasColumnName("ReDate").HasColumnType("date");
            e.Property(x => x.ReceiptNo).HasColumnName("RecipNo");
            e.Property(x => x.CollectionTypeId).HasColumnName("CollectionType");
            e.Property(x => x.DateEdited).HasColumnType("datetime");
            e.Property(x => x.EditedBy).HasColumnName("Description").HasMaxLength(255);
            e.Property(x => x.DrCr).HasColumnName("DRCR").HasMaxLength(2);
            e.Property(x => x.Remarks).HasMaxLength(255);
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            e.Ignore(x => x.Total);
            MapSoftDelete(e);
        });

        b.Entity<FeeDefinition>(e =>
        {
            e.ToTable("FeeDefinitation", t => t.HasTrigger("trg_Audit_FeeDefinitation"));
            e.Property(x => x.Id).HasColumnName("FeeDefId");
            e.Property(x => x.StudentId).HasColumnName("FK_CC");
            e.Property(x => x.DsNo).HasColumnName("DS#");
            e.Property(x => x.RegistrationFee).HasColumnName("MiscFee");
            e.Property(x => x.ProspectusFee).HasColumnName("LabFee");
            e.Property(x => x.AcFee).HasColumnName("ParkingFee");
            e.Property(x => x.CreatedDate).HasColumnType("date");
            e.Property(x => x.RevisionDate).HasColumnType("date");
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            MapSoftDelete(e);
        });

        b.Entity<FeeIncrease>(e =>
        {
            e.ToTable("tblFeeUpdateList");
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.StudentId).HasColumnName("CC");
            e.Property(x => x.CurrentFee).HasColumnType("money");
            e.Property(x => x.IncreasedAmount).HasColumnType("money");
            e.Property(x => x.NewFee).HasColumnType("money");
            e.Property(x => x.UpdatedOn).HasColumnType("datetime");
            e.Property(x => x.Reason).HasColumnName("ReasonUpdate").HasMaxLength(255);
        });

        b.Entity<CollectionType>(e =>
        {
            e.ToTable("tblCollectionTypes");
            e.Property(x => x.Id).HasColumnName("CollectionTypeID");
            e.Property(x => x.Name).HasColumnName("CollectionType");
            e.Property(x => x.Code).HasColumnName("Rem");
            e.Property(x => x.Active).HasColumnName("Status");
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            MapSoftDelete(e);
        });

        b.Entity<FeeReceiptFile>(e =>
        {
            e.ToTable("tblFeeReceiptFiles");
            e.Property(x => x.Id).HasColumnName("FileID");
            e.Property(x => x.CollectionTypeId).HasColumnName("CollectionType");
            e.Property(x => x.ReceivedOn).HasColumnType("date");
            e.Property(x => x.FilePath).HasMaxLength(400);
            e.Property(x => x.OriginalFileName).HasMaxLength(260);
            e.Property(x => x.CreatedBy).HasMaxLength(50);
            e.Property(x => x.DeletedBy).HasMaxLength(50);
            e.Property(x => x.RowVersion).HasColumnName("TS").IsRowVersion();
            MapSoftDelete(e);
        });
    }

    private static void MapSoftDelete<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : class, ISoftDelete
    {
        e.Property(x => x.IsDeleted).HasColumnName("isDeleted");
        e.Property(x => x.CreatedOn).HasColumnType("datetime");
        e.Property(x => x.DeletedOn).HasColumnType("datetime");
        e.HasQueryFilter(x => !x.IsDeleted);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var now = DateTime.Now;
        foreach (var entry in ChangeTracker.Entries<ISoftDelete>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedOn ??= now;
            else if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedOn = now;
            }
        }
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
}
