# External sky datasets

This directory contains third-party datasets that inform the SkyMonitor constellation overlays.

# Sky datasets

This directory contains third-party datasets that power the SkyMonitor overlays and star-catalog queries.

## Constellation figure data

- `ConstellationLines.csv` – stick-figure definitions for the 88 IAU constellations, sourced from the
  [Constellation Lines project](https://github.com/MarcvdSluys/ConstellationLines) by Marc van der Sluys.
  The data are available under the [Creative Commons Attribution 4.0 International licence](https://creativecommons.org/licenses/by/4.0/).
- `ConstellationLines.sqlite` – normalized SQLite copy of the CSV that splits each draw sequence into ordered star IDs for fast runtime queries.

Regenerate the constellation database at any time with:

```bash
python3 scripts/import-constellation-lines.py
```

The importer re-ingests the CSV and recreates the SQLite schema; feel free to extend the script if you want to augment the figures.

## Stellar catalog data

- `hyg_v42.sqlite` – snapshot of the [HYG Database v4.2](https://github.com/astronexus/HYG-Database) compiled by David Nash (Astronexus).
  The upstream project distributes the catalog under the [Creative Commons Attribution 4.0 International licence](https://creativecommons.org/licenses/by/4.0/).
