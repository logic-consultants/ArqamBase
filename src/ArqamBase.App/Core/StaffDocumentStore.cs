using ArqamBase.Data;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Core;

/// <summary>
/// Saves staff pictures and documents in the shared folder as [Employee ID]_[Name]_[Type].ext and records them in
/// tblStaffDocuments. The size rules, picture shrinking and Windows account are the same as for students.
/// Each change is saved straight away, separately from the Staff screen's Save.
/// </summary>
public sealed class StaffDocumentStore(AppConfig config, UserSession session)
{
    private const string DeletedFolderName = "Deleted";
    private readonly StudentDocumentStore _files = new(config, session);

    public string Folder => config.StaffAttachmentsFolder;

    public List<StaffDocument> List(int staffId)
    {
        using var db = config.CreateContext(session);
        return db.StaffDocuments.AsNoTracking().Where(d => d.StaffId == staffId).OrderBy(d => d.CreatedOn).ToList()
            .OrderBy(d => StaffDocumentTypes.All.ToList().FindIndex(t => t.Type == d.DocType) is var i && i >= 0 ? i : 99)
            .ThenBy(d => d.DocType.Length).ThenBy(d => d.DocType).ToList();
    }

    /// <summary>
    /// Copies the file into the shared folder and records it. Type "Doc" becomes the next free Doc1, Doc2…
    /// If the person already has this type, the old file is replaced (moved to the Deleted folder).
    /// </summary>
    public StaffDocument Add(int staffId, string? staffName, string docType, string sourcePath, string? notes)
    {
        var (bytes, extension) = StudentDocumentStore.PrepareFile(docType, sourcePath);

        using var db = config.CreateContext(session);
        var existing = db.StaffDocuments.Where(d => d.StaffId == staffId).ToList();
        if (docType == StaffDocumentTypes.Other)
            docType = NextOtherType(existing);
        var replaced = existing.FirstOrDefault(d => d.DocType == docType);
        if (replaced is null && docType != StaffDocumentTypes.ProfilePicture
                             && existing.Count(d => d.DocType != StaffDocumentTypes.ProfilePicture) >= StaffDocumentTypes.MaxDocuments)
            throw new DocumentProblem($"A staff member can have up to {StaffDocumentTypes.MaxDocuments} documents and a profile picture. Delete one first.");

        var target = Path.Combine(Folder, StudentDocumentStore.BaseName(staffId, staffName, docType) + extension);
        var doc = new StaffDocument
        {
            StaffId = staffId,
            DocType = docType,
            FilePath = target,
            OriginalFileName = Path.GetFileName(sourcePath),
            FileSizeKB = (int)Math.Ceiling(bytes.Length / 1024.0),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedBy = session.UserName,
        };

        var replacedOldPath = replaced?.FilePath;
        _files.RunAsUser(() =>
        {
            Directory.CreateDirectory(Folder);
            if (replaced is not null)
                MarkDeleted(replaced);
            else if (File.Exists(target))
                MoveToDeleted(target); // a file left over from earlier, not in the table
            File.WriteAllBytes(target, bytes);
        });
        try
        {
            // The old row is marked deleted before the new one is added: only one file of each type may be current.
            using var transaction = db.Database.BeginTransaction();
            db.SaveChanges();
            db.StaffDocuments.Add(doc);
            db.SaveChanges();
            transaction.Commit();
            return doc;
        }
        catch
        {
            // Put the files back as they were.
            _files.RunAsUser(() =>
            {
                File.Delete(target);
                if (replaced is not null && replacedOldPath is not null && File.Exists(replaced.FilePath))
                    File.Move(replaced.FilePath, replacedOldPath);
            });
            throw;
        }
    }

    /// <summary>Moves the file to the Deleted folder and marks the row deleted.</summary>
    public void Delete(StaffDocument doc)
    {
        using var db = config.CreateContext(session);
        var row = db.StaffDocuments.Single(d => d.Id == doc.Id);
        _files.RunAsUser(() => MarkDeleted(row));
        db.SaveChanges();
    }

    /// <summary>Renames a person's files after their name is corrected.</summary>
    public void RenameForStaff(int staffId, string? newName)
    {
        using var db = config.CreateContext(session);
        var docs = db.StaffDocuments.Where(d => d.StaffId == staffId).ToList();
        _files.RunAsUser(() =>
        {
            foreach (var doc in docs)
            {
                var target = Path.Combine(Path.GetDirectoryName(doc.FilePath)!,
                    StudentDocumentStore.BaseName(staffId, newName, doc.DocType) + Path.GetExtension(doc.FilePath));
                if (string.Equals(target, doc.FilePath, StringComparison.OrdinalIgnoreCase) || !File.Exists(doc.FilePath)) continue;
                if (File.Exists(target)) MoveToDeleted(target);
                File.Move(doc.FilePath, target);
                doc.FilePath = target;
            }
        });
        db.SaveChanges();
    }

    public byte[]? ReadBytes(string path) => _files.ReadBytes(path);

    /// <summary>Opens a document in its usual program (from a temporary copy).</summary>
    public void Open(StaffDocument doc) => _files.OpenFile(doc.FilePath);

    public void SaveCopy(StaffDocument doc, string destination)
    {
        var bytes = ReadBytes(doc.FilePath) ?? throw new DocumentProblem($"The file could not be found:\n{doc.FilePath}");
        File.WriteAllBytes(destination, bytes);
    }

    private static string NextOtherType(List<StaffDocument> existing)
    {
        var n = 1;
        while (existing.Any(d => d.DocType == $"{StaffDocumentTypes.Other}{n}")) n++;
        return $"{StaffDocumentTypes.Other}{n}";
    }

    private void MarkDeleted(StaffDocument doc)
    {
        if (File.Exists(doc.FilePath))
            doc.FilePath = MoveToDeleted(doc.FilePath);
        doc.IsDeleted = true;
        doc.DeletedOn = DateTime.Now;
        doc.DeletedBy = session.UserName;
    }

    /// <summary>Moves a file to the Deleted subfolder with the date and time added to its name.</summary>
    private string MoveToDeleted(string path)
    {
        var folder = Path.Combine(Folder, DeletedFolderName);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");
        File.Move(path, target, overwrite: true);
        return target;
    }
}
