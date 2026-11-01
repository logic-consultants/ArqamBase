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
            e.Ignore(x => x.PeriodText);
            MapSoftDelete(e);
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
