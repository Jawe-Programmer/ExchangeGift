using ExchangeGift.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ExchangeGift.Web.Services;

public enum ParticipantLinkState { NotFound, NotReady, Closed, Ready }

public sealed record ParticipantLookup(
    ParticipantLinkState State,
    ExchangeGiftGroup? Group,
    ExchangeGiftParticipant? Participant);

public sealed record RevealResult(ParticipantLinkState State, string? RecipientName, IReadOnlyList<string>? Wishes = null);

public class RevealService(AppDbContext db, TimeProvider clock, ILogger<RevealService> log)
{
    /// <summary>
    /// Validates the group slug and participant code together. Every failure to match is NotFound,
    /// so a caller can show one generic response and learn nothing about which part was wrong.
    /// </summary>
    public async Task<ParticipantLookup> LookupAsync(string? slug, string? code, CancellationToken ct = default)
    {
        var normalizedCode = ParticipantCode.Normalize(code);
        var s = slug?.ToLowerInvariant();
        if (normalizedCode is null || !SlugService.IsWellFormed(s))
            return new(ParticipantLinkState.NotFound, null, null);

        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Slug == s, ct);
        if (group is null) return new(ParticipantLinkState.NotFound, null, null);

        var participant = await db.Participants.AsNoTracking()
            .FirstOrDefaultAsync(p => p.GroupId == group.Id && p.ParticipantCode == normalizedCode && p.IsActive, ct);
        if (participant is null) return new(ParticipantLinkState.NotFound, null, null);

        var now = clock.GetUtcNow().UtcDateTime;
        if (group.IsClosed(now)) return new(ParticipantLinkState.Closed, group, participant);
        if (group.Status != GroupStatus.Drawn) return new(ParticipantLinkState.NotReady, group, participant);
        return new(ParticipantLinkState.Ready, group, participant);
    }

    /// <summary>
    /// Returns the recipient for the participant identified by slug + code and records the reveal.
    /// Idempotent: repeat or concurrent calls return the same recipient and keep the first timestamp.
    /// </summary>
    public async Task<RevealResult> RevealAsync(string? slug, string? code, CancellationToken ct = default)
    {
        var lookup = await LookupAsync(slug, code, ct);
        if (lookup.State != ParticipantLinkState.Ready) return new(lookup.State, null);

        var participantId = lookup.Participant!.Id;
        var groupId = lookup.Group!.Id;

        var recipient = await db.Assignments
            .Where(a => a.GiverParticipantId == participantId && a.GroupId == groupId)
            .Select(a => new { a.Recipient.Name, a.Recipient.Wishes })
            .FirstOrDefaultAsync(ct);
        var recipientName = recipient?.Name;
        if (recipientName is null)
        {
            log.LogError("Participant {ParticipantId} in group {GroupId} has no assignment", participantId, groupId);
            return new(ParticipantLinkState.NotReady, null);
        }

        await RecordAsync(groupId, participantId, ct);
        return new(ParticipantLinkState.Ready, recipientName, GroupService.ParseWishes(recipient!.Wishes));
    }

    private async Task RecordAsync(int groupId, int participantId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var reveal = await db.Reveals.FirstOrDefaultAsync(r => r.ParticipantId == participantId, ct);
            if (reveal is null)
                db.Reveals.Add(new ExchangeGiftReveal
                {
                    GroupId = groupId, ParticipantId = participantId,
                    FirstRevealedAtUtc = now, LastVisitedAtUtc = now,
                });
            else
                reveal.LastVisitedAtUtc = now;

            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException)
            {
                // A simultaneous request inserted the row first (unique index on ParticipantId): retry as an update.
                db.ChangeTracker.Clear();
            }
        }
    }
}
