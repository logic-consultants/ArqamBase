using ArqamBase.Data;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Core;

/// <summary>
/// Bank / online deposit slips of fee receipts, kept in the FeeReceipts subfolder of the shared attachments folder as
/// [yyyy-MM-dd]_[type code]_[receipt no.] (e.g. 2026-11-01_ODG_953.jpg) and recorded in tblFeeReceiptFiles.
/// Pictures over 1 MB are shrunk; PDFs may be up to 3 MB. Files are written with the user's DAS-SVR Windows account.
/// </summary>
public sealed class FeeReceiptFileStore(AppConfig config, UserSession session)
{
    private const string DeletedFolderName = "Deleted";
    private readonly StudentDocumentStore _files = new(config, session);

    public string Folder => Path.Combine(config.StudentAttachmentsFolder, "FeeReceipts");

    /// <summary>Whether a collection type is an online deposit (ODG, ODB), which has a slip.</summary>
    public static bool IsOnline(CollectionType? type) => (type?.Code ?? "").Trim().StartsWith("OD", StringComparison.OrdinalIgnoreCase);

    /// <summary>The current slip of a receipt, or null.</summary>
    public FeeReceiptFile? Find(int typeId, int receiptNo, DateTime date)
    {
        using var db = config.CreateContext(session);
        return db.FeeReceiptFiles.AsNoTracking()
            .FirstOrDefault(f => f.CollectionTypeId == typeId && f.ReceiptNo == receiptNo && f.ReceivedOn == date.Date);
    }

    /// <summary>Copies the slip into the shared folder and records it; an earlier slip of the receipt moves to the Deleted folder.</summary>
    public FeeReceiptFile Add(CollectionType type, int receiptNo, DateTime date, string sourcePath)
    {
        var (bytes, extension) = StudentDocumentStore.PrepareFile("BankReceipt", sourcePath);
        var code = string.Concat((type.Code ?? $"T{type.Id}").Trim().Where(char.IsLetterOrDigit));
        var target = Path.Combine(Folder, $"{date:yyyy-MM-dd}_{code}_{receiptNo}{extension}");

        using var db = config.CreateContext(session);
        var replaced = db.FeeReceiptFiles.FirstOrDefault(f => f.CollectionTypeId == type.Id && f.ReceiptNo == receiptNo && f.ReceivedOn == date.Date);
        var row = new FeeReceiptFile
        {
            CollectionTypeId = type.Id,
            ReceiptNo = receiptNo,
            ReceivedOn = date.Date,
            FilePath = target,
            OriginalFileName = Path.GetFileName(sourcePath),
            FileSizeKB = (int)Math.Ceiling(bytes.Length / 1024.0),
            CreatedBy = session.UserName,
        };
        _files.RunAsUser(() =>
        {
            Directory.CreateDirectory(Folder);
            if (replaced is not null && File.Exists(replaced.FilePath)) replaced.FilePath = MoveToDeleted(replaced.FilePath);
            else if (File.Exists(target)) MoveToDeleted(target); // left over from earlier, not in the table
            File.WriteAllBytes(target, bytes);
        });
        if (replaced is not null)
        {
            replaced.IsDeleted = true;
            replaced.DeletedOn = DateTime.Now;
            replaced.DeletedBy = session.UserName;
        }
        // The old row is marked deleted before the new one is added: only one slip per receipt may be current.
        using var transaction = db.Database.BeginTransaction();
        db.SaveChanges();
        db.FeeReceiptFiles.Add(row);
        db.SaveChanges();
        transaction.Commit();
        return row;
    }

    public void Open(FeeReceiptFile file) => _files.OpenFile(file.FilePath);

    /// <summary>The slip's file (null if it cannot be found or read).</summary>
    public byte[]? ReadBytes(FeeReceiptFile file) => _files.ReadBytes(file.FilePath);

    public static bool IsPicture(FeeReceiptFile file) => StudentDocumentStore.IsPicture(file.FilePath);

    /// <summary>
    /// A slip for printing on the receipt: the picture as a JPEG (turned the right way up, at most 2400 pixels), or, when it is
    /// a PDF or cannot be read, a note to print instead.
    /// </summary>
    public static (byte[]? Picture, string? Note) ForPrinting(string path, byte[]? bytes)
    {
        if (bytes is null) return (null, "Bank / online deposit slip: the file could not be found.");
        if (!StudentDocumentStore.IsPicture(path)) return (null, $"Bank / online deposit slip attached as {Path.GetExtension(path).TrimStart('.').ToUpperInvariant()} (not printed).");
        try { return (StudentDocumentStore.ShrinkPicture(bytes), null); }
        catch (DocumentProblem) { return (null, "Bank / online deposit slip: the picture could not be read."); }
    }


    private string MoveToDeleted(string path)
    {
        var folder = Path.Combine(Folder, DeletedFolderName);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");
        File.Move(path, target, overwrite: true);
        return target;
    }
}
