using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace MV.DomainLayer.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public class TimeRangeAttribute : ValidationAttribute
{
    private readonly TimeSpan _minTime;
    private readonly TimeSpan _maxTime;

    public TimeRangeAttribute(string minTime, string maxTime)
    {
        _minTime = TimeSpan.ParseExact(minTime, @"hh\:mm", CultureInfo.InvariantCulture);
        _maxTime = TimeSpan.ParseExact(maxTime, @"hh\:mm", CultureInfo.InvariantCulture);
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value == null) return ValidationResult.Success;

        if (value is not string timeString)
            return new ValidationResult("Invalid time format.");

        if (!TimeSpan.TryParseExact(timeString, @"hh\:mm", CultureInfo.InvariantCulture, out var time))
            return new ValidationResult("Time must be in HH:mm format.");

        if (time < _minTime || time > _maxTime)
            return new ValidationResult($"Time must be between {_minTime:hh\\:mm} and {_maxTime:hh\\:mm}.");

        return ValidationResult.Success;
    }
}
