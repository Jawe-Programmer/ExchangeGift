using ExchangeGift.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ExchangeGift.Web.Services;

public sealed record ServiceResult(bool Ok, string? Error = null)
{
    public static ServiceResult Success() => new(true);
    public static ServiceResult Fail(string error) => new(false, error);
}

public sealed record CreateGroupResult(bool Ok, string? Error, ExchangeGiftGroup? Group);

public sealed record AddParticipantsResult(int Added, IReadOnlyList<string> Problems);

public class GroupService(AppDbContext db, TimeProvider clock, ILogger<GroupService> log)
{
    public const int MaxNameLength = 80;
    public const int MaxTitleLength = 120;
    public const int MaxParticipants = 200;

    public static string CleanName(string? raw) =>
        string.Join(' ', (raw ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Splits pasted text into names (one per line; semicolons also separate).</summary>
    public static List<string> ParseNames(string? pasted) =>
        (pasted ?? "")
            .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanName)
            .Where(n => n.Length > 0)
            .ToList();

    public async Task<bool> SlugExistsAsync(string slug, int? exceptGroupId = null, CancellationToken ct = default) =>
        await db.Groups.AnyAsync(g => g.Slug == slug && (exceptGroupId == null || g.Id != exceptGroupId), ct);

    public async Task<CreateGroupResult> CreateGroupAsync(
        string? title, string? slug, string? themeKey, IEnumerable<string> names, DateTime? expiresAtUtc, CancellationToken ct = default)
    {
        title = CleanName(title);
        if (title.Length == 0) return new(false, "Enter a title for the group.", null);
        if (title.Length > MaxTitleLength) return new(false, $"The title can be at most {MaxTitleLength} characters.", null);

        slug = string.IsNullOrWhiteSpace(slug) ? SlugService.Slugify(title) : slug.Trim().ToLowerInvariant();
        var slugError = ValidateSlug(slug);
        if (slugError is not null) return new(false, slugError, null);
        if (await SlugExistsAsync(slug, null, ct)) return new(false, "That URL slug is already used by another group.", null);

        if (!ThemeCatalog.IsValid(themeKey)) return new(false, "Choose one of the available themes.", null);

        var nameList = names.Select(CleanName).Where(n => n.Length > 0).ToList();
        var nameProblem = ValidateNames(nameList);
        if (nameProblem is not null) return new(false, nameProblem, null);

        var now = clock.GetUtcNow().UtcDateTime;
        var group = new ExchangeGiftGroup
        {
            Title = title,
            Slug = slug,
            ThemeKey = themeKey!,
            Status = GroupStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc,
        };
        var codes = new HashSet<string>();
        foreach (var n in nameList)
            group.Participants.Add(NewParticipant(n, codes, now));

        db.Groups.Add(group);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            log.LogWarning(ex, "Creating group {Slug} failed", slug);
            db.ChangeTracker.Clear();
            return new(false, "That URL slug is already used by another group.", null);
        }
        return new(true, null, group);
    }

    public static string? ValidateSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug)) return "Enter a URL slug.";
        if (!SlugService.IsWellFormed(slug)) return "The URL slug may only contain lowercase letters, numbers and single hyphens.";
        if (SlugService.IsReserved(slug)) return "That URL slug is reserved by the application. Choose another.";
        return null;
    }

    private static string? ValidateNames(List<string> names)
    {
        if (names.Any(n => n.Length > MaxNameLength)) return $"Names can be at most {MaxNameLength} characters.";
        if (names.Count > MaxParticipants) return $"A group can have at most {MaxParticipants} participants.";
        var dup = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        return dup is null ? null : $"\"{dup.Key}\" appears more than once. Each participant needs a unique name.";
    }

    private static ExchangeGiftParticipant NewParticipant(string name, HashSet<string> usedCodes, DateTime now)
    {
        string code;
        do { code = ParticipantCode.Generate(); } while (!usedCodes.Add(code));
        return new ExchangeGiftParticipant { Name = name, ParticipantCode = code, CreatedAtUtc = now, IsActive = true };
    }

    public async Task<AddParticipantsResult> AddParticipantsAsync(int groupId, IEnumerable<string> rawNames, CancellationToken ct = default)
    {
        var group = await db.Groups.Include(g => g.Participants).FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return new(0, new[] { "Group not found." });
        if (group.Status != GroupStatus.Draft)
            return new(0, new[] { "Participants can only be changed before the draw is generated." });

        var problems = new List<string>();
        var existing = new HashSet<string>(group.Participants.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var codes = new HashSet<string>(group.Participants.Select(p => p.ParticipantCode));
        var now = clock.GetUtcNow().UtcDateTime;
        var added = 0;

        foreach (var name in rawNames.Select(CleanName).Where(n => n.Length > 0))
        {
            if (name.Length > MaxNameLength) { problems.Add($"\"{name[..20]}…\" is longer than {MaxNameLength} characters."); continue; }
            if (!existing.Add(name)) { problems.Add($"\"{name}\" is already in the list."); continue; }
            if (group.Participants.Count >= MaxParticipants) { problems.Add($"A group can have at most {MaxParticipants} participants."); break; }
            group.Participants.Add(NewParticipant(name, codes, now));
            added++;
        }
        if (added > 0) group.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return new(added, problems);
    }

    public async Task<ServiceResult> RenameParticipantAsync(int groupId, int participantId, string? newName, CancellationToken ct = default)
    {
        var name = CleanName(newName);
        if (name.Length == 0) return ServiceResult.Fail("A name cannot be blank.");
        if (name.Length > MaxNameLength) return ServiceResult.Fail($"Names can be at most {MaxNameLength} characters.");

        var group = await db.Groups.Include(g => g.Participants).FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return ServiceResult.Fail("Group not found.");
        if (group.Status != GroupStatus.Draft) return ServiceResult.Fail("Names can only be edited before the draw is generated.");
        var p = group.Participants.FirstOrDefault(x => x.Id == participantId);
        if (p is null) return ServiceResult.Fail("Participant not found.");
        if (group.Participants.Any(x => x.Id != participantId && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult.Fail($"\"{name}\" is already in the list.");

        p.Name = name;
        group.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public const int MaxWishes = 3;
    public const int MaxWishLength = 100;

    /// <summary>Splits text into at most three trimmed, non-empty wishes.</summary>
    public static List<string> ParseWishes(string? text) =>
        (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanName).Where(w => w.Length > 0).Take(MaxWishes)
            .Select(w => w.Length > MaxWishLength ? w[..MaxWishLength] : w).ToList();

    /// <summary>Wishes can be edited at any time, even after the draw.</summary>
    public async Task<ServiceResult> SetWishesAsync(int groupId, int participantId, string? text, CancellationToken ct = default)
    {
        var p = await db.Participants.FirstOrDefaultAsync(x => x.Id == participantId && x.GroupId == groupId, ct);
        if (p is null) return ServiceResult.Fail("Participant not found.");
        var wishes = ParseWishes(text);
        p.Wishes = wishes.Count == 0 ? null : string.Join('\n', wishes);
        await db.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> RemoveParticipantAsync(int groupId, int participantId, CancellationToken ct = default)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return ServiceResult.Fail("Group not found.");
        if (group.Status != GroupStatus.Draft) return ServiceResult.Fail("Participants can only be removed before the draw is generated.");
        var p = await db.Participants.FirstOrDefaultAsync(x => x.Id == participantId && x.GroupId == groupId, ct);
        if (p is null) return ServiceResult.Fail("Participant not found.");
        db.Participants.Remove(p);
        group.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    /// <summary>Generates and stores the whole draw in one transaction. Fails if a draw already exists.</summary>
    public async Task<ServiceResult> GenerateDrawAsync(int groupId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return ServiceResult.Fail("Group not found.");
        if (group.Status != GroupStatus.Draft) return ServiceResult.Fail("The draw has already been generated for this group.");

        var people = await db.Participants.Where(p => p.GroupId == groupId && p.IsActive).OrderBy(p => p.Id).ToListAsync(ct);
        if (people.Count < 2) return ServiceResult.Fail("At least two participants are needed to generate a draw.");

        var perm = DrawAlgorithm.GenerateDerangement(people.Count);
        if (!DrawAlgorithm.IsDerangement(perm))
            return ServiceResult.Fail("The generated draw failed validation. Nothing was saved; please try again.");

        var now = clock.GetUtcNow().UtcDateTime;
        var assignments = people.Select((giver, i) => new ExchangeGiftAssignment
        {
            GroupId = groupId,
            GiverParticipantId = giver.Id,
            RecipientParticipantId = people[perm[i]].Id,
            CreatedAtUtc = now,
        }).ToList();

        // Second, independent check on the actual rows before they are committed.
        if (assignments.Any(a => a.GiverParticipantId == a.RecipientParticipantId)
            || assignments.Select(a => a.GiverParticipantId).Distinct().Count() != people.Count
            || assignments.Select(a => a.RecipientParticipantId).Distinct().Count() != people.Count)
            return ServiceResult.Fail("The generated draw failed validation. Nothing was saved; please try again.");

        db.Assignments.AddRange(assignments);
        group.Status = GroupStatus.Drawn;
        group.DrawGeneratedAtUtc = now;
        group.UpdatedAtUtc = now;
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            log.LogWarning(ex, "Draw for group {GroupId} could not be saved", groupId);
            return ServiceResult.Fail("The draw could not be saved. It may already have been generated.");
        }
        return ServiceResult.Success();
    }

    /// <summary>Deletes the draw and all reveal records and returns the group to Draft.</summary>
    public async Task<ServiceResult> ResetDrawAsync(int groupId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return ServiceResult.Fail("Group not found.");
        if (group.Status == GroupStatus.Draft) return ServiceResult.Fail("There is no draw to reset.");

        await db.Assignments.Where(a => a.GroupId == groupId).ExecuteDeleteAsync(ct);
        await db.Reveals.Where(r => r.GroupId == groupId).ExecuteDeleteAsync(ct);
        group.Status = GroupStatus.Draft;
        group.DrawGeneratedAtUtc = null;
        group.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        log.LogInformation("Draw for group {GroupId} was reset", groupId);
        return ServiceResult.Success();
    }

    /// <summary>Changes only the stored theme key; assignments are never touched.</summary>
    public async Task<ServiceResult> SetThemeAsync(int groupId, string? themeKey, CancellationToken ct = default)
    {
        if (!ThemeCatalog.IsValid(themeKey)) return ServiceResult.Fail("Choose one of the available themes.");
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return ServiceResult.Fail("Group not found.");
        group.ThemeKey = themeKey!;
        group.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> UpdateDetailsAsync(int groupId, string? title, string? slug, DateTime? expiresAtUtc, CancellationToken ct = default)
    {
        title = CleanName(title);
        if (title.Length == 0) return ServiceResult.Fail("Enter a title for the group.");
        if (title.Length > MaxTitleLength) return ServiceResult.Fail($"The title can be at most {MaxTitleLength} characters.");
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return ServiceResult.Fail("Group not found.");

        slug = string.IsNullOrWhiteSpace(slug) ? group.Slug : slug.Trim().ToLowerInvariant();
        var slugError = ValidateSlug(slug);
        if (slugError is not null) return ServiceResult.Fail(slugError);
        if (slug != group.Slug)
        {
            if (group.Status != GroupStatus.Draft)
                return ServiceResult.Fail("The URL slug can only be changed before the draw, because shared links would stop working.");
            if (await SlugExistsAsync(slug, groupId, ct)) return ServiceResult.Fail("That URL slug is already used by another group.");
        }

        group.Title = title;
        group.Slug = slug;
        group.ExpiresAtUtc = expiresAtUtc;
        group.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); return ServiceResult.Fail("That URL slug is already used by another group."); }
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetArchivedAsync(int groupId, bool archived, CancellationToken ct = default)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return ServiceResult.Fail("Group not found.");
        if (archived)
        {
            group.Status = GroupStatus.Archived;
        }
        else
        {
            if (group.Status != GroupStatus.Archived) return ServiceResult.Fail("This group is not archived.");
            group.Status = group.DrawGeneratedAtUtc is null ? GroupStatus.Draft : GroupStatus.Drawn;
        }
        group.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> DeleteGroupAsync(int groupId, CancellationToken ct = default)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, ct);
        if (group is null) return ServiceResult.Fail("Group not found.");
        db.Groups.Remove(group);
        await db.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }
}
