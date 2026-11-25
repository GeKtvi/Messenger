#!/bin/sh
# Starts PostgreSQL as a read replica. On first start it copies the whole primary; after that it only
# follows the primary's changes.
set -eu

if [ ! -s "$PGDATA/PG_VERSION" ]; then
    # A copy that was interrupted would be unusable.
    rm -rf "${PGDATA:?}"/*
    # -R writes standby.signal and the primary's address, which make the copy a replica.
    pg_basebackup --host="$PRIMARY_HOST" --username=messenger --pgdata="$PGDATA" --wal-method=stream -R
    chmod 700 "$PGDATA"
fi

exec postgres -c hba_file=/etc/pingo/pg_hba.conf
