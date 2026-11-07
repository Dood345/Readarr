#!/bin/sh
set -e

# Align the container user with the host's ownership so bind-mounted /config,
# /books and /downloads stay writable without resorting to running as root.
PUID=${PUID:-1000}
PGID=${PGID:-1000}
UMASK=${UMASK:-002}

if [ "$(id -u)" = "0" ]; then
    if [ "$(id -g readarr)" != "$PGID" ]; then
        groupmod -o -g "$PGID" readarr
    fi

    if [ "$(id -u readarr)" != "$PUID" ]; then
        usermod -o -u "$PUID" readarr
    fi

    # /books and /downloads are large bind mounts shared with other containers;
    # only the mount point itself is touched, never their contents.
    chown readarr:readarr /books /downloads 2>/dev/null || true

    # /config is only walked when its ownership is actually wrong, which is the
    # first-run-on-an-empty-volume case. Bind mounts from Windows and macOS
    # synthesise ownership at the driver level, so there a recursive chown changes
    # nothing while still stat-ing every file: over a multi-gigabyte database on a
    # 9p or virtiofs share that stalls startup for minutes before Readarr even runs.
    if [ "$(stat -c %u /config 2>/dev/null)" = "$PUID" ] && [ "$(stat -c %g /config 2>/dev/null)" = "$PGID" ]; then
        chown readarr:readarr /config 2>/dev/null || true
    else
        echo "[entrypoint] /config ownership differs from PUID:PGID, correcting recursively (first run may take a while)"
        chown -R readarr:readarr /config 2>/dev/null || true
    fi

    umask "$UMASK"

    exec gosu readarr "$@"
fi

umask "$UMASK"
exec "$@"
