using System.ComponentModel;
using System.Text.Json.Nodes;
using ArqamBase.Data;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Core;

public enum GoogleAction { Add, Update, MoveToLeft, BackFromLeft }

/// <summary>One change the sync would make in Google Contacts.</summary>
public sealed class GoogleSyncRow : INotifyPropertyChanged
{
    private bool _apply = true;
    private string _result = "";

    public bool Apply { get => _apply; set { if (_apply != value) { _apply = value; PropertyChanged?.Invoke(this, new(nameof(Apply))); } } }
    public GoogleAction Action { get; init; }
    public string ActionText => Action switch
    {
        GoogleAction.Add => "Add", GoogleAction.Update => "Update", GoogleAction.MoveToLeft => "Move to Left", _ => "Back from Left",
    };
    public int Cc { get; init; }
    public string Name { get; init; } = "";
    public string Father { get; init; } = "";
    public string Class { get; init; } = "";
    public string Changes { get; init; } = "";
    public string Result { get => _result; set { _result = value; PropertyChanged?.Invoke(this, new(nameof(Result))); } }

    /// <summary>The Google contact (empty for Add).</summary>
    public string ResourceName { get; init; } = "";
    /// <summary>What is sent: the new contact, or the updated names, phone numbers and custom fields.</summary>
    public JsonObject? Person { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>The result of comparing the students with the Google contacts.</summary>
public sealed record GoogleSyncPlan(List<GoogleSyncRow> Rows, int OnRoll, int UpToDate, int Duplicates, GoogleGroup? StudentsLabel, GoogleGroup? LeftLabel);

/// <summary>
/// Keeps the Google contacts in step with the students, one way: ArqamBase → Google. Each student is one contact, as Access
/// had them: prefix = CC, first name = student, last name = father, suffix = family ID, the father's mobile, and custom
/// fields Current Class, Department and Father CNIC. Contacts are matched on the CC in the prefix. Students who have left are
/// moved to the "Left students" label, never deleted. Phone numbers, notes and anything else typed on the phone are kept.
/// </summary>
public static class GoogleSync
{
    public const string LeftLabelName = "Left students";
    private static readonly HashSet<string> SystemGroups = ["contactGroups/myContacts", "contactGroups/starred", "contactGroups/all", "contactGroups/friends",
        "contactGroups/family", "contactGroups/coworkers", "contactGroups/chatBuddies", "contactGroups/blocked"];

    private sealed record StudentInfo(int Cc, string Name, string Father, int? FamilyId, string? Mobile, string Class, string Department, string Cnic, bool OnRoll);

    public static GoogleSyncPlan Plan(ArqamDbContext db, IReadOnlyList<GoogleContact> contacts, IReadOnlyList<GoogleGroup> groups)
    {
        var classes = db.Classes.AsNoTracking().Select(c => new { c.Id, c.Name, c.DepartmentId }).ToDictionary(c => c.Id);
        var departments = db.Departments.AsNoTracking().ToDictionary(d => d.Id, d => d.Name);
        var students = db.Students.AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.OnRoll, s.ClassId, s.FamilyId, s.WhatsApp, Father = s.Family!.FatherName, Mobile = s.Family!.Mobile, Cnic = s.Family!.Cnic })
            .ToList()
            .Select(s =>
            {
                var cls = s.ClassId is int c && classes.TryGetValue(c, out var ci) ? ci : null;
                var dept = cls?.DepartmentId is int d && departments.TryGetValue(d, out var dn) ? dn : null;
                return new StudentInfo(s.Id, Clean(s.Name), Clean(s.Father), s.FamilyId, MessageText.WhatsAppNumber(s.Mobile) ?? MessageText.WhatsAppNumber(s.WhatsApp),
                    Clean(cls?.Name), Clean(dept), Clean(s.Cnic), s.OnRoll);
            }).ToList();

        // Contacts are matched on the CC in the name prefix; a second contact with the same CC is left alone.
        var byCc = new Dictionary<int, GoogleContact>();
        var duplicates = 0;
        foreach (var c in contacts)
            if (int.TryParse(c.Prefix, out var cc) && !byCc.TryAdd(cc, c)) duplicates++;

        var left = groups.FirstOrDefault(g => string.Equals(g.Name, LeftLabelName, StringComparison.OrdinalIgnoreCase));
        // The label the student contacts are in: the one most of them share (other than the system ones and Left).
        var label = byCc.Values.SelectMany(c => c.Groups).Where(g => !SystemGroups.Contains(g) && g != left?.ResourceName)
            .GroupBy(g => g).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var studentsLabel = label is null ? null : groups.FirstOrDefault(g => g.ResourceName == label) ?? new GoogleGroup(label, label, 0);

        var rows = new List<GoogleSyncRow>();
        var upToDate = 0;
        foreach (var s in students.OrderBy(s => s.Name))
        {
            byCc.TryGetValue(s.Cc, out var contact);
            if (!s.OnRoll)
            {
                if (contact is not null && !contact.Groups.Contains(left?.ResourceName ?? LeftLabelName))
                    rows.Add(Row(GoogleAction.MoveToLeft, s, contact, "Has left: moves to the \"Left students\" label", null));
                continue;
            }
            if (contact is null && s.Name.Length == 0) continue; // a student with no name gets no contact
            if (contact is null)
            {
                rows.Add(Row(GoogleAction.Add, s, null, $"New contact: {Display(s)}", NewPerson(s, studentsLabel?.ResourceName)));
                continue;
            }
            var (person, changes) = Update(s, contact);
            if (left is not null && contact.Groups.Contains(left.ResourceName))
                rows.Add(Row(GoogleAction.BackFromLeft, s, contact, string.Join("; ", new[] { "Back on roll: leaves the \"Left students\" label" }.Concat(changes)), person));
            else if (person is not null)
            {
                // A different student name usually means the contact is someone else's: shown, but not ticked.
                var row = Row(GoogleAction.Update, s, contact, string.Join("; ", changes), person);
                if (!Same(contact.Given, s.Name)) { row.Apply = false; row.Result = ""; }
                rows.Add(row);
            }
            else upToDate++;
        }
        return new GoogleSyncPlan(rows, students.Count(s => s.OnRoll), upToDate, duplicates, studentsLabel, left);
    }

    private static GoogleSyncRow Row(GoogleAction action, StudentInfo s, GoogleContact? c, string changes, JsonObject? person) => new()
    {
        Action = action, Cc = s.Cc, Name = s.Name, Father = s.Father, Class = s.Class, Changes = changes, ResourceName = c?.ResourceName ?? "", Person = person,
    };

    private static string Display(StudentInfo s) => $"{s.Cc} {s.Name} {s.Father} {s.FamilyId}".Trim();

    private static JsonObject Name(StudentInfo s, string middle) => new()
    {
        ["honorificPrefix"] = s.Cc.ToString(), ["givenName"] = s.Name, ["middleName"] = middle, ["familyName"] = s.Father,
        ["honorificSuffix"] = s.FamilyId?.ToString() ?? "",
    };

    private static JsonObject NewPerson(StudentInfo s, string? label)
    {
        var person = new JsonObject
        {
            ["names"] = new JsonArray(Name(s, "")),
            ["userDefined"] = new JsonArray(Custom(s).Select(kv => (JsonNode)new JsonObject { ["key"] = kv.Key, ["value"] = kv.Value }).ToArray()),
        };
        if (s.Mobile is not null) person["phoneNumbers"] = new JsonArray(new JsonObject { ["value"] = "+" + s.Mobile, ["type"] = "mobile" });
        if (label is not null)
            person["memberships"] = new JsonArray(new JsonObject { ["contactGroupMembership"] = new JsonObject { ["contactGroupResourceName"] = label } });
        return person;
    }

    private static IEnumerable<KeyValuePair<string, string>> Custom(StudentInfo s)
    {
        yield return new("Current Class", s.Class);
        yield return new("Department", s.Department);
        if (s.Cnic.Length > 0) yield return new("Father CNIC", s.Cnic);
    }

    /// <summary>The update for a contact that differs from the student, with what changes; (null, []) when it is up to date.</summary>
    private static (JsonObject? Person, List<string> Changes) Update(StudentInfo s, GoogleContact c)
    {
        var changes = new List<string>();
        void Compare(string what, string google, string ours)
        {
            if (!Same(google, ours)) changes.Add($"{what}: {(google.Length == 0 ? "(none)" : google)} → {ours}");
        }
        Compare("Name", c.Given, s.Name);
        Compare("Father", c.Family, s.Father);
        Compare("Family", c.Suffix, s.FamilyId?.ToString() ?? "");

        var phones = (JsonArray)c.Phones.DeepClone();
        if (s.Mobile is not null && !phones.OfType<JsonObject>().Any(p => Last10(p["value"]?.ToString()) == Last10(s.Mobile)))
        {
            // Added in front; the numbers already on the contact are kept.
            changes.Add($"Mobile: add +{s.Mobile}");
            phones.Insert(0, new JsonObject { ["value"] = "+" + s.Mobile, ["type"] = "mobile" });
        }

        var custom = (JsonArray)c.UserDefined.DeepClone();
        foreach (var (key, value) in Custom(s))
        {
            var google = c.Custom(key);
            var same = key == "Father CNIC" ? Digits(google) == Digits(value) : Same(google, value);
            if (same) continue;
            changes.Add($"{key}: {(google.Length == 0 ? "(none)" : google)} → {value}");
            var item = custom.OfType<JsonObject>().FirstOrDefault(u => string.Equals(u["key"]?.ToString()?.Trim(), key, StringComparison.OrdinalIgnoreCase));
            if (item is not null) item["value"] = value;
            else custom.Add(new JsonObject { ["key"] = key, ["value"] = value });
        }
        if (changes.Count == 0) return (null, changes);
        var person = new JsonObject
        {
            ["etag"] = c.Etag, ["names"] = new JsonArray(Name(s, c.Middle)), ["phoneNumbers"] = phones, ["userDefined"] = custom,
        };
        // The CC itself cannot differ (it is how the contact was found), so names only change for the parts above.
        return (person, changes);
    }

    private static bool Same(string a, string b) => string.Equals(Squeeze(a), Squeeze(b), StringComparison.OrdinalIgnoreCase);
    private static string Squeeze(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static string Digits(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());
    private static string Last10(string? s) => Digits(s) is var d && d.Length >= 10 ? d[^10..] : d;
    private static string Clean(string? s) => Squeeze(s?.Trim() ?? "");

    /// <summary>
    /// Makes the ticked changes in Google: new contacts and updates in batches of 200, then the label moves.
    /// Reports progress, and puts each row's result in it.
    /// </summary>
    public static async Task<(int Done, int Failed)> ApplyAsync(GooglePeople google, GoogleSyncPlan plan, IProgress<string> progress, CancellationToken cancel)
    {
        var ticked = plan.Rows.Where(r => r.Apply && r.Result.Length == 0).ToList();
        var (done, failed) = (0, 0);

        async Task Batch(List<GoogleSyncRow> rows, Func<List<GoogleSyncRow>, Task> call, string what)
        {
            foreach (var chunk in rows.Chunk(200))
            {
                cancel.ThrowIfCancellationRequested();
                progress.Report($"{what} {done + failed + 1:N0}–{done + failed + chunk.Length:N0} of {ticked.Count:N0}…");
                try
                {
                    await call([.. chunk]);
                    foreach (var r in chunk) r.Result = "Done";
                    done += chunk.Length;
                }
                catch (GoogleException ex)
                {
                    foreach (var r in chunk) r.Result = "Failed: " + ex.Message;
                    failed += chunk.Length;
                }
                await Task.Delay(1500, cancel); // Google limits how fast contacts are written
            }
        }

        await Batch(ticked.Where(r => r.Action == GoogleAction.Add).ToList(), c => google.CreateAsync(c.Select(r => r.Person!), cancel), "Adding");
        await Batch(ticked.Where(r => r.Person is not null && r.Action is GoogleAction.Update or GoogleAction.BackFromLeft).ToList(),
            c => google.UpdateAsync(c.Select(r => (r.ResourceName, r.Person!)), cancel), "Updating");

        var moving = ticked.Where(r => r.Action is GoogleAction.MoveToLeft).ToList();
        var back = ticked.Where(r => r.Action is GoogleAction.BackFromLeft && (r.Result is "" or "Done")).ToList();
        if (moving.Count > 0 || back.Count > 0)
        {
            var left = plan.LeftLabel;
            try
            {
                if (left is null && moving.Count > 0) left = await google.CreateGroupAsync(LeftLabelName, cancel);
                foreach (var chunk in moving.Chunk(1000))
                {
                    progress.Report($"Moving {chunk.Length:N0} to \"{LeftLabelName}\"…");
                    var names = chunk.Select(r => r.ResourceName).ToList();
                    await google.ModifyGroupAsync(left!.ResourceName, names, [], cancel);
                    if (plan.StudentsLabel is { } students) await google.ModifyGroupAsync(students.ResourceName, [], names, cancel);
                    foreach (var r in chunk) r.Result = "Done";
                    done += chunk.Length;
                }
                foreach (var chunk in back.Chunk(1000))
                {
                    var names = chunk.Select(r => r.ResourceName).ToList();
                    if (left is not null) await google.ModifyGroupAsync(left.ResourceName, [], names, cancel);
                    if (plan.StudentsLabel is { } students) await google.ModifyGroupAsync(students.ResourceName, names, [], cancel);
                    foreach (var r in chunk.Where(r => r.Result.Length == 0)) { r.Result = "Done"; done++; }
                }
            }
            catch (GoogleException ex)
            {
                foreach (var r in moving.Where(r => r.Result.Length == 0)) { r.Result = "Failed: " + ex.Message; failed++; }
            }
        }
        return (done, failed);
    }
}
