using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Helpers;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using MV.DomainLayer.Entities;
using MV.DomainLayer.Helpers;

namespace MV.ApplicationLayer.Services
{
    public class TutorAvailabilityService : ITutorAvailabilityService
    {
        private readonly IAppDbContext _context;
        private readonly ITutorService _tutorService;
        private const string DefaultFlexiblePackageName = "Gói lịch rảnh linh hoạt";

        public TutorAvailabilityService(IAppDbContext context, ITutorService tutorService)
        {
            _context = context;
            _tutorService = tutorService;
        }

        /// <summary>
        /// Add a new availability slot with overlap validation.
        /// Converts user timezone to UTC before saving to database.
        /// </summary>
        public async Task<TutorAvailabilityResponse> AddAvailabilityAsync(string tutorId, CreateAvailabilityRequest request)
        {
            // Parse time strings to TimeOnly — ParseTimeOnly handles "24:00" → 23:59
            var startTime = ParseTimeOnly(request.Starttime);
            var endTime = ParseTimeOnly(request.Endtime);

            if (startTime >= endTime)
                throw new ArgumentException("Giờ bắt đầu phải trước giờ kết thúc.");

            // FE sends UTC — use dayofweek and time directly, no conversion needed
            var utcStartDay = request.Dayofweek;
            var utcStartTime = startTime;
            var utcEndTime = endTime;

            // Check for overlapping slots on the same UTC day
            var existingSlots = await _context.Tutoravailabilities
                .Where(a => a.Tutorid == tutorId && a.Dayofweek == utcStartDay)
                .ToListAsync();

            foreach (var slot in existingSlots)
            {
                if (slot.Starttime.HasValue && slot.Endtime.HasValue &&
                    utcStartTime < slot.Endtime.Value && utcEndTime > slot.Starttime.Value)
                {
                    throw new InvalidOperationException(
                        $"Time slot overlaps with existing slot: {slot.Starttime.Value:HH:mm} - {slot.Endtime.Value:HH:mm}");
                }
            }

            var availability = new Tutoravailability
            {
                Tutorid = tutorId,
                Dayofweek = utcStartDay,
                Starttime = utcStartTime,
                Endtime = utcEndTime,
                Createdat = TimeZoneHelper.UtcNow
            };

            _context.Tutoravailabilities.Add(availability);
            await EnsureFlexiblePackageAsync(tutorId);
            await _context.SaveChangesAsync();

            await _tutorService.TryAutoSubmitAsync(tutorId);
            return MapToResponse(availability);
        }

        /// <summary>
        /// Add multiple availability slots at once with overlap validation.
        /// Converts user timezone to UTC before saving to database.
        /// </summary>
        public async Task<List<TutorAvailabilityResponse>> BulkAddAvailabilitiesAsync(string tutorId, BulkCreateAvailabilityRequest request)
        {
            var results = new List<TutorAvailabilityResponse>();
            var newSlots = new List<Tutoravailability>();

            // Get all existing slots for this tutor (in UTC)
            var existingSlots = await _context.Tutoravailabilities
                .Where(a => a.Tutorid == tutorId)
                .ToListAsync();

            // Parse and validate all new slots
            foreach (var req in request.Availabilities)
            {
                var startTime = ParseTimeOnly(req.Starttime);
                var endTime = ParseTimeOnly(req.Endtime);

                if (startTime >= endTime)
                    throw new ArgumentException($"Giờ bắt đầu phải trước giờ kết thúc (slot: {req.Starttime} - {req.Endtime}).");

                // FE sends UTC — use directly
                var utcStartDay = req.Dayofweek;
                var utcStartTime = startTime;
                var utcEndTime = endTime;

                var daySlots = existingSlots.Where(a => a.Dayofweek == utcStartDay).ToList();
                foreach (var slot in daySlots)
                {
                    if (slot.Starttime.HasValue && slot.Endtime.HasValue &&
                        utcStartTime < slot.Endtime.Value && utcEndTime > slot.Starttime.Value)
                    {
                        throw new InvalidOperationException(
                            $"Time slot {req.Starttime}-{req.Endtime} overlaps with existing slot: {slot.Starttime.Value:HH:mm} - {slot.Endtime.Value:HH:mm}");
                    }
                }

                // Check overlap with other new slots in this batch (in UTC)
                var conflictingNewSlot = newSlots.FirstOrDefault(ns =>
                    ns.Dayofweek == utcStartDay &&
                    ns.Starttime.HasValue && ns.Endtime.HasValue &&
                    utcStartTime < ns.Endtime.Value && utcEndTime > ns.Starttime.Value);

                if (conflictingNewSlot != null)
                {
                    throw new InvalidOperationException(
                        $"Time slot {req.Starttime}-{req.Endtime} overlaps with another slot in the request: {conflictingNewSlot.Starttime.Value:HH:mm} - {conflictingNewSlot.Endtime.Value:HH:mm}");
                }

                // Create new availability entity with UTC time
                var availability = new Tutoravailability
                {
                    Tutorid = tutorId,
                    Dayofweek = utcStartDay,
                    Starttime = utcStartTime,
                    Endtime = utcEndTime,
                    Createdat = TimeZoneHelper.UtcNow
                };

                newSlots.Add(availability);
            }

            // All validations passed, add all slots to database
            _context.Tutoravailabilities.AddRange(newSlots);
            await EnsureFlexiblePackageAsync(tutorId);
            await _context.SaveChangesAsync();

            await _tutorService.TryAutoSubmitAsync(tutorId);

            results = newSlots.Select(MapToResponse).ToList();
            return results;
        }

        /// <summary>
        /// Update multiple availability slots at once.
        /// Converts user timezone (+7) to UTC before saving. Blocks if a future booking exists in the old slot.
        /// </summary>
        public async Task<List<TutorAvailabilityResponse>> BulkUpdateAvailabilitiesAsync(string tutorId, BulkUpdateAvailabilityRequest request)
        {
            var updateIds = request.Availabilities.Select(a => a.Availabilityid).ToList();

            var availabilities = await _context.Tutoravailabilities
                .Where(a => updateIds.Contains(a.Availabilityid))
                .ToListAsync();

            if (availabilities.Count == 0)
                throw new ArgumentException("Không tìm thấy khung giờ nào để cập nhật.");

            var notFound = updateIds.Except(availabilities.Select(a => a.Availabilityid)).ToList();
            if (notFound.Any())
                throw new ArgumentException($"Không tìm thấy các khung giờ với ID: {string.Join(", ", notFound)}.");

            // Ownership check
            var unauthorizedSlots = availabilities.Where(a => a.Tutorid != tutorId).ToList();
            if (unauthorizedSlots.Any())
                throw new UnauthorizedAccessException(
                    $"Bạn không có quyền cập nhật {unauthorizedSlots.Count} slot(s) lịch học này.");

            // Future-lesson guard: block if any old slot has an upcoming booking
            foreach (var availability in availabilities)
            {
                var slotDayOfWeek = availability.Dayofweek!.Value;
                var slotStartTime = availability.Starttime!.Value.ToTimeSpan();
                var slotEndTime = availability.Endtime!.Value.ToTimeSpan();

                var hasUpcomingLessons = await HasFutureLessonInSlotAsync(tutorId, slotDayOfWeek, slotStartTime, slotEndTime);
                if (hasUpcomingLessons)
                    throw new InvalidOperationException(
                        $"Không thể cập nhật khung giờ {availability.Starttime:HH:mm}-{availability.Endtime:HH:mm} (ngày {availability.Dayofweek}) vì đang có buổi học được đặt lịch. Vui lòng hủy booking trước khi cập nhật.");
            }

            // Get all existing slots for this tutor excluding the ones being updated (for overlap check)
            var otherExistingSlots = await _context.Tutoravailabilities
                .Where(a => a.Tutorid == tutorId && !updateIds.Contains(a.Availabilityid))
                .ToListAsync();

            var processedNewSlots = new List<(int utcDay, TimeOnly utcStart, TimeOnly utcEnd, int itemId)>();

            foreach (var item in request.Availabilities)
            {
                var startTime = ParseTimeOnly(item.Starttime);
                var endTime = ParseTimeOnly(item.Endtime);

                if (startTime >= endTime)
                    throw new ArgumentException($"Giờ bắt đầu phải trước giờ kết thúc (slot ID: {item.Availabilityid}).");

                // FE sends UTC — use directly
                var utcStartDay = item.Dayofweek;
                var utcStartTime = startTime;
                var utcEndTime = endTime;

                var dayOtherSlots = otherExistingSlots.Where(a => a.Dayofweek == utcStartDay).ToList();
                foreach (var slot in dayOtherSlots)
                {
                    if (slot.Starttime.HasValue && slot.Endtime.HasValue &&
                        utcStartTime < slot.Endtime.Value && utcEndTime > slot.Starttime.Value)
                    {
                        throw new InvalidOperationException(
                            $"Slot {item.Starttime}-{item.Endtime} bị trùng với slot hiện tại: {slot.Starttime.Value:HH:mm} - {slot.Endtime.Value:HH:mm}");
                    }
                }

                // Check overlap with other items in this batch
                var conflicting = processedNewSlots.FirstOrDefault(ns =>
                    ns.utcDay == utcStartDay &&
                    utcStartTime < ns.utcEnd && utcEndTime > ns.utcStart);

                if (conflicting != default)
                    throw new InvalidOperationException(
                        $"Slot {item.Starttime}-{item.Endtime} bị trùng với slot khác trong cùng request (ID: {conflicting.itemId}).");

                processedNewSlots.Add((utcStartDay, utcStartTime, utcEndTime, item.Availabilityid));
            }

            // Apply updates to tracked entities
            foreach (var item in request.Availabilities)
            {
                var entity = availabilities.First(a => a.Availabilityid == item.Availabilityid);
                var processed = processedNewSlots.First(p => p.itemId == item.Availabilityid);

                entity.Dayofweek = processed.utcDay;
                entity.Starttime = processed.utcStart;
                entity.Endtime = processed.utcEnd;
            }

            await _context.SaveChangesAsync();

            return availabilities.Select(MapToResponse).ToList();
        }

        /// <summary>
        /// Get all availability slots for a tutor.
        /// Returns all slots in user timezone (+7), ordered by day and start time.
        /// </summary>
        public async Task<List<TutorAvailabilityResponse>> GetAvailabilitiesAsync(string tutorId)
        {
            var allSlots = await _context.Tutoravailabilities
                .Where(a => a.Tutorid == tutorId)
                .OrderBy(a => a.Dayofweek)
                .ThenBy(a => a.Starttime)
                .ToListAsync();

            return allSlots
                .Select(MapToResponse)
                .ToList();
        }

        /// <summary>
        /// Delete an availability slot (only owner can delete)
        /// </summary>
        public async Task<bool> DeleteAvailabilityAsync(string tutorId, int availabilityId)
        {
            var availability = await _context.Tutoravailabilities
                .FirstOrDefaultAsync(a => a.Availabilityid == availabilityId);

            if (availability == null)
            {
                return false; // Not found
            }

            // Security check: only owner can delete
            if (availability.Tutorid != tutorId)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền xóa slot lịch học này.");
            }

            var slotDayOfWeek = availability.Dayofweek!.Value;
            var slotStartTime = availability.Starttime!.Value.ToTimeSpan();
            var slotEndTime = availability.Endtime!.Value.ToTimeSpan();

            var hasUpcomingLessons = await HasFutureLessonInSlotAsync(tutorId, slotDayOfWeek, slotStartTime, slotEndTime);

            if (hasUpcomingLessons)
                throw new InvalidOperationException(
                    "Không thể xóa khung giờ này vì đang có buổi học được đặt lịch. Vui lòng hủy booking trước khi xóa.");

            _context.Tutoravailabilities.Remove(availability);
            await _context.SaveChangesAsync();

            return true;
        }

        /// <summary>
        /// Delete multiple availability slots at once (only owner can delete)
        /// </summary>
        public async Task<int> BulkDeleteAvailabilitiesAsync(string tutorId, BulkDeleteAvailabilityRequest request)
        {
            var availabilities = await _context.Tutoravailabilities
                .Where(a => request.AvailabilityIds.Contains(a.Availabilityid))
                .ToListAsync();

            if (availabilities.Count == 0)
            {
                return 0; // No records found
            }

            // Security check: all slots must belong to the owner
            var unauthorizedSlots = availabilities.Where(a => a.Tutorid != tutorId).ToList();
            if (unauthorizedSlots.Any())
            {
                throw new UnauthorizedAccessException(
                    $"Bạn không có quyền xóa {unauthorizedSlots.Count} slot(s) lịch học này.");
            }

            // Check for upcoming lessons in any of the slots
            foreach (var availability in availabilities)
            {
                var slotDayOfWeek = availability.Dayofweek!.Value;
                var slotStartTime = availability.Starttime!.Value.ToTimeSpan();
                var slotEndTime = availability.Endtime!.Value.ToTimeSpan();

                var hasUpcomingLessons = await HasFutureLessonInSlotAsync(tutorId, slotDayOfWeek, slotStartTime, slotEndTime);

                if (hasUpcomingLessons)
                {
                    throw new InvalidOperationException(
                        $"Không thể xóa khung giờ {availability.Starttime:HH:mm}-{availability.Endtime:HH:mm} (ngày {availability.Dayofweek}) vì đang có buổi học được đặt lịch. Vui lòng hủy booking trước khi xóa.");
                }
            }

            // All validations passed, delete all slots
            _context.Tutoravailabilities.RemoveRange(availabilities);
            await _context.SaveChangesAsync();

            return availabilities.Count;
        }

        /// <summary>
        /// Parses "HH:mm" time string to TimeOnly.
        /// "24:00" (end-of-day sentinel) is normalized to 23:59 — the maximum representable TimeOnly value
        /// for a same-day slot, since TimeOnly does not support midnight-of-next-day.
        /// </summary>
        private static TimeOnly ParseTimeOnly(string timeStr)
        {
            if (timeStr.Trim() == "24:00")
                return new TimeOnly(23, 59);
            return TimeOnly.Parse(timeStr);
        }

        private async Task EnsureFlexiblePackageAsync(string tutorId)
        {
            var hasFlexiblePackage = await _context.Tutorpackages
                .AnyAsync(p => p.Tutorid == tutorId
                    && p.Packagetype == Tutorpackage.FlexiblePackageType
                    && p.Isactive);

            if (hasFlexiblePackage)
            {
                return;
            }

            var now = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow;
            _context.Tutorpackages.Add(new Tutorpackage
            {
                Tutorid = tutorId,
                Name = DefaultFlexiblePackageName,
                Packagetype = Tutorpackage.FlexiblePackageType,
                Isactive = true,
                Createdat = now,
                Updatedat = now
            });
        }

        private async Task<bool> HasFutureLessonInSlotAsync(string tutorId, int utcDayOfWeek, TimeSpan utcSlotStart, TimeSpan utcSlotEnd)
        {
            var lessons = await _context.Lessons
                .Where(l => l.Tutorid == tutorId
                    && l.Scheduledstart > TimeZoneHelper.UtcNow
                    && l.Status != LessonStatus.Cancelled
                    && l.Status != LessonStatus.CancelledNoshow
                    && l.Status != LessonStatus.Completed
                    && l.Status != LessonStatus.NoShow)
                .Select(l => new { l.Scheduledstart, l.Scheduledend })
                .ToListAsync();

            return lessons.Any(l =>
            {
                // Lessons are stored in UTC, availability slots are now stored in UTC too
                // Convert C# DayOfWeek (0=Sunday, 1=Monday...) to ISO format (1=Monday, 7=Sunday)
                var lessonIsoDayOfWeek = l.Scheduledstart.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)l.Scheduledstart.DayOfWeek;

                return lessonIsoDayOfWeek == utcDayOfWeek
                    && l.Scheduledstart.TimeOfDay < utcSlotEnd
                    && l.Scheduledend.TimeOfDay > utcSlotStart;
            });
        }

        /// <summary>
        /// Map entity to response DTO. Returns UTC values — FE handles display conversion.
        /// </summary>
        private static TutorAvailabilityResponse MapToResponse(Tutoravailability entity)
        {
            return new TutorAvailabilityResponse
            {
                Availabilityid = entity.Availabilityid,
                Tutorid = entity.Tutorid ?? string.Empty,
                Dayofweek = entity.Dayofweek ?? 1,
                Starttime = entity.Starttime.HasValue ? entity.Starttime.Value.ToString("HH:mm") : "00:00",
                Endtime = entity.Endtime.HasValue ? entity.Endtime.Value.ToString("HH:mm") : "00:00",
                Createdat = entity.Createdat ?? TimeZoneHelper.UtcNow
            };
        }
    }
}
