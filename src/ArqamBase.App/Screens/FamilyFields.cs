using ArqamBase.App.Controls;
using ArqamBase.Data;
using Krypton.Toolkit;

namespace ArqamBase.App.Screens;

/// <summary>
/// The family's details in two boxes, "Father and mother" and "Address and emergency", as in the Family screen design.
/// Used on both the Students and Family screens. Its height follows its contents; put it in a scrolling panel.
/// </summary>
public sealed class FamilyFields : StackPanel
{
    private readonly BindingSource _bs = new() { DataSource = typeof(Family) };
    private readonly KryptonLabel _none = new() { Text = "No family linked.", AutoSize = true, Visible = false };
    private readonly Card _parents = new("Father and mother");
    private readonly Card _address = new("Address and emergency");

    /// <param name="showFlag">Shows a "Flagged family" tick box (the Family screen has a ribbon button instead).</param>
    public FamilyFields(bool canEdit, bool showFlag = true)
    {
        var parents = new FieldPanel(4);
        parents.Add("Family ID", Inputs.Text(_bs, nameof(Family.Id), readOnly: true));
        parents.Add("Father name", Inputs.Text(_bs, nameof(Family.FatherName)), 2);
        parents.Add("Father name (Urdu)", Inputs.Text(_bs, nameof(Family.FatherNameUrdu), rightToLeft: true));
        parents.Add("CNIC", Inputs.Cnic(_bs, nameof(Family.Cnic)));
        parents.Add("Caste", Inputs.Text(_bs, nameof(Family.Caste)));
        parents.Add("Education", Inputs.Text(_bs, nameof(Family.FatherEducation)));
        parents.Add("Profession", Inputs.Text(_bs, nameof(Family.Profession)));
        parents.Add("Business name", Inputs.Text(_bs, nameof(Family.BusinessName)), 2);
        parents.Add("Mobile", Inputs.Phone(_bs, nameof(Family.Mobile)));
        parents.Add("Office phone", Inputs.Phone(_bs, nameof(Family.PhoneOffice)));
        parents.Add("Mother name", Inputs.Text(_bs, nameof(Family.MotherName)), 2);
        parents.Add("Mother mobile", Inputs.Phone(_bs, nameof(Family.MotherMobile)));
        parents.Add("Mother education", Inputs.Text(_bs, nameof(Family.MotherEducation)));
        parents.Enabled = canEdit;
        _parents.AddTop(parents);

        var address = new FieldPanel(4);
        address.Add("Current address", Inputs.Text(_bs, nameof(Family.CurrentAddress)), 2);
        address.Add("Permanent address", Inputs.Text(_bs, nameof(Family.PermanentAddress)), 2);
        address.Add("Home phone", Inputs.Phone(_bs, nameof(Family.PhoneHome)));
        address.Add("Emergency contact", Inputs.Text(_bs, nameof(Family.EmergencyName)));
        address.Add("Emergency number", Inputs.Phone(_bs, nameof(Family.EmergencyNo)));
        address.Add("Nationality", Inputs.Text(_bs, nameof(Family.Nationality)));
        address.Add("Family notes", Inputs.Text(_bs, nameof(Family.Notes)), 2);
        address.Add("Special instructions", Inputs.Text(_bs, nameof(Family.SpecialInstructions)), 2);
        if (showFlag) address.Add("", Inputs.Check(_bs, nameof(Family.Flag), "Flagged family"));
        address.Enabled = canEdit;
        _address.AddTop(address);

        AddTop(_none, _parents, _address);
    }

    public Family? Family => _bs.DataSource as Family;

    public void Show(Family? family)
    {
        _bs.DataSource = family ?? (object)typeof(Family);
        _parents.Visible = _address.Visible = family is not null;
        _none.Visible = family is null;
    }

    /// <summary>Re-reads the record after it was changed in code (e.g. the Flag family button).</summary>
    public void RefreshRecord(Family family)
    {
        if (ReferenceEquals(_bs.DataSource, family)) _bs.ResetBindings(false);
    }

    /// <summary>Pushes the value being typed into the record (call before saving).</summary>
    public void Commit()
    {
        FindForm()?.Validate(); // pushes the value of the box being typed in
        _bs.EndEdit();
    }
}
