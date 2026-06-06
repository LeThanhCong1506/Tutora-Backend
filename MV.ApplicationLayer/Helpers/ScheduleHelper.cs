using MV.DomainLayer.DTO.RequestModel;
using MV.DomainLayer.DTO.ResponseModel;
using System.Text.Json;

namespace MV.ApplicationLayer.Helpers;

public static class ScheduleHelper
{
    public static bool Validate(List<ScheduleItemRequest> schedule)
    {
        if (schedule == null || schedule.Count == 0) return false;

        // Business validation: check for overlaps and start < end
        foreach (var s in schedule)
        {
            if (!TimeOnly.TryParse(s.StartTime, out var start) || !TimeOnly.TryParse(s.EndTime, out var end) || start >= end)
                return false;
        }

        // Check for overlapping slots on same day
        for (var i = 0; i < schedule.Count; i++)
            for (var j = i + 1; j < schedule.Count; j++)
            {
                if (schedule[i].DayOfWeek != schedule[j].DayOfWeek) continue;
                if (!TimeOnly.TryParse(schedule[i].StartTime, out var a1) || !TimeOnly.TryParse(schedule[i].EndTime, out var a2)) return false;
                if (!TimeOnly.TryParse(schedule[j].StartTime, out var b1) || !TimeOnly.TryParse(schedule[j].EndTime, out var b2)) return false;
                if (a1 < b2 && b1 < a2) return false; // Overlap detected
            }
        return true;
    }

    public static List<ScheduleItemRequest> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var schedule = JsonSerializer.Deserialize<List<ScheduleItemRequest>>(json, options);
            return schedule ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static List<ScheduleItemResponse>? ToResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var schedule = JsonSerializer.Deserialize<List<ScheduleItemRequest>>(json, options);
            if (schedule == null) return null;

            return schedule.Select(s => new ScheduleItemResponse
            {
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime,
                EndTime = s.EndTime
            }).ToList();
        }
        catch
        {
            return null;
        }
    }
}
