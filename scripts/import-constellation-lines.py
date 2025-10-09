#!/usr/bin/env python3
"""Import constellation line definitions into a SQLite database.

This script downloads/parses the ConstellationLines.csv dataset that lists the
stick-figure segments for the 88 constellations. It creates a normalized SQLite
schema so the SkyMonitor pipeline can query segments efficiently.
"""
from __future__ import annotations

import csv
import sqlite3
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

REPO_ROOT = Path(__file__).resolve().parents[1]
DATA_DIR = REPO_ROOT / "src" / "HVO.SkyMonitorV5.RPi" / "Data"
CSV_PATH = DATA_DIR / "ConstellationLines.csv"
DB_PATH = DATA_DIR / "ConstellationLines.sqlite"

STAR_COLUMNS: tuple[str, ...] = tuple(f"s{i:02d}" for i in range(1, 32))


@dataclass
class ConstellationLine:
    constellation: str
    line_number: int
    star_ids: tuple[int, ...]


class ConstellationImporter:
    def __init__(self, csv_path: Path, db_path: Path) -> None:
        self._csv_path = csv_path
        self._db_path = db_path

    def load(self) -> list[ConstellationLine]:
        if not self._csv_path.exists():
            raise FileNotFoundError(f"CSV file not found: {self._csv_path}")

        lines: list[ConstellationLine] = []
        with self._csv_path.open(newline="", encoding="utf-8") as handle:
            reader = csv.DictReader(handle, skipinitialspace=True)

            current_constellation = ""
            per_constellation_counter: dict[str, int] = {}

            for raw in reader:
                abbr = (raw.get("abr") or "").strip()
                if abbr:
                    current_constellation = abbr
                    per_constellation_counter.setdefault(abbr, 0)
                elif not current_constellation:
                    # CSV may start with blank abbreviation; treat as invalid row.
                    continue

                star_total = self._parse_int(raw.get("nr"))
                star_ids = tuple(
                    sid for sid in (
                        self._parse_int(raw.get(column)) for column in STAR_COLUMNS
                    )
                    if sid is not None
                )

                if star_total is not None and star_total != len(star_ids):
                    raise ValueError(
                        f"Row for {current_constellation} expected {star_total} stars but parsed {len(star_ids)}"
                    )

                if not star_ids:
                    continue

                per_constellation_counter[current_constellation] += 1
                line_number = per_constellation_counter[current_constellation]

                lines.append(
                    ConstellationLine(
                        constellation=current_constellation,
                        line_number=line_number,
                        star_ids=star_ids,
                    )
                )

        return lines

    def import_to_db(self, lines: Iterable[ConstellationLine]) -> None:
        self._db_path.parent.mkdir(parents=True, exist_ok=True)

        with sqlite3.connect(self._db_path) as conn:
            conn.execute("PRAGMA journal_mode = WAL;")
            conn.execute("PRAGMA synchronous = NORMAL;")

            self._create_schema(conn)
            self._insert_lines(conn, lines)
            conn.commit()

    @staticmethod
    def _create_schema(conn: sqlite3.Connection) -> None:
        conn.executescript(
            """
            DROP TABLE IF EXISTS constellation_line_star;
            DROP TABLE IF EXISTS constellation_line;

            CREATE TABLE constellation_line (
                line_id       INTEGER PRIMARY KEY AUTOINCREMENT,
                constellation TEXT    NOT NULL,
                line_number   INTEGER NOT NULL,
                star_count    INTEGER NOT NULL
            );

            CREATE TABLE constellation_line_star (
                line_id        INTEGER NOT NULL,
                sequence_index INTEGER NOT NULL,
                bsc_number     INTEGER NOT NULL,
                PRIMARY KEY (line_id, sequence_index),
                FOREIGN KEY (line_id) REFERENCES constellation_line(line_id) ON DELETE CASCADE
            );

            CREATE INDEX idx_constellation_line_constellation
                ON constellation_line (constellation);
            CREATE INDEX idx_constellation_line_star_constellation
                ON constellation_line_star (line_id, sequence_index);
            """
        )

    @staticmethod
    def _insert_lines(conn: sqlite3.Connection, lines: Iterable[ConstellationLine]) -> None:
        cursor = conn.cursor()
        try:
            for line in lines:
                cursor.execute(
                    "INSERT INTO constellation_line (constellation, line_number, star_count) VALUES (?, ?, ?)",
                    (line.constellation, line.line_number, len(line.star_ids)),
                )
                line_id = cursor.lastrowid

                cursor.executemany(
                    "INSERT INTO constellation_line_star (line_id, sequence_index, bsc_number) VALUES (?, ?, ?)",
                    ((line_id, index + 1, star_id) for index, star_id in enumerate(line.star_ids)),
                )
        finally:
            cursor.close()

    @staticmethod
    def _parse_int(raw: str | None) -> int | None:
        if raw is None:
            return None
        stripped = raw.strip()
        if not stripped:
            return None
        return int(stripped)


def main() -> None:
    importer = ConstellationImporter(CSV_PATH, DB_PATH)
    lines = importer.load()
    importer.import_to_db(lines)
    print(f"Imported {len(lines)} constellation line definitions into {DB_PATH.name}")


if __name__ == "__main__":
    main()
