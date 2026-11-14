using Kiitou.Hosts;
using Kiitou.Models;

namespace Kiitou.Catalog;

/// <summary>
/// Creates, renames, duplicates, deletes, saves and switches profiles. A profile is only
/// changed by an explicit save; editing a working copy of its choices never alters it.
/// </summary>
public sealed class ProfileManager
{
    public const int MaximumNameLength = 60;
    private readonly AppConfiguration _config;
    private readonly Func<DateTime> _utcNow;

    public ProfileManager(AppConfiguration config, Func<DateTime>? utcNow = null)
    {
        _config = config;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public IReadOnlyList<MappingProfile> Profiles => _config.Profiles;

    public MappingProfile Active => Get(_config.ActiveProfileId);

    public MappingProfile Get(Guid profileId) =>
        _config.Profiles.FirstOrDefault(p => p.Id == profileId)
        ?? throw new CatalogException("That profile no longer exists.");

    /// <summary>Creates a profile in which no website is enabled.</summary>
    public MappingProfile Create(string name)
    {
        var profile = new MappingProfile { Name = ValidateNewName(name, null), LastSavedUtc = _utcNow() };
        _config.Profiles.Add(profile);
        return profile;
    }

    public void Rename(Guid profileId, string name)
    {
        MappingProfile profile = Get(profileId);
        profile.Name = ValidateNewName(name, profileId);
    }

    public MappingProfile Duplicate(Guid profileId, string? name = null)
    {
        MappingProfile source = Get(profileId);
        var copy = new MappingProfile
        {
            Name = name is null ? UniqueName(source.Name + " (copy)") : ValidateNewName(name, null),
            Selections = SelectionEditor.Copy(source.Selections),
            LastSavedUtc = _utcNow()
        };
        _config.Profiles.Add(copy);
        return copy;
    }

    /// <summary>Deletes a profile. The last remaining profile cannot be deleted.</summary>
    public void Delete(Guid profileId)
    {
        MappingProfile profile = Get(profileId);
        if (_config.Profiles.Count == 1)
            throw new CatalogException("The last profile cannot be deleted.");

        _config.Profiles.Remove(profile);
        if (_config.ActiveProfileId == profileId) _config.ActiveProfileId = _config.Profiles[0].Id;
        if (_config.Hosts.LastAppliedProfileId == profileId) _config.Hosts.LastAppliedProfileId = null;
    }

    /// <summary>Makes a profile the selected one. This does not change the hosts file.</summary>
    public void SetActive(Guid profileId) => _config.ActiveProfileId = Get(profileId).Id;

    /// <summary>Overwrites a profile's saved choices with a copy of the given working choices.</summary>
    public void SaveSelections(Guid profileId, IEnumerable<ProfileWebsiteSelection> working)
    {
        MappingProfile profile = Get(profileId);
        profile.Selections = Cleaned(working);
        profile.LastSavedUtc = _utcNow();
    }

    /// <summary>Saves the given working choices as a new profile, leaving every existing profile unchanged.</summary>
    public MappingProfile SaveAsNew(string name, IEnumerable<ProfileWebsiteSelection> working)
    {
        var profile = new MappingProfile
        {
            Name = ValidateNewName(name, null),
            Selections = Cleaned(working),
            LastSavedUtc = _utcNow()
        };
        _config.Profiles.Add(profile);
        return profile;
    }

    /// <summary>The exact hosts mappings a saved profile describes.</summary>
    public IReadOnlyList<HostsMapping> BuildMappings(Guid profileId) =>
        new SelectionEditor(_config, Get(profileId).Selections).BuildMappings();

    /// <summary>Returns <paramref name="baseName"/>, or a numbered variant of it that no profile uses yet.</summary>
    public string UniqueName(string baseName)
    {
        string trimmed = (baseName ?? "").Trim();
        if (trimmed.Length == 0) trimmed = "Profile";
        if (trimmed.Length > MaximumNameLength) trimmed = trimmed[..MaximumNameLength].TrimEnd();
        if (!NameInUse(trimmed, null)) return trimmed;

        for (int number = 2; ; number++)
        {
            string suffix = " " + number;
            string stem = trimmed.Length + suffix.Length > MaximumNameLength
                ? trimmed[..(MaximumNameLength - suffix.Length)].TrimEnd()
                : trimmed;
            if (!NameInUse(stem + suffix, null)) return stem + suffix;
        }
    }

    private string ValidateNewName(string? name, Guid? ignoreProfileId)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) throw new CatalogException("Enter a profile name.");
        if (trimmed.Length > MaximumNameLength)
            throw new CatalogException($"Profile names can be at most {MaximumNameLength} characters.");
        if (trimmed.Any(char.IsControl)) throw new CatalogException("Profile names cannot contain control characters.");
        if (NameInUse(trimmed, ignoreProfileId))
            throw new CatalogException($"A profile named \"{trimmed}\" already exists.");
        return trimmed;
    }

    private bool NameInUse(string name, Guid? ignoreProfileId) =>
        _config.Profiles.Any(p => p.Id != ignoreProfileId &&
                                  string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private List<ProfileWebsiteSelection> Cleaned(IEnumerable<ProfileWebsiteSelection> working)
    {
        var editor = new SelectionEditor(_config, SelectionEditor.Copy(working));
        editor.Prune();
        return editor.Selections;
    }
}
