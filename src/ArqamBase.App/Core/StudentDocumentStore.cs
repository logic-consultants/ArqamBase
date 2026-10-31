using System.Diagnostics;
using System.Drawing.Imaging;
using System.Security.Principal;
using System.Text.RegularExpressions;
using ArqamBase.Data;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Core;

/// <summary>A reason a file cannot be attached, worded for the user.</summary>
public sealed class DocumentProblem(string message) : Exception(message);

/// <summary>
/// Saves student pictures and documents in the shared attachments folder as [CC]_[Name]_[Type].ext
/// (e.g. 2562_MARYAM NAEEM_ProfilePicture.jpg) and records them in tblStudentDocuments.
/// Pictures over 1 MB are shrunk; other documents may be up to 3 MB. Files are read and written with the
/// user's DAS-SVR Windows account, the same one used for SQL Server.
/// Each change is saved straight away, separately from the Students screen's Save.
/// </summary>
public sealed partial class StudentDocumentStore(AppConfig config, UserSession session)
{
    public const long PictureLimit = 1024 * 1024;
    public const long DocumentLimit = 3 * 1024 * 1024;
    private const string DeletedFolderName = "Deleted";

    public static readonly string[] PictureExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];
    public static readonly string[] DocumentExtensions = [".pdf", ".doc", ".docx", ".xls", ".xlsx"];

    public string Folder => config.StudentAttachmentsFolder;

    public static bool IsPicture(string path) => PictureExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public List<StudentDocument> List(int cc)
    {
        using var db = config.CreateContext(session);
        return db.StudentDocuments.AsNoTracking().Where(d => d.StudentId == cc).OrderBy(d => d.CreatedOn).ToList()
            .OrderBy(d => DocumentTypes.All.ToList().FindIndex(t => t.Type == d.DocType) is var i && i >= 0 ? i : 99)
            .ThenBy(d => d.DocType.Length).ThenBy(d => d.DocType).ToList();
    }

    /// <summary>The file name for a student's document, without extension: 2562_MARYAM NAEEM_FormB.</summary>
    public static string BaseName(int cc, string? studentName, string docType)
    {
        var name = string.Concat((studentName ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        name = Spaces().Replace(name, " ").Trim().TrimEnd('.');
        if (name.Length > 80) name = name[..80].TrimEnd();
        return $"{cc}_{(name.Length == 0 ? "NoName" : name)}_{docType}";
    }

    /// <summary>
    /// Copies the file into the shared folder and records it. Type "Doc" becomes the next free Doc1, Doc2…
    /// If the student already has this type, the old file is replaced (moved to the Deleted folder).
    /// </summary>
    public StudentDocument Add(int cc, string? studentName, string docType, string sourcePath, string? notes)
    {
        var (bytes, extension) = PrepareFile(docType, sourcePath);

        using var db = config.CreateContext(session);
        var existing = db.StudentDocuments.Where(d => d.StudentId == cc).ToList();
        if (docType == DocumentTypes.Other)
            docType = NextOtherType(existing);
        var replaced = existing.FirstOrDefault(d => d.DocType == docType);
        if (replaced is null && existing.Count >= DocumentTypes.MaxPerStudent)
            throw new DocumentProblem($"A student can have up to {DocumentTypes.MaxPerStudent} pictures and documents. Delete one first.");

        var target = Path.Combine(Folder, BaseName(cc, studentName, docType) + extension);
        var doc = new StudentDocument
        {
            StudentId = cc,
            DocType = docType,
            FilePath = target,
            OriginalFileName = Path.GetFileName(sourcePath),
            FileSizeKB = (int)Math.Ceiling(bytes.Length / 1024.0),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedBy = session.UserName,
        };

        string? replacedOldPath = replaced?.FilePath;
        RunAsUser(() =>
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
            db.StudentDocuments.Add(doc);
            db.SaveChanges();
            transaction.Commit();
            return doc;
        }
        catch
        {
            // Put the files back as they were.
            RunAsUser(() =>
            {
                File.Delete(target);
                if (replaced is not null && replacedOldPath is not null && File.Exists(replaced.FilePath))
                    File.Move(replaced.FilePath, replacedOldPath);
            });
            throw;
        }
    }

    /// <summary>Moves the file to the Deleted folder and marks the row deleted.</summary>
    public void Delete(StudentDocument doc)
    {
        using var db = config.CreateContext(session);
        var row = db.StudentDocuments.Single(d => d.Id == doc.Id);
        RunAsUser(() => MarkDeleted(row));
        db.SaveChanges();
    }

    /// <summary>Renames a student's files after their name is corrected.</summary>
    public void RenameForStudent(int cc, string? newName)
    {
        using var db = config.CreateContext(session);
        var docs = db.StudentDocuments.Where(d => d.StudentId == cc).ToList();
        RunAsUser(() =>
        {
            foreach (var doc in docs)
            {
                var target = Path.Combine(Path.GetDirectoryName(doc.FilePath)!, BaseName(cc, newName, doc.DocType) + Path.GetExtension(doc.FilePath));
                if (string.Equals(target, doc.FilePath, StringComparison.OrdinalIgnoreCase) || !File.Exists(doc.FilePath)) continue;
                if (File.Exists(target)) MoveToDeleted(target);
                File.Move(doc.FilePath, target);
                doc.FilePath = target;
            }
        });
        db.SaveChanges();
    }

    public byte[]? ReadBytes(string path)
    {
        byte[]? bytes = null;
        try
        {
            RunAsUser(() => { if (File.Exists(path)) bytes = File.ReadAllBytes(path); });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return bytes;
    }

    /// <summary>Opens a document in its usual program (from a temporary copy, so the program does not need access to the shared folder).</summary>
    public void Open(StudentDocument doc)
    {
        var bytes = ReadBytes(doc.FilePath) ?? throw new DocumentProblem($"The file could not be found:\n{doc.FilePath}");
        var temp = Path.Combine(Path.GetTempPath(), "ArqamBase", doc.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        File.WriteAllBytes(temp, bytes);
        Process.Start(new ProcessStartInfo(temp) { UseShellExecute = true });
    }

    public void SaveCopy(StudentDocument doc, string destination)
    {
        var bytes = ReadBytes(doc.FilePath) ?? throw new DocumentProblem($"The file could not be found:\n{doc.FilePath}");
        File.WriteAllBytes(destination, bytes);
    }

    // ----- Files -----

    /// <summary>Reads the chosen file; shrinks pictures to 1 MB and checks documents are at most 3 MB.</summary>
    public static (byte[] Bytes, string Extension) PrepareFile(string docType, string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var picture = PictureExtensions.Contains(extension);
        if (docType == DocumentTypes.ProfilePicture && !picture)
            throw new DocumentProblem("The profile picture must be a picture (JPG, PNG, BMP, GIF or TIFF).");
        if (!picture && !DocumentExtensions.Contains(extension))
            throw new DocumentProblem("Only pictures and PDF, Word or Excel documents can be attached.");

        var bytes = File.ReadAllBytes(sourcePath);
        if (picture)
            return bytes.Length <= PictureLimit ? (bytes, extension == ".jpeg" ? ".jpg" : extension) : (ShrinkPicture(bytes), ".jpg");
        if (bytes.Length > DocumentLimit)
            throw new DocumentProblem($"This document is {bytes.Length / 1024.0 / 1024:0.0} MB. Documents can be up to 3 MB; please scan it at a lower quality or as a PDF.");
        return (bytes, extension);
    }

    /// <summary>Re-saves a picture as JPEG, making it smaller until it is under 1 MB.</summary>
    public static byte[] ShrinkPicture(byte[] data)
    {
        Image source;
        try
        {
            source = Image.FromStream(new MemoryStream(data));
        }
        catch (ArgumentException)
        {
            throw new DocumentProblem("This picture could not be read.");
        }
        using (source)
        {
            ApplyOrientation(source);
            var scale = Math.Min(1.0, 2400.0 / Math.Max(source.Width, source.Height));
            for (var attempt = 0; attempt < 15; attempt++, scale *= 0.85)
            {
                var size = new Size(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
                using var bitmap = new Bitmap(size.Width, size.Height);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.White); // transparent PNGs get a white background
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source, new Rectangle(Point.Empty, size));
                }
                var jpeg = EncodeJpeg(bitmap, 85);
                if (jpeg.Length <= PictureLimit) return jpeg;
            }
        }
        throw new DocumentProblem("This picture could not be made smaller than 1 MB.");
    }

    /// <summary>Turns a camera photo the right way up (EXIF orientation).</summary>
    public static void ApplyOrientation(Image image)
    {
        const int orientationTag = 0x0112;
        if (!image.PropertyIdList.Contains(orientationTag)) return;
        var value = image.GetPropertyItem(orientationTag)?.Value;
        if (value is null || value.Length == 0) return;
        var flip = value[0] switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone
        };
        if (flip == RotateFlipType.RotateNoneFlipNone) return;
        image.RotateFlip(flip);
        image.RemovePropertyItem(orientationTag);
    }

    private static byte[] EncodeJpeg(Image image, long quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        using var stream = new MemoryStream();
        image.Save(stream, codec, parameters);
        return stream.ToArray();
    }

    private static string NextOtherType(List<StudentDocument> existing)
    {
        var n = 1;
        while (existing.Any(d => d.DocType == $"{DocumentTypes.Other}{n}")) n++;
        return $"{DocumentTypes.Other}{n}";
    }

    private void MarkDeleted(StudentDocument doc)
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

    private void RunAsUser(Action action)
    {
        if (session.WindowsToken is { IsInvalid: false } token)
            WindowsIdentity.RunImpersonated(token, action);
        else
            action();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
