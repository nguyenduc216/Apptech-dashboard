using ApptechDashboard.Models;

namespace ApptechDashboard.Services;

public static class RequestProgressChangeDetector
{
    public static RequestProgressChangeSet Detect(RequestProgressState before, RequestProgressState after)
    {
        if (before.RequestId != after.RequestId)
        {
            throw new ArgumentException("Progress states must belong to the same request.");
        }

        var oldRequestStatus = YeuCauTrangThaiCatalog.Normalize(before.RequestStatus);
        var newRequestStatus = YeuCauTrangThaiCatalog.Normalize(after.RequestStatus);
        var requestChange = string.Equals(oldRequestStatus, newRequestStatus, StringComparison.Ordinal)
            ? null
            : new RequestStatusChange(oldRequestStatus, newRequestStatus);

        var oldWorks = before.Works
            .GroupBy(work => work.RequestWorkItemId)
            .ToDictionary(group => group.Key, group => group.First());
        var workChanges = new List<ZaloWorkStatusChange>();
        foreach (var newWork in after.Works)
        {
            if (!oldWorks.TryGetValue(newWork.RequestWorkItemId, out var oldWork))
            {
                continue;
            }

            var oldStatus = YeuCauCongViecTrangThaiCatalog.Normalize(oldWork.Status);
            var newStatus = YeuCauCongViecTrangThaiCatalog.Normalize(newWork.Status);
            if (string.Equals(oldStatus, newStatus, StringComparison.Ordinal))
            {
                continue;
            }

            workChanges.Add(new ZaloWorkStatusChange(
                newWork.RequestWorkItemId,
                string.IsNullOrWhiteSpace(newWork.WorkName) ? oldWork.WorkName : newWork.WorkName,
                oldStatus,
                newStatus));
        }

        return new RequestProgressChangeSet(after.RequestCode, requestChange, workChanges);
    }
}
