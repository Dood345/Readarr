#!/usr/bin/env bash
# Regenerates the committed Open Library payloads used by BibliographyAccuracyFixture.
#
# The field list MUST stay identical to OpenLibraryProxy.SearchFields. If it drifts, the fixtures
# carry data the provider never sees (or lack data it relies on) and the accuracy scores measure
# something other than production behaviour. An earlier version of this corpus omitted author_name
# and the anthology signal silently measured as useless.
#
# Run from this directory. Re-scoring after a refresh is expected to move the numbers a little;
# Open Library's catalogue drifts. Investigate a fall in recall, not a fall in junk.
set -euo pipefail

FIELDS="key,title,subtitle,first_publish_year,cover_i,edition_key,isbn,subject,author_key,author_name,ratings_average,ratings_count,number_of_pages_median,language"
LIMIT=500

fetch() {
    local key="$1" out="$2"
    echo "fetching $key -> $out"
    curl -sf -H "User-Agent: Readarr/1.0" \
        "https://openlibrary.org/search.json?author_key=${key}&fields=${FIELDS}&limit=${LIMIT}" \
        -o "$out"
}

fetch OL79034A   frank_herbert_works.json
fetch OL2629960A trudi_canavan_works.json
fetch OL1194290A brian_herbert_works.json

echo
echo "Done. Expected bibliographies in expected_bibliographies.json are hand-curated and are NOT"
echo "regenerated here - update them by hand when an author publishes something new."
