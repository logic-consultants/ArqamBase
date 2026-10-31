using ArqamBase.App.Controls;
using ArqamBase.Data;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens;

/// <summary>The family's details (father, mother, phones, address). Used on both the Students and Family screens.</summary>
public sealed class FamilyFields : KryptonPanel
{
    private readonly BindingSource _bs = new() { DataSource = typeof(Family) };
    private readonly KryptonLabel _none = new() { Text = "No family linked.", Dock = DockStyle.Top, AutoSize = true, Visible = false };
    private readonly TableLayoutPanel _fields = new() { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, ColumnCount = 1 };

    public FamilyFields(bool canEdit)
    {
        AutoScroll = true;

        var father = new FieldPanel(4);
        father.Add("Family ID", Inputs.Text(_bs, nameof(Family.Id), readOnly: true));
        father.Add("Father name", Inputs.Text(_bs, nameof(Family.FatherName)), 2);
        father.Add("Father name (Urdu)", Inputs.Text(_bs, nameof(Family.FatherNameUrdu), rightToLeft: true));
        father.Add("CNIC", Inputs.Text(_bs, nameof(Family.Cnic)));
        father.Add("Caste", Inputs.Text(_bs, nameof(Family.Caste)));
        father.Add("Education", Inputs.Text(_bs, nameof(Family.FatherEducation)));
        father.Add("Profession", Inputs.Text(_bs, nameof(Family.Profession)));
        father.Add("Business name", Inputs.Text(_bs, nameof(Family.BusinessName)), 2);
        father.Add("Mobile (WhatsApp)", Inputs.Text(_bs, nameof(Family.Mobile)));
        father.Add("Office phone", Inputs.Text(_bs, nameof(Family.PhoneOffice)));
        father.Add("Mother name", Inputs.Text(_bs, nameof(Family.MotherName)), 2);
        father.Add("Mother mobile", Inputs.Text(_bs, nameof(Family.MotherMobile)));
        father.Add("Mother education", Inputs.Text(_bs, nameof(Family.MotherEducation)));
        father.Add("Current address", Inputs.Text(_bs, nameof(Family.CurrentAddress)), 2);
        father.Add("Permanent address", Inputs.Text(_bs, nameof(Family.PermanentAddress)), 2);
        father.Add("Home phone", Inputs.Text(_bs, nameof(Family.PhoneHome)));
        father.Add("Emergency contact", Inputs.Text(_bs, nameof(Family.EmergencyName)));
        father.Add("Emergency number", Inputs.Text(_bs, nameof(Family.EmergencyNo)));
        father.Add("Nationality", Inputs.Text(_bs, nameof(Family.Nationality)));
        father.Add("Family notes", Inputs.Text(_bs, nameof(Family.Notes), multiline: true), 2);
        father.Add("Special instructions", Inputs.Text(_bs, nameof(Family.SpecialInstructions), multiline: true), 2);
        father.Add("", Inputs.Check(_bs, nameof(Family.Flag), "Flagged family"));
        father.Enabled = canEdit;

        _fields.Controls.Add(father);
        Controls.Add(_fields);
        Controls.Add(_none);
    }

    public Family? Family => _bs.DataSource as Family;

    public void Show(Family? family)
    {
        _bs.DataSource = family ?? (object)typeof(Family);
        _fields.Visible = family is not null;
        _none.Visible = family is null;
    }

    /// <summary>Pushes the value being typed into the record (call before saving).</summary>
    public void Commit()
    {
        FindForm()?.Validate(); // pushes the value of the box being typed in
        _bs.EndEdit();
    }
}
