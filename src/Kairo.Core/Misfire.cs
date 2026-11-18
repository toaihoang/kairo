namespace Kairo;

/// <summary>
/// What a recurring job does with occurrences that were missed, for example while the app was down.
/// An occurrence is missed when it is older than <see cref="KairoOptions.MisfireThreshold"/>.
/// </summary>
public enum Misfire
{
    /// <summary>Run once for all missed occurrences, then continue on schedule.</summary>
    FireOnce = 0,

    /// <summary>Run once for every missed occurrence (at most 100), then continue on schedule.</summary>
    FireAll = 1,

    /// <summary>Drop missed occurrences and continue on schedule.</summary>
    Skip = 2,
}
