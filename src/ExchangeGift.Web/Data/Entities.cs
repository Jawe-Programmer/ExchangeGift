namespace ExchangeGift.Web.Data;

public enum GroupStatus
{
    /// <summary>Participants are still being prepared; no draw exists.</summary>
    Draft = 0,
    /// <summary>The draw has been generated and personal links are live.</summary>
    Drawn = 1,
    /// <summary>Closed by the administrator; personal links stop working.</summary>
    Archived = 2,
}

public class ExchangeGiftGroup
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string ThemeKey { get; set; } = "";
    public GroupStatus Status { get; set; } = GroupStatus.Draft;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? DrawGeneratedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }

    public List<ExchangeGiftParticipant> Participants { get; set; } = new();
    public List<ExchangeGiftAssignment> Assignments { get; set; } = new();

    public bool IsClosed(DateTime utcNow) =>
        Status == GroupStatus.Archived || (ExpiresAtUtc is { } e && e <= utcNow);
}

public class ExchangeGiftParticipant
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public ExchangeGiftGroup Group { get; set; } = null!;
    public string Name { get; set; } = "";
    /// <summary>Random six-character uppercase alphanumeric public access token.</summary>
    public string ParticipantCode { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Up to three wishes, one per line, shown to whoever drew this person.</summary>
    public string? Wishes { get; set; }
}

public class ExchangeGiftAssignment
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public ExchangeGiftGroup Group { get; set; } = null!;
    public int GiverParticipantId { get; set; }
    public ExchangeGiftParticipant Giver { get; set; } = null!;
    public int RecipientParticipantId { get; set; }
    public ExchangeGiftParticipant Recipient { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
}

public class ExchangeGiftReveal
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public ExchangeGiftGroup Group { get; set; } = null!;
    public int ParticipantId { get; set; }
    public ExchangeGiftParticipant Participant { get; set; } = null!;
    public DateTime FirstRevealedAtUtc { get; set; }
    public DateTime LastVisitedAtUtc { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
