using ArqamBase.App.Core;
using ArqamBase.Data;
using Krypton.Toolkit;

namespace ArqamBase.App.Shell;

/// <summary>Shows a bank / online deposit slip picture, with Open in its program and Save a copy. PDF slips open in their program.</summary>
public sealed class SlipViewerForm : KryptonForm
{
    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(96, 96, 96) };

    private SlipViewerForm(FeeReceiptFileStore store, FeeReceiptFile file, Image image, string title)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Shown += (_, _) => Core.Theme.FitButtons(this); // buttons as wide as their text at any font size
        Size = new Size(900, 800);
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        _picture.Image = image;

        var open = Button("Open in its program", Icons.Document, () => Run(() => store.Open(file)));
        var save = Button("Save a copy…", Icons.Save, () => SaveCopy(store, file));
        var close = Button("Close", Icons.Exit, Close);
        var info = new KryptonLabel
        {
            AutoSize = true, Margin = new Padding(12, 6, 0, 0),
            Text = $"{Path.GetFileName(file.FilePath)} · {image.Width} × {image.Height} · added by {file.CreatedBy} on {file.CreatedOn:dd/MM/yyyy}",
            StateCommon = { ShortText = { Color1 = Theme.Muted } }
        };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 6), BackColor = Color.Transparent };
        bar.Controls.AddRange([open, save, close, info]);
        var host = new KryptonPanel { Dock = DockStyle.Fill };
        host.Controls.Add(_picture);
        host.Controls.Add(bar);
        Controls.Add(host);
    }

    private static KryptonButton Button(string text, char glyph, Action click)
    {
        var b = new KryptonButton { Text = text, Values = { Image = Icons.Small(glyph) }, Margin = new Padding(0, 0, 8, 0) };
        b.Size = new Size(TextRenderer.MeasureText(text, Theme.BaseFont).Width + 48, Theme.RowHeight + 6);
        b.Click += (_, _) => click();
        return b;
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (DocumentProblem ex) { KryptonMessageBox.Show(this, ex.Message, Text, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Program.ShowError("Could not open the slip.", ex);
        }
    }

    private void SaveCopy(FeeReceiptFileStore store, FeeReceiptFile file)
    {
        var ext = Path.GetExtension(file.FilePath);
        using var dialog = new SaveFileDialog { FileName = Path.GetFileName(file.FilePath), Filter = $"{ext.TrimStart('.').ToUpperInvariant()} file|*{ext}", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dialog.FileName, store.ReadBytes(file) ?? throw new DocumentProblem("The slip's file could not be found."));
        }
        catch (DocumentProblem ex) { KryptonMessageBox.Show(this, ex.Message, Text, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Program.ShowError("Could not save the copy.", ex); }
    }

    /// <summary>Shows a slip: a picture in this window, a PDF (or other document) in its own program.</summary>
    public static void Show(IWin32Window owner, FeeReceiptFileStore store, FeeReceiptFile file, string title)
    {
        try
        {
            if (!FeeReceiptFileStore.IsPicture(file))
            {
                store.Open(file);
                return;
            }
            var bytes = store.ReadBytes(file) ?? throw new DocumentProblem($"The slip's file could not be found:\n{file.FilePath}");
            Image image;
            try { image = new Bitmap(new MemoryStream(bytes)); }
            catch (ArgumentException) { throw new DocumentProblem("The slip's picture could not be read."); }
            using var form = new SlipViewerForm(store, file, image, title);
            form.ShowDialog(owner);
            image.Dispose();
        }
        catch (DocumentProblem ex) { KryptonMessageBox.Show(owner, ex.Message, title, KryptonMessageBoxButtons.OK, KryptonMessageBoxIcon.Information); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
                                       or Microsoft.Data.SqlClient.SqlException)
        {
            Program.ShowError("Could not show the slip.", ex);
        }
    }
}
