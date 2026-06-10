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
        private const string DefaultFlexiblePackageName = "Gói lịch rảnh linh hoạt";



        public TutorAvailabilityService(IAppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Add a new availability slot with overlap validation.
        /// Converts user timezone to UTC before saving to database.
        /// </summary>
        public async Task<TutorAvailabilityResponse> AddAvailabilityAsync(string tutorId, CreateAvailabilityRequest request)
        {
            // Parse time strings to TimeOnly — ParseTimeOnly handles "24:00" → 23:59
            var localStartTime = ParseTimeOnly(request.Starttime);
            var localEndTime = ParseTimeOnly(request.Endtime);

            // Business validation: starttime must be before endtime (in local time)
            if (localStartTime >= localEndTime)
            {
                throw new ArgumentException("Giờ bắt đầu phải trước giờ kết thúc.");
            }

            // Convert user timezone to UTC for storage
            var (utcStartDay, utcStartTime) = TimeZoneHelper.ShiftToUtc(request.Dayofweek, localStartTime);
            var (utcEndDay, utcEndTime) = TimeZoneHelper.ShiftToUtc(request.Dayofweek, localEndTime);

            // Check for overlapping slots on the same day (in UTC)
            var existingSlots = await _context.Tutoravailabilities
                .Where(a => a.Tutorid == tutorId && a.Dayofweek == utcStartDay)
                .ToListAsync();

            foreach (var slot in existingSlots)
            {
                if (slot.Starttime.HasValue && slot.Endtime.HasValue)
                {
                    // Check if new slot overlaps with existing slot (both in UTC)
                    // Overlap occurs if: newStart < existingEnd AND newEnd > existingStart
                    if (utcStartTime < slot.Endtime.Value && utcEndTime > slot.Starttime.Value)
                    {
                        // Convert existing slot back to user timezone for error message
                        var (_, existingLocalStart) = TimeZoneHelper.ShiftToUserTime(slot.Dayofweek ?? 1, slot.Starttime.Value);
                        var (_, existingLocalEnd) = TimeZoneHelper.ShiftToUserTime(slot.Dayofweek ?? 1, slot.Endtime.Value);
                        
                        throw new InvalidOperationException(
                            $"Time slot overlaps with existing slot: {existingLocalStart:HH:mm} - {existingLocalEnd:HH:mm}");
                    }
                }
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

            _context.Tutoravailabilities.Add(availability);
            await EnsureFlexiblePackageAsync(tutorId);
            await _context.SaveChangesAsync();

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
                var localStartTime = ParseTimeOnly(req.Starttime);
                var localEndTime = ParseTimeOnly(req.Endtime);

                // Business validation: starttime must be before endtime (in local time)
                if (localStartTime >= localEndTime)
                {
                    throw new ArgumentException($"Giờ bắt đầu phải trước giờ kết thúc (slot: {req.Starttime} - {req.Endtime}).");
                }

                // Convert user timezone to UTC
                var (utcStartDay, utcStartTime) = TimeZoneHelper.ShiftToUtc(req.Dayofweek, localStartTime);
                var (utcEndDay, utcEndTime) = TimeZoneHelper.ShiftToUtc(req.Dayofweek, localEndTime);

                // Check overlap with existing slots on the same day (in UTC)
                var daySlots = existingSlots.Where(a => a.Dayofweek == utcStartDay).ToList();
                foreach (var slot in daySlots)
                {
                    if (slot.Starttime.HasValue && slot.Endtime.HasValue)
                    {
                        if (utcStartTime < slot.Endtime.Value && utcEndTime > slot.Starttime.Value)
                        {
                            // Convert to user timezone for error message
                            var (_, existingLocalStart) = TimeZoneHelper.ShiftToUserTime(slot.Dayofweek ?? 1, slot.Starttime.Value);
                            var (_, existingLocalEnd) = TimeZoneHelper.ShiftToUserTime(slot.Dayofweek ?? 1, slot.Endtime.Value);
                            
                            throw new InvalidOperationException(
                                $"Time slot {req.Starttime}-{req.Endtime} overlaps with existing slot: {existingLocalStart:HH:mm} - {existingLocalEnd:HH:mm}");
                        }
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

            // Map to response
            results = newSlots.Select(MapToResponse).ToList();

            return results;
        }

        /// <summary>
        /// Get all availability slots for a tutor.
        /// Returns all slots in user timezone, ordered by day and start time.
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
        /// Map entity to response DTO.
        /// Converts UTC time back to user timezone for display.
        /// </summary>
        private static TutorAvailabilityResponse MapToResponse(Tutoravailability entity)
        {
            // Convert UTC back to user timezone for response
            var utcDay = entity.Dayofweek ?? 1;
            var utcStartTime = entity.Starttime ?? TimeOnly.MinValue;
            var utcEndTime = entity.Endtime ?? TimeOnly.MinValue;

            var (localDay, localStartTime) = TimeZoneHelper.ShiftToUserTime(utcDay, utcStartTime);
            var (_, localEndTime) = TimeZoneHelper.ShiftToUserTime(utcDay, utcEndTime);

            return new TutorAvailabilityResponse
            {
                Availabilityid = entity.Availabilityid,
                Tutorid = entity.Tutorid ?? string.Empty,
                Dayofweek = localDay,
                Starttime = localStartTime.ToString("HH:mm"),
                Endtime = localEndTime.ToString("HH:mm"),
                Createdat = TimeZoneHelper.ToUserTime(entity.Createdat ?? TimeZoneHelper.UtcNow)
            };
        }
    }
}
