using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ArqamBase.App.Printing;

/// <summary>What one employee was paid for a month, from their line on the salary sheet.</summary>
public sealed record SalaryVoucher(int StaffId, string Name, string Post, string Campus, string Month, decimal Basic, int Days, int AbsentLeave,
    decimal Incentive, decimal Deduction, decimal Scf, decimal Security, decimal AdvanceDeducted, decimal AdvanceBalance, decimal Paid,
    string PayMethod, string? RemainingSecurity);

/// <summary>An employee's salary voucher on half an A4 sheet.</summary>
public static class SalaryVouchers
{
    public static IDocument Create(SalaryVoucher v, string schoolName, SchoolPrint school) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A5.Landscape());
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(x => x.FontFamily(PrintKit.TextFont("Arial")).FontSize(9.5f));
            page.Header().PaddingBottom(6).Element(c => PrintKit.Letterhead(c, schoolName, school));
            page.Content().Column(col =>
            {
                col.Item().AlignCenter().Text($"Salary voucher: {v.Month}").FontSize(12).Bold().FontColor(PrintKit.Navy);
                col.Item().PaddingTop(6).Row(r =>
                {
                    r.RelativeItem().Text(t => { t.Span("Name: ").Bold(); t.Span(v.Name); });
                    r.RelativeItem().Text(t => { t.Span("Post: ").Bold(); t.Span(v.Post); });
                    r.ConstantItem(90).Text(t => { t.Span("Emp ID: ").Bold(); t.Span($"{v.StaffId}"); });
                });
                col.Item().PaddingTop(2).Row(r =>
                {
                    r.RelativeItem().Text(t => { t.Span("Campus: ").Bold(); t.Span(v.Campus); });
                    r.RelativeItem().Text(t => { t.Span("Days: ").Bold(); t.Span($"{v.Days}, absent or leave {v.AbsentLeave}"); });
                    r.ConstantItem(90).Text(t => { t.Span("Paid by: ").Bold(); t.Span(v.PayMethod); });
                });
                col.Item().PaddingTop(8).Row(r =>
                {
                    r.RelativeItem().Element(c => Side(c, "Earnings", [("Salary", v.Basic), ("Incentive", v.Incentive)]));
                    r.ConstantItem(14);
                    r.RelativeItem().Element(c => Side(c, "Deductions",
                        [("Days absent", v.Deduction), ("SCF", v.Scf), ("Security", v.Security), ("Advance", v.AdvanceDeducted)]));
                });
                col.Item().PaddingTop(6).Background(Colors.Grey.Lighten3).Padding(4).Row(r =>
                {
                    r.RelativeItem().Text("Amount paid").Bold().FontSize(11);
                    r.AutoItem().Text($"Rs {v.Paid:N0}").Bold().FontSize(11);
                });
                var notes = new List<string>();
                if (v.AdvanceBalance > 0) notes.Add($"Advance still owed: Rs {v.AdvanceBalance:N0}");
                if (!string.IsNullOrWhiteSpace(v.RemainingSecurity)) notes.Add($"Security remaining: {v.RemainingSecurity!.Trim()}");
                if (notes.Count > 0) col.Item().PaddingTop(4).Text(string.Join("     ", notes)).FontSize(8.5f);
            });
            page.Footer().AlignRight().Element(c => PrintKit.Signature(c, "Accounts"));
        }));

    private static void Side(IContainer container, string title, (string Label, decimal Amount)[] lines) =>
        container.Column(col =>
        {
            col.Item().BorderBottom(0.75f).PaddingBottom(2).Text(title).Bold();
            foreach (var (label, amount) in lines.Where(l => l.Amount != 0))
                col.Item().PaddingTop(2).Row(r =>
                {
                    r.RelativeItem().Text(label);
                    r.AutoItem().Text(amount.ToString("N0"));
                });
        });
}
