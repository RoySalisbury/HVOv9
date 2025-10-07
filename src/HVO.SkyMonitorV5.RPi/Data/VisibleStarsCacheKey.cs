using System;

namespace HVO.SkyMonitorV5.RPi.Data;

/*
We bucket utc to the hour—great for starfields where minute-by-minute changes are visually negligible but you still want time awareness. If you need finer granularity, change to DateTime.Utc truncated to 10–15 minutes.
*/
public readonly record struct VisibleStarsCacheKey(
    double LatitudeDeg,
    double LongitudeDeg,
    DateTime UtcHour,      // hour-bucketed time to avoid per-minute churn
    double MagnitudeLimit,
    double MinMaxAltitudeDeg,
    int TopN,
    bool Stratified,
    int RaBins,
    int DecBands,
    int ScreenWidth,
    int ScreenHeight
);


