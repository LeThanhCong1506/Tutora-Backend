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
        /// Add a new availability slot with overlap validation
        /// </summary>
        public async Task<TutorAvailabilityResponse> AddAvailabilityAsync(string tutorId, CreateAvailabilityRequest request)
        {
            // Parse time strings to TimeOnly — ParseTimeOnly handles "24:00" → 23:59
            var startTime = ParseTimeOnly(request.Starttime);
            var endTime = ParseTimeOnly(request.Endtime);

            // Business validation: starttime must be before endtime
            if (startTime >= endTime)
            {
                throw new ArgumentException("Giờ bắt đầu phải trước giờ kết thúc.");
            }

            // Check for overlapping slots on the same day
            var existingSlots = await _context.Tutoravailabilities
                .Where(a => a.Tutorid == tutorId && a.Dayofweek == request.Dayofweek)
                .ToListAsync();

            foreach (var slot in existingSlots)
            {
                if (slot.Starttime.HasValue && slot.Endtime.HasValue)
                {
                    // Check if new slot overlaps with existing slot
                    // Overlap occurs if: newStart < existingEnd AND newEnd > existingStart
                    if (startTime < slot.Endtime.Value && endTime > slot.Starttime.Value)
                    {
                        throw new InvalidOperationException(
                            $"Time slot overlaps with existing slot: {slot.Starttime.Value:HH:mm} - {slot.Endtime.Value:HH:mm}");
                    }
                }
            }

            // Create new availability entity
            var availability = new Tutoravailability
            {
                Tutorid = tutorId,
                Dayofweek = request.Dayofweek,
                Starttime = startTime,
                Endtime = endTime,
                Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
            };

            _context.Tutoravailabilities.Add(availability);
            await EnsureFlexiblePackageAsync(tutorId);
            await _context.SaveChangesAsync();

            return MapToResponse(availability);
        }

        /// <summary>
        /// Add multiple availability slots at once with overlap validation
        /// </summary>
        public async Task<List<TutorAvailabilityResponse>> BulkAddAvailabilitiesAsync(string tutorId, BulkCreateAvailabilityRequest request)
        {
            var results = new List<TutorAvailabilityResponse>();
            var newSlots = new List<Tutoravailability>();

            // Get all existing slots for this tutor
            var existingSlots = await _context.Tutoravailabilities
                .Where(a => a.Tutorid == tutorId)
                .ToListAsync();

            // Parse and validate all new slots
            foreach (var req in request.Availabilities)
            {
                var startTime = ParseTimeOnly(req.Starttime);
                var endTime = ParseTimeOnly(req.Endtime);

                // Business validation: starttime must be before endtime
                if (startTime >= endTime)
                {
                    throw new ArgumentException($"Giờ bắt đầu phải trước giờ kết thúc (slot: {req.Starttime} - {req.Endtime}).");
                }

                // Check overlap with existing slots on the same day
                var daySlots = existingSlots.Where(a => a.Dayofweek == req.Dayofweek).ToList();
                foreach (var slot in daySlots)
                {
                    if (slot.Starttime.HasValue && slot.Endtime.HasValue)
                    {
                        if (startTime < slot.Endtime.Value && endTime > slot.Starttime.Value)
                        {
                            throw new InvalidOperationException(
                                $"Time slot {req.Starttime}-{req.Endtime} overlaps with existing slot: {slot.Starttime.Value:HH:mm} - {slot.Endtime.Value:HH:mm}");
                        }
                    }
                }

                // Check overlap with other new slots in this batch
                var conflictingNewSlot = newSlots.FirstOrDefault(ns =>
                    ns.Dayofweek == req.Dayofweek &&
                    ns.Starttime.HasValue && ns.Endtime.HasValue &&
                    startTime < ns.Endtime.Value && endTime > ns.Starttime.Value);

                if (conflictingNewSlot != null)
                {
                    throw new InvalidOperationException(
                        $"Time slot {req.Starttime}-{req.Endtime} overlaps with another slot in the request: {conflictingNewSlot.Starttime.Value:HH:mm} - {conflictingNewSlot.Endtime.Value:HH:mm}");
                }

                // Create new availability entity
                var availability = new Tutoravailability
                {
                    Tutorid = tutorId,
                    Dayofweek = req.Dayofweek,
                    Starttime = startTime,
                    Endtime = endTime,
                    Createdat = MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
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
        /// Returns all slots, ordered by day and start time.
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

        private async Task<bool> HasFutureLessonInSlotAsync(string tutorId, int dayOfWeek, TimeSpan slotStart, TimeSpan slotEnd)
        {
            var lessons = await _context.Lessons
                .Where(l => l.Tutorid == tutorId
                    && l.Scheduledstart > MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow
                    && l.Status != LessonStatus.Cancelled
                    && l.Status != LessonStatus.CancelledNoshow
                    && l.Status != LessonStatus.Completed
                    && l.Status != LessonStatus.NoShow)
                .Select(l => new { l.Scheduledstart, l.Scheduledend })
                .ToListAsync();

            return lessons.Any(l =>
            {
                var startVn = TimeZoneHelper.ToUserTime(l.Scheduledstart);
                var endVn = TimeZoneHelper.ToUserTime(l.Scheduledend);

                // Convert C# DayOfWeek (0=Sunday, 1=Monday...) to ISO format (1=Monday, 7=Sunday)
                var isoDayOfWeek = startVn.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)startVn.DayOfWeek;

                return isoDayOfWeek == dayOfWeek
                    && startVn.TimeOfDay < slotEnd
                    && endVn.TimeOfDay > slotStart;
            });
        }

        /// <summary>
        /// Map entity to response DTO.
        /// </summary>
        private static TutorAvailabilityResponse MapToResponse(Tutoravailability entity)
        {
            return new TutorAvailabilityResponse
            {
                Availabilityid = entity.Availabilityid,
                Tutorid = entity.Tutorid ?? string.Empty,
                Dayofweek = entity.Dayofweek ?? 1,
                Starttime = entity.Starttime?.ToString("HH:mm") ?? string.Empty,
                Endtime = entity.Endtime?.ToString("HH:mm") ?? string.Empty,
                Createdat = TimeZoneHelper.ToUserTime(entity.Createdat ?? MV.DomainLayer.Helpers.TimeZoneHelper.UtcNow)
            };
        }
    }
}
