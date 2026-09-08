using Microsoft.EntityFrameworkCore;
using GorevTakipApi.Models;

namespace GorevTakipApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskActivity> TaskActivities => Set<TaskActivity>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<TaskChecklistItem> TaskChecklistItems => Set<TaskChecklistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Aynı e-postayla iki hesap açılmasın diye DB seviyesinde de garanti alıyoruz -
        // AuthController.Register zaten kontrol ediyor ama bu, eşzamanlı (race condition)
        // isteklere karşı asıl güvenceyi sağlıyor.
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // --- Metin alanlarının uzunluk sınırları ---
        // Sınır tanımlanmadığında hepsi PostgreSQL tarafında sınırsız "text" oluyordu;
        // megabaytlık bir başlık gönderilmesini engelleyen hiçbir şey yoktu.
        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.FullName).HasMaxLength(150).IsRequired();
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.AvatarUrl).HasMaxLength(500);
            e.Property(u => u.ResetToken).HasMaxLength(128);
        });

        modelBuilder.Entity<Team>()
            .Property(t => t.Name).HasMaxLength(150).IsRequired();

        modelBuilder.Entity<Project>()
            .Property(p => p.Name).HasMaxLength(150).IsRequired();

        modelBuilder.Entity<TaskItem>(e =>
        {
            e.Property(t => t.Title).HasMaxLength(200).IsRequired();
            e.Property(t => t.Description).HasMaxLength(4000);
            e.Property(t => t.Status).HasMaxLength(30).IsRequired();
            e.Property(t => t.Priority).HasMaxLength(30).IsRequired();
        });

        modelBuilder.Entity<TaskComment>()
            .Property(c => c.Content).HasMaxLength(2000).IsRequired();

        modelBuilder.Entity<Notification>()
            .Property(n => n.Message).HasMaxLength(500).IsRequired();

        // --- Silme davranışları ---
        // Bunlar tanımlanmadığında EF varsayılanlarına kalınıyordu: zorunlu foreign key'ler
        // Cascade, opsiyoneller Restrict. Sonuç iki yönde de kötüydü - üyesi olan bir ekip
        // foreign key ihlali yüzünden hiç silinemiyor (500), projesi olan bir ekip silinince
        // ise projeler, görevler ve yorumlar hiçbir uyarı olmadan yok oluyordu.

        // Ekip silinince üyeleri silinmez, sadece ekipsiz kalır.
        modelBuilder.Entity<User>()
            .HasOne(u => u.Team)
            .WithMany(t => t.Users)
            .HasForeignKey(u => u.TeamId)
            .OnDelete(DeleteBehavior.SetNull);

        // Projesi olan bir ekip silinemez; önce projeler taşınmalı ya da silinmeli.
        modelBuilder.Entity<Project>()
            .HasOne(p => p.Team)
            .WithMany(t => t.Projects)
            .HasForeignKey(p => p.TeamId)
            .OnDelete(DeleteBehavior.Restrict);

        // Proje silinince görevleri de silinir (ProjectsController açık onay istiyor).
        modelBuilder.Entity<TaskItem>()
            .HasOne(t => t.Project)
            .WithMany(p => p.Tasks)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Kullanıcı silinince görevleri silinmez, sadece ataması kalkar - iş kaybolmasın.
        modelBuilder.Entity<TaskItem>()
            .HasOne(t => t.AssignedTo)
            .WithMany()
            .HasForeignKey(t => t.AssignedToId)
            .OnDelete(DeleteBehavior.SetNull);

        // Görev silinince yorumları da silinir.
        modelBuilder.Entity<TaskComment>()
            .HasOne(c => c.TaskItem)
            .WithMany()
            .HasForeignKey(c => c.TaskItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // Yorum yazarının yorumları UsersController.DeleteUser içinde açıkça siliniyor;
        // buradaki Restrict, o adım atlanırsa yorumların sessizce yok olmasını engelleyen
        // güvenlik ağı.
        modelBuilder.Entity<TaskComment>()
            .HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.CreatedAt });

        // Bildirim bir göreve işaret ediyor. Görev silinince bildirim silinmesin, sadece
        // bağlantısı kopsun - kullanıcı geçmiş bildirimlerini kaybetmemeli.
        modelBuilder.Entity<Notification>()
            .HasOne(n => n.TaskItem)
            .WithMany()
            .HasForeignKey(n => n.TaskItemId)
            .OnDelete(DeleteBehavior.SetNull);

        // --- Görev geçmişi ---
        modelBuilder.Entity<TaskActivity>(e =>
        {
            e.Property(a => a.ActivityType).HasMaxLength(30).IsRequired();
            e.Property(a => a.Detail).HasMaxLength(500).IsRequired();

            // Görev silinince geçmişi de silinir (kaydın bağlandığı bir şey kalmıyor).
            e.HasOne(a => a.TaskItem)
                .WithMany()
                .HasForeignKey(a => a.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Kullanıcı silinse bile "kim yaptı" bilgisi yerine boş kalsın ama kayıt dursun.
            e.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(a => new { a.TaskItemId, a.CreatedAt });
        });

        // --- Dosya ekleri ---
        modelBuilder.Entity<TaskAttachment>(e =>
        {
            e.Property(a => a.FileName).HasMaxLength(260).IsRequired();
            e.Property(a => a.StoredFileName).HasMaxLength(260).IsRequired();
            e.Property(a => a.ContentType).HasMaxLength(120).IsRequired();

            e.HasOne(a => a.TaskItem)
                .WithMany(t => t.Attachments)
                .HasForeignKey(a => a.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // --- Kontrol listesi ---
        modelBuilder.Entity<TaskChecklistItem>(e =>
        {
            e.Property(c => c.Text).HasMaxLength(300).IsRequired();

            e.HasOne(c => c.TaskItem)
                .WithMany(t => t.ChecklistItems)
                .HasForeignKey(c => c.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(c => new { c.TaskItemId, c.SortOrder });
        });

        // Pano sütunları sıraya göre çekiliyor; bu indeks olmadan her yüklemede sıralama
        // maliyeti görev sayısıyla birlikte artıyor.
        modelBuilder.Entity<TaskItem>()
            .HasIndex(t => new { t.Status, t.SortOrder });
    }
}
