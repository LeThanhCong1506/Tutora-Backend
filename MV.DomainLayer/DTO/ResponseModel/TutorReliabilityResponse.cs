namespace MV.DomainLayer.DTO.ResponseModel;

/// <summary>
/// Punctuality and reliability of one tutor across a date range, aggregated from the same
/// attendance evidence a single dispute is settled with.
///
/// Every rate is reported over <see cref="SessionsMeasured"/> — the sessions whose evidence was
/// good enough to judge — never over the whole range. A tutor whose lessons produced no Agora data
/// must not be scored as punctual, and one bad week of missing notifications must not read as a
/// perfect record.
/// </summary>
public class TutorReliabilityResponse
{
    public string TutorUserId { get; set; } = string.Empty;
    public string? TutorName { get; set; }

    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    /// <summary>Sessions scheduled in the range, cancelled ones excluded.</summary>
    public int SessionsInRange { get; set; }

    /// <summary>Sessions with usable presence evidence — the denominator of every rate below.</summary>
    public int SessionsMeasured { get; set; }

    /// <summary>Sessions we could not judge, because neither Agora nor the heartbeat had presence.</summary>
    public int SessionsWithoutEvidence { get; set; }

    // ── Late arrivals ────────────────────────────────────────────────────────

    public int LateCount { get; set; }

    /// <summary>LateCount / SessionsMeasured, rounded to 4 decimals. Null when nothing was measured.</summary>
    public double? LateRate { get; set; }

    /// <summary>Mean lateness over the late sessions only, in seconds. Null when there are none.</summary>
    public int? AverageLateSeconds { get; set; }

    public int? WorstLateSeconds { get; set; }

    // ── Early departures ─────────────────────────────────────────────────────

    public int EarlyLeaveCount { get; set; }
    public double? EarlyLeaveRate { get; set; }
    public int? AverageEarlyLeaveSeconds { get; set; }
    public int? WorstEarlyLeaveSeconds { get; set; }

    // ── No-shows ─────────────────────────────────────────────────────────────

    /// <summary>Sessions where the evidence was conclusive and showed no tutor presence at all.</summary>
    public int NoShowCount { get; set; }

    public double? NoShowRate { get; set; }

    // ── Integrity signals ────────────────────────────────────────────────────

    /// <summary>Sessions flagged as an unattended room — connected but nothing being taught.</summary>
    public int IdlePresenceCount { get; set; }

    /// <summary>Sessions where the tutor account was admitted from more than one network.</summary>
    public int MultipleNetworkCount { get; set; }

    /// <summary>Sessions where the tutor account was admitted from more than one device.</summary>
    public int MultipleDeviceCount { get; set; }

    /// <summary>Mean share of scheduled time both sides were present, over measured sessions.</summary>
    public double? AverageOverlapRatio { get; set; }

    /// <summary>Per-session rows behind the numbers, newest first, so a rate can always be audited.</summary>
    public List<TutorReliabilitySession> Sessions { get; set; } = [];
}

public class TutorReliabilitySession
{
    public int ClassSessionId { get; set; }
    public DateTime ScheduledStart { get; set; }
    public DateTime ScheduledEnd { get; set; }
    public string? Status { get; set; }

    public int? LateSeconds { get; set; }
    public int? EarlyLeaveSeconds { get; set; }

    public int OverlapSeconds { get; set; }
    public double OverlapRatio { get; set; }

    /// <summary>True only when the evidence was conclusive and showed no tutor presence.</summary>
    public bool IsNoShow { get; set; }

    /// <summary>False when this row was excluded from every rate.</summary>
    public bool IsMeasured { get; set; }

    /// <summary><c>agora</c>, <c>heartbeat</c> or null — how strong this row's timing is.</summary>
    public string? PunctualitySource { get; set; }

    public List<string> Flags { get; set; } = [];
}
