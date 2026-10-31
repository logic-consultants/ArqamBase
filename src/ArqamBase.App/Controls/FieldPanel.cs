using ArqamBase.App.Core;
using Krypton.Toolkit;

namespace ArqamBase.App.Controls;

/// <summary>A form layout of labelled fields (small label above each input) in equal columns.</summary>
public sealed class FieldPanel : TableLayoutPanel
{
    public FieldPanel(int columns)
    {
        ColumnCount = columns;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        BackColor = Color.Transparent;
        GrowStyle = TableLayoutPanelGrowStyle.AddRows;
        for (var i = 0; i < columns; i++)
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
    }

    public T Add<T>(string label, T input, int span = 1) where T : Control
    {
        var cell = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 6), BackColor = Color.Transparent };
        cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cell.Controls.Add(new KryptonLabel { Text = label, AutoSize = true, Margin = new Padding(0), StateCommon = { ShortText = { Color1 = Theme.Muted, Font = new Font("Segoe UI", 8f) } } });
        input.Dock = DockStyle.Top;
        input.Margin = new Padding(0, 1, 0, 0);
        cell.Controls.Add(input);
        Controls.Add(cell);
        SetColumnSpan(cell, span);
        return input;
    }

    /// <summary>Leaves an empty cell.</summary>
    public void Skip(int span = 1)
    {
        var p = new Panel { Height = 1, BackColor = Color.Transparent };
        Controls.Add(p);
        SetColumnSpan(p, span);
    }
}

/// <summary>Helpers that create inputs and bind them to a property of the current record.</summary>
public static class Inputs
{
    public static KryptonTextBox Text(BindingSource bs, string property, bool readOnly = false, bool rightToLeft = false, bool multiline = false)
    {
        var box = new KryptonTextBox { ReadOnly = readOnly, Multiline = multiline, Height = multiline ? 54 : 25 };
        if (rightToLeft) box.RightToLeft = RightToLeft.Yes;
        var b = box.DataBindings.Add(nameof(box.Text), bs, property, true, DataSourceUpdateMode.OnValidation, "");
        b.DataSourceNullValue = null;
        return box;
    }

    public static KryptonDateTimePicker Date(BindingSource bs, string property)
    {
        var picker = new KryptonDateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", ShowCheckBox = true };
        picker.DataBindings.Add(nameof(picker.ValueNullable), bs, property, true, DataSourceUpdateMode.OnPropertyChanged);
        return picker;
    }

    public static KryptonCheckBox Check(BindingSource bs, string property, string text)
    {
        var box = new KryptonCheckBox { Text = text, AutoSize = true };
        box.DataBindings.Add(nameof(box.Checked), bs, property, true, DataSourceUpdateMode.OnPropertyChanged);
        return box;
    }

    /// <summary>
    /// Drop-down of (value, text) items bound to a nullable value property. An item with a null value is the blank choice.
    /// A combo box cannot select a null value, so blanks are shown through a stand-in value and saved back as null.
    /// </summary>
    public static KryptonComboBox Combo<TValue>(BindingSource bs, string property, IEnumerable<(TValue? Value, string Text)> items)
    {
        var combo = new KryptonComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 220 };
        var list = items.Select(i => new Item<object>(i.Value is null ? Blank : i.Value, i.Text)).ToList();
        if (!list.Any(i => Blank.Equals(i.Value)))
            list.Insert(0, new Item<object>(Blank, ""));
        combo.DataSource = list;
        combo.ValueMember = nameof(Item<object>.Value);
        combo.DisplayMember = nameof(Item<object>.Text);
        var b = combo.DataBindings.Add(nameof(combo.SelectedValue), bs, property, true, DataSourceUpdateMode.OnPropertyChanged, Blank);
        b.DataSourceNullValue = null;
        b.Parse += (_, e) => { if (Blank.Equals(e.Value)) e.Value = null; };
        return combo;
    }

    /// <summary>Stand-in for "no value" in drop-downs.</summary>
    private const string Blank = "\u0001blank";

    public sealed record Item<TValue>(TValue? Value, string Text);
}
