using Microsoft.EntityFrameworkCore;
using GorevTakipApi.Data;
using GorevTakipApi.Models;

namespace GorevTakipApi.Services;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _context;

    public NotificationService(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Bildirimi DbContext'e ekler ama KAYDETMEZ. Kaydetme işini çağıran controller,
    /// asıl işlemle birlikte tek bir SaveChangesAsync ile yapıyor.
    /// </summary>
    public void Queue(int userId, string message, int? taskItemId = null)
    {
        _context.Notifications.Add(new Notification
        {
            UserId = userId,
            Message = message,
            TaskItemId = taskItemId,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task QueueTeamAsync(int teamId, string message, int? excludeUserId = null, int? taskItemId = null)
    {
        var userIds = await _context.Users
            .AsNoTracking()
            .Where(u => u.TeamId == teamId && (excludeUserId == null || u.Id != excludeUserId))
            .Select(u => u.Id)
            .ToListAsync();

        foreach (var userId in userIds)
        {
            Queue(userId, message, taskItemId);
        }
    }
}
