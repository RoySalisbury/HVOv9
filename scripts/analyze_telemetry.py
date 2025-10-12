#!/usr/bin/env python3
"""Summarize SkyMonitor telemetry SQLite databases.

Usage:
    python scripts/analyze_telemetry.py path1.db [path2.db ...]

Outputs JSON summary to stdout capturing key metrics per database.
"""
from __future__ import annotations

import json
import sqlite3
import sys
from collections import defaultdict
from dataclasses import dataclass, asdict
from pathlib import Path
from statistics import mean

@dataclass
class QueueMetrics:
    samples: int
    avg_stack_ms: float
    avg_filter_ms: float
    avg_queue_latency_ms: float
    avg_fill_pct: float
    avg_depth: float
    capacity: float
    max_depth: int
    max_latency_ms: float
    avg_queue_memory_mb: float

@dataclass
class ProcessingMetrics:
    samples: int
    avg_processing_ms: float
    avg_enqueue_wait_ms: float
    peak_processing_ms: float
    peak_enqueue_wait_ms: float
    avg_depth: float
    capacity: float
    backpressure_events: int

@dataclass
class CapturePacingMetrics:
    samples: int
    avg_adjusted_delay_ms: float
    avg_pressure_delay_ms: float
    penalty_seconds: float
    penalty_rate: float
    queue_pressure_level_avg: float

@dataclass
class FilterMetrics:
    filter_name: str
    avg_duration_ms: float
    last_duration_ms: float

@dataclass
class TelemetrySummary:
    path: str
    queue: QueueMetrics | None
    processing: ProcessingMetrics | None
    capture: CapturePacingMetrics | None
    filters: list[FilterMetrics]


def _coalesce(rows, index, default=0.0):
    values = [row[index] for row in rows if row[index] is not None]
    return mean(values) if values else default


def _fetchall(cursor, query: str):
    cursor.execute(query)
    return cursor.fetchall()


def summarize_db(path: Path) -> TelemetrySummary:
    conn = sqlite3.connect(path)
    conn.row_factory = sqlite3.Row
    cur = conn.cursor()

    queue_rows = _fetchall(cur, """
        SELECT queue_fill_percentage, queue_depth, queue_capacity,
               queue_latency_ms, stack_duration_ms, filter_duration_ms,
               queue_memory_mb
        FROM background_stacker_sample
    """)
    queue_summary = None
    if queue_rows:
        queue_summary = QueueMetrics(
            samples=len(queue_rows),
            avg_stack_ms=_coalesce(queue_rows, "stack_duration_ms"),
            avg_filter_ms=_coalesce(queue_rows, "filter_duration_ms"),
            avg_queue_latency_ms=_coalesce(queue_rows, "queue_latency_ms"),
            avg_fill_pct=_coalesce(queue_rows, "queue_fill_percentage"),
            avg_depth=_coalesce(queue_rows, "queue_depth"),
            capacity=_coalesce(queue_rows, "queue_capacity"),
            max_depth=max(row["queue_depth"] for row in queue_rows),
            max_latency_ms=max((row["queue_latency_ms"] or 0) for row in queue_rows),
            avg_queue_memory_mb=_coalesce(queue_rows, "queue_memory_mb"),
        )

    processing_rows = _fetchall(cur, """
        SELECT avg_processing_ms, avg_enqueue_wait_ms, peak_processing_ms,
               peak_enqueue_wait_ms, depth, capacity, backpressure_events
        FROM processing_queue_sample
    """)
    processing_summary = None
    if processing_rows:
        processing_summary = ProcessingMetrics(
            samples=len(processing_rows),
            avg_processing_ms=_coalesce(processing_rows, "avg_processing_ms"),
            avg_enqueue_wait_ms=_coalesce(processing_rows, "avg_enqueue_wait_ms"),
            peak_processing_ms=max((row["peak_processing_ms"] or 0) for row in processing_rows),
            peak_enqueue_wait_ms=max((row["peak_enqueue_wait_ms"] or 0) for row in processing_rows),
            avg_depth=_coalesce(processing_rows, "depth"),
            capacity=_coalesce(processing_rows, "capacity"),
            backpressure_events=sum(row["backpressure_events"] or 0 for row in processing_rows),
        )

    capture_rows = _fetchall(cur, """
        SELECT adjusted_delay_ms, pressure_delay_ms, penalty_delay_ms,
               penalty_active, queue_pressure_level
        FROM capture_pacing_sample
    """)
    capture_summary = None
    if capture_rows:
        penalty_seconds = sum((row["penalty_delay_ms"] or 0) / 1000.0 for row in capture_rows if row["penalty_active"])
        capture_summary = CapturePacingMetrics(
            samples=len(capture_rows),
            avg_adjusted_delay_ms=_coalesce(capture_rows, "adjusted_delay_ms"),
            avg_pressure_delay_ms=_coalesce(capture_rows, "pressure_delay_ms"),
            penalty_seconds=penalty_seconds,
            penalty_rate=sum(1 for row in capture_rows if row["penalty_active"]) / len(capture_rows),
            queue_pressure_level_avg=_coalesce(capture_rows, "queue_pressure_level"),
        )

    filter_rows = _fetchall(cur, """
        SELECT filter_name, average_duration_ms, last_duration_ms
        FROM filter_metric_sample
        ORDER BY captured_at_utc
    """)
    filters_map: dict[str, list[tuple[float, float]]] = defaultdict(list)
    for filter_name, avg_dur, last_dur in filter_rows:
        filters_map[filter_name].append((avg_dur or 0.0, last_dur or 0.0))

    filters_summary = [
        FilterMetrics(
            filter_name=name,
            avg_duration_ms=mean(val[0] for val in values if val[0]),
            last_duration_ms=values[-1][1] if values else 0.0,
        )
        for name, values in sorted(filters_map.items())
    ]

    conn.close()

    return TelemetrySummary(
        path=str(path),
        queue=queue_summary,
        processing=processing_summary,
        capture=capture_summary,
        filters=filters_summary,
    )


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print("Usage: python scripts/analyze_telemetry.py <db> [<db> ...]", file=sys.stderr)
        return 1

    summaries = [summarize_db(Path(arg)) for arg in argv[1:]]
    def serialize(obj):
        if hasattr(obj, "__dict__"):
            return {k: serialize(v) for k, v in obj.__dict__.items() if v is not None}
        if isinstance(obj, list):
            return [serialize(v) for v in obj]
        return obj

    data = [serialize(summary) for summary in summaries]
    json.dump(data, sys.stdout, indent=2)
    print()
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
