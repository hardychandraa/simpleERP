using SimpleERP.Application.DTOs;
using SimpleERP.Application.Interfaces;
using SimpleERP.Domain.Interfaces;

namespace SimpleERP.Application.Services;

public class AuditService : IAuditService
{
    private readonly IAuditLogRepository _repo;
    public AuditService(IAuditLogRepository repo) => _repo = repo;

    public async Task<List<AuditLogDto>> GetRecentAsync(int count = 200) =>
        (await _repo.GetRecentAsync(count)).Select(a => new AuditLogDto {
            Id        = a.Id,
            Timestamp = a.Timestamp,
            User      = a.User,
            Action    = a.Action,
            Detail    = a.Detail
        }).ToList();
}
