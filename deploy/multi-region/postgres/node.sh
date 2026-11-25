#!/bin/sh
# Starts one PostgreSQL server of the lab. Its data decides whether it's the primary or a replica, so a
# promoted replica stays the primary after a restart.
#
#   FIRST_START  "primary" creates an empty database, "replica" copies the primary
#   PRIMARIES    libpq hosts and ports where the primary may be; whichever is the primary is used
set -eu

# libpq skips the hosts that aren't the primary.
conninfo="$PRIMARIES user=messenger password=messenger dbname=postgres target_session_attrs=primary connect_timeout=3"

wait_for_primary() {
    until psql "$conninfo" -qAtc 'select 1' >/dev/null 2>&1; do
        echo "Waiting for a primary"
        sleep 2
    done
}

if [ "$FIRST_START" = replica ] && [ ! -s "$PGDATA/PG_VERSION" ]; then
    wait_for_primary
    # A copy that was interrupted would be unusable.
    rm -rf "${PGDATA:?}"/*
    # -R writes standby.signal, which makes the copy a replica.
    pg_basebackup --dbname="$conninfo" --pgdata="$PGDATA" --wal-method=stream -R
    chmod 700 "$PGDATA"
fi

# This server was the primary, but another one has been promoted meanwhile (see README.md). Starting as
# it is would make two primaries. The check only works while the other primary is reachable, so the
# README also marks the old primary as fenced before promoting.
if [ -s "$PGDATA/PG_VERSION" ] && [ ! -f "$PGDATA/standby.signal" ] && psql "$conninfo" -qAtc 'select 1' >/dev/null 2>&1; then
    echo "Another primary exists; this server will follow it"
    touch "$PGDATA/pingo.fenced"
fi

# Take back everything the new primary doesn't have, and follow it.
if [ -f "$PGDATA/pingo.fenced" ]; then
    wait_for_primary
    # A killed server leaves its lock file. It names PID 1, which in this new container is this script,
    # so pg_rewind would think a server is still running here. None is: this container has just started.
    rm -f "$PGDATA/postmaster.pid"
    # A server that was killed needs crash recovery first. pg_rewind would run it itself, but without
    # wal_log_hints, and then refuse to continue because that setting is off.
    postgres --single -F -c wal_log_hints=on -D "$PGDATA" template1 </dev/null
    pg_rewind --target-pgdata="$PGDATA" --source-server="$conninfo" --write-recovery-conf
    rm -f "$PGDATA/pingo.fenced"
fi

# wal_keep_size: how far a stopped replica may fall behind and still catch up.
# wal_log_hints: pg_rewind needs it.
exec docker-entrypoint.sh postgres \
    -c hba_file=/etc/pingo/pg_hba.conf \
    -c wal_keep_size=256MB \
    -c wal_log_hints=on \
    -c primary_conninfo="$conninfo"
