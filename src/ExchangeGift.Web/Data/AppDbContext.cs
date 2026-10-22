using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExchangeGift.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<ExchangeGiftGroup> Groups => Set<ExchangeGiftGroup>();
    public DbSet<ExchangeGiftParticipant> Participants => Set<ExchangeGiftParticipant>();
    public DbSet<ExchangeGiftAssignment> Assignments => Set<ExchangeGiftAssignment>();
    public DbSet<ExchangeGiftReveal> Reveals => Set<ExchangeGiftReveal>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ExchangeGiftGroup>(e =>
        {
            e.ToTable("ExchangeGiftGroups");
            e.Property(x => x.Title).HasMaxLength(120).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            e.Property(x => x.ThemeKey).HasMaxLength(80).IsRequired();
            e.Property(x => x.Status).HasConversion<int>();
            e.HasIndex(x => x.Slug).IsUnique();
        });

        b.Entity<ExchangeGiftParticipant>(e =>
        {
            e.ToTable("ExchangeGiftParticipants");
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.ParticipantCode).HasMaxLength(6).IsRequired();
            e.Property(x => x.Wishes).HasMaxLength(400);
            e.HasOne(x => x.Group).WithMany(g => g.Participants).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            // A code identifies exactly one participant within one group.
            e.HasIndex(x => new { x.GroupId, x.ParticipantCode }).IsUnique();
        });

        b.Entity<ExchangeGiftAssignment>(e =>
        {
            e.ToTable("ExchangeGiftAssignments");
            e.HasOne(x => x.Group).WithMany(g => g.Assignments).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Giver).WithMany().HasForeignKey(x => x.GiverParticipantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Recipient).WithMany().HasForeignKey(x => x.RecipientParticipantId).OnDelete(DeleteBehavior.Cascade);
            // Every giver has exactly one recipient, and every recipient is drawn exactly once.
            e.HasIndex(x => x.GiverParticipantId).IsUnique();
            e.HasIndex(x => x.RecipientParticipantId).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_Assignment_NoSelf", "\"GiverParticipantId\" <> \"RecipientParticipantId\""));
        });

        b.Entity<AppSetting>(e =>
        {
            e.ToTable("AppSettings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Value).HasMaxLength(500);
        });

        b.Entity<ExchangeGiftReveal>(e =>
        {
            e.ToTable("ExchangeGiftReveals");
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Participant).WithMany().HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ParticipantId).IsUnique();
        });
    }
}
