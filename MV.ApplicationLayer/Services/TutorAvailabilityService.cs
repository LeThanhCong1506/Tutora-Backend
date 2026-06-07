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
                .Where(a => a.Tutorid == tutorId && a.Dayofweek == request.Dayofweek && a.Isactive)
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
                Createdat = MV.DomainLayer.Helpers.VietnamTimeHelper.Now
            };

            _context.Tutoravailabilities.Add(availability);
            await _context.SaveChangesAsync();

            return MapToResponse(availability);
        }

        /// <summary>
        /// Get all active availability slots for a tutor.
        /// Returns all slots where Isactive = true, ordered by day and start time.
        /// </summary>
        public async Task<List<TutorAvailabilityResponse>> GetAvailabilitiesAsync(string tutorId)
        {
            var allSlots = await _context.Tutoravailabilities
                .Where(a => a.Tutorid == tutorId && a.Isactive)
                .OrderBy(a => a.Dayofweek)
                .ThenBy(a => a.Starttime)
                .ToListAsync();

            return allSlots
                .Select(MapToResponse)
                .ToList();
        }

        /// <summary>
        /// Update an existing availability slot with overlap validation
        /// </summary>
        public async Task<TutorAvailabilityResponse> UpdateAvailabilityAsync(string tutorId, int availabilityId, UpdateAvailabilityRequest request)
        {
            // Parse time strings to TimeOnly — ParseTimeOnly handles "24:00" → 23:59
            var startTime = ParseTimeOnly(request.Starttime);
            var endTime = ParseTimeOnly(request.Endtime);

            // Business validation: starttime must be before endtime
            if (startTime >= endTime)
            {
                throw new ArgumentException("Giờ bắt đầu phải trước giờ kết thúc.");
            }

            var availability = await _context.Tutoravailabilities
                .FirstOrDefaultAsync(a => a.Availabilityid == availabilityId);

            if (availability == null)
            {
                throw new KeyNotFoundException("Không tìm thấy slot lịch học.");
            }

            // Security check: only owner can update
            if (availability.Tutorid != tutorId)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền cập nhật slot lịch học này.");
            }

            // ── Kiểm tra conflict: có lesson/reservation đang dùng slot hiện tại không ──
            var oldDayOfWeek = availability.Dayofweek!.Value;
            var oldSlotStart = availability.Starttime!.Value.ToTimeSpan();
            var oldSlotEnd   = availability.Endtime!.Value.ToTimeSpan();

            var hasUpcomingLessons = await HasFutureLessonInSlotAsync(tutorId, oldDayOfWeek, oldSlotStart, oldSlotEnd);

            if (hasUpcomingLessons)
                throw new InvalidOperationException(
                    "Không thể chỉnh sửa khung giờ này vì đang có buổi học được đặt lịch. Vui lòng hủy booking trước khi thay đổi.");

            // Check for overlapping slots on the same day (excluding the current one)
            var existingSlots = await _context.Tutoravailabilities
                .Where(a => a.Tutorid == tutorId && a.Dayofweek == request.Dayofweek && a.Availabilityid != availabilityId && a.Isactive)
                .ToListAsync();

            foreach (var slot in existingSlots)
            {
                if (slot.Starttime.HasValue && slot.Endtime.HasValue)
                {
                    // Check if new slot overlaps with existing slot
                    if (startTime < slot.Endtime.Value && endTime > slot.Starttime.Value)
                    {
                        throw new InvalidOperationException(
                            $"Time slot overlaps with existing slot: {slot.Starttime.Value:HH:mm} - {slot.Endtime.Value:HH:mm}");
                    }
                }
            }

            // Update availability entity
            availability.Dayofweek = request.Dayofweek;
            availability.Starttime = startTime;
            availability.Endtime = endTime;
            // CreatedAt remains unchanged

            _context.Tutoravailabilities.Update(availability);
            await _context.SaveChangesAsync();

            return MapToResponse(availability);
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

            availability.Isactive = false;
            await _context.SaveChangesAsync();

            return true;
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

        private async Task<bool> HasFutureLessonInSlotAsync(string tutorId, int dayOfWeek, TimeSpan slotStart, TimeSpan slotEnd)
        {
            var lessons = await _context.Lessons
                .Where(l => l.Tutorid == tutorId
                    && l.Scheduledstart > MV.DomainLayer.Helpers.VietnamTimeHelper.Now
                    && l.Status != LessonStatus.Cancelled
                    && l.Status != LessonStatus.CancelledNoshow
                    && l.Status != LessonStatus.Completed
                    && l.Status != LessonStatus.NoShow)
                .Select(l => new { l.Scheduledstart, l.Scheduledend })
                .ToListAsync();

            return lessons.Any(l =>
            {
                var startVn = VietnamTimeHelper.ToVietnamTime(l.Scheduledstart);
                var endVn = VietnamTimeHelper.ToVietnamTime(l.Scheduledend);
                return (int)startVn.DayOfWeek == dayOfWeek
                    && startVn.TimeOfDay < slotEnd
                    && endVn.TimeOfDay > slotStart;
            });
        }

        /// <summary>
        /// Map entity to response DTO.
        /// IsActive reflects the DB Isactive flag directly.
        /// </summary>
        private static TutorAvailabilityResponse MapToResponse(Tutoravailability entity)
        {
            return new TutorAvailabilityResponse
            {
                Availabilityid = entity.Availabilityid,
                Tutorid = entity.Tutorid ?? string.Empty,
                Dayofweek = entity.Dayofweek ?? 0,
                Starttime = entity.Starttime?.ToString("HH:mm") ?? string.Empty,
                Endtime = entity.Endtime?.ToString("HH:mm") ?? string.Empty,
                Createdat = VietnamTimeHelper.ToVietnamTime(entity.Createdat ?? MV.DomainLayer.Helpers.VietnamTimeHelper.Now),
                IsActive = entity.Isactive
            };
        }
    }
}
