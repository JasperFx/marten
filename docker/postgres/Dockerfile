# Custom PostgreSQL image for Marten local development.
#
# Layers PostGIS 3 (`postgresql-17-postgis-3`) and pgvector
# (`postgresql-17-pgvector`) on top of the official multi-arch `postgres:17`
# image. Both Debian packages are available for amd64 and arm64, so this
# builds and runs on Apple-silicon hosts without emulation.
#
# Used by `docker-compose.yml` to back the Marten test suite plus the
# Marten.PostGIS / Marten.PgVector extension test projects.
FROM postgres:17

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       postgresql-17-postgis-3 \
       postgresql-17-pgvector \
    && rm -rf /var/lib/apt/lists/*
