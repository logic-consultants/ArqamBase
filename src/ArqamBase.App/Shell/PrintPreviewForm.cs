using System.Drawing.Printing;
using ArqamBase.App.Core;
using Krypton.Toolkit;
using QuestPDF.Fluent;
using IDocument = QuestPDF.Infrastructure.IDocument;
using ImageGenerationSettings = QuestPDF.Infrastructure.ImageGenerationSettings;

namespace ArqamBase.App.Shell;

/// <summary>Shows a printout page by page, with Print and Save as PDF.</summary>
public sealed class PrintPreviewForm : KryptonForm
{
    private const int ScreenDpi = 110, PrintDpi = 300;
    private readonly IDocument _document;
    private readonly string _fileName;
    private readonly FlowLayoutPanel _pages = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.FromArgb(128, 128, 128) };
    private readonly List<Image> _images = [];

    /// <param name="fileName">Suggested PDF file name, without extension.</param>
    public PrintPreviewForm(IDocument document, string title, string fileName)
    {
        _document = document;
        _fileName = string.Concat(fileName.Split(Path.GetInvalidFileNameChars()));
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 900);
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;

        var print = Button("Print…", Icons.Print, Print);
        var pdf = Button("Save as PDF…", Icons.Document, SavePdf);
        var close = Button("Close", Icons.Exit, Close);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 6), BackColor = Color.Transparent };
        bar.Controls.AddRange([print, pdf, close]);

        foreach (var bytes in document.GenerateImages(new ImageGenerationSettings { ImageFormat = QuestPDF.Infrastructure.ImageFormat.Png, RasterDpi = ScreenDpi }))
        {
            var image = Image.FromStream(new MemoryStream(bytes));
            _images.Add(image);
            _pages.Controls.Add(new PictureBox { Image = image, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(20, 16, 20, 4), BackColor = Color.White });
        }
        _pages.Resize += (_, _) => FitPages();

        var host = new KryptonPanel { Dock = DockStyle.Fill };
        host.Controls.Add(_pages);
        host.Controls.Add(bar);
        Controls.Add(host);
        Shown += (_, _) => { FitPages(); print.Focus(); };
    }

    private static KryptonButton Button(string text, char glyph, Action click)
    {
        var b = new KryptonButton { Text = text, Values = { Image = Icons.Small(glyph) }, Margin = new Padding(0, 0, 8, 0) };
        b.Size = new Size(TextRenderer.MeasureText(text, Theme.BaseFont).Width + 48, Theme.RowHeight + 6);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Pages as wide as the window allows.</summary>
    private void FitPages()
    {
        var width = Math.Max(200, _pages.ClientSize.Width - 40 - SystemInformation.VerticalScrollBarWidth);
        foreach (PictureBox p in _pages.Controls)
            if (p.Image is { } img) p.Size = new Size(width, width * img.Height / img.Width);
    }

    private void Print()
    {
        using var printDoc = new PrintDocument { DocumentName = _fileName };
        using var dialog = new PrintDialog { Document = printDoc, UseEXDialog = true, AllowSomePages = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        List<Image> pages;
        UseWaitCursor = true;
        try
        {
            pages = _document.GenerateImages(new ImageGenerationSettings { ImageFormat = QuestPDF.Infrastructure.ImageFormat.Png, RasterDpi = PrintDpi })
                .Select(b => Image.FromStream(new MemoryStream(b))).ToList();
        }
        finally { UseWaitCursor = false; }

        var next = 0;
        printDoc.PrintPage += (_, e) =>
        {
            // Draw over the whole sheet; the printer's unprintable edge is outside the page margins of the layout.
            var page = pages[next++];
            var hard = e.PageSettings;
            e.Graphics!.DrawImage(page, -hard.HardMarginX, -hard.HardMarginY, e.PageBounds.Width, e.PageBounds.Height);
            e.HasMorePages = next < pages.Count;
        };
        try { printDoc.Print(); }
        catch (Exception ex) when (ex is InvalidPrinterException or System.ComponentModel.Win32Exception) { Program.ShowError("Could not print.", ex); }
        finally { pages.ForEach(p => p.Dispose()); }
    }

    private void SavePdf()
    {
        using var dialog = new SaveFileDialog { Filter = "PDF file (*.pdf)|*.pdf", FileName = _fileName + ".pdf", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _document.GeneratePdf(dialog.FileName);
        }
        catch (IOException ex)
        {
            Program.ShowError("Could not save the PDF. Is it open in another program?", ex);
            return;
        }
        KryptonMessageBox.Show(this, $"Saved {Path.GetFileName(dialog.FileName)}.", Text, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information);
    }

    /// <summary>Builds the printout and shows it, or explains the problem if the layout fails.</summary>
    public static void Show(IWin32Window owner, Func<IDocument> build, string title, string fileName)
    {
        PrintPreviewForm form;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            form = new PrintPreviewForm(build(), title, fileName);
        }
        catch (QuestPDF.Drawing.Exceptions.DocumentLayoutException ex)
        {
            Program.ShowError("Could not lay out the printout.", ex);
            return;
        }
        finally { Cursor.Current = Cursors.Default; }
        using (form) form.ShowDialog(owner);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _images.ForEach(i => i.Dispose());
        base.Dispose(disposing);
    }
}
