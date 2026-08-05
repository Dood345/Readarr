# syntax=docker/dockerfile:1.7
#
# Multi-stage build for Readarr.
#
#   docker build -t readarr:local .                                   production image
#   docker build --target test .                                      build, then run the unit suite
#   docker build --build-arg RUNTIME_ID=linux-arm64 -t readarr:local . cross-target another arch
#
# The backend is published self-contained, so the runtime layer only needs the
# native dependencies rather than a full .NET runtime.
#
ARG DOTNET_VERSION=8.0
ARG NODE_VERSION=20
ARG FRAMEWORK=net8.0
ARG RUNTIME_ID=linux-x64

# Directory.Build.props ships <AssemblyVersion>10.0.0.*</AssemblyVersion> as a
# placeholder that CI replaces (see UpdateVersionNumber in build.sh). It must be
# replaced here too: RuntimeInfo.InternalIsOfficialBuild() treats Major >= 10 or
# Revision > 10000 as an unofficial build, which makes RuntimeInfo.IsProduction
# false, and CacheableSpecification then marks *every* response no-cache/no-store.
# Leaving the placeholder in place costs a full re-download and re-compression of
# the UI bundle on every single page load.
ARG READARR_VERSION=0.4.19.0

# ---------------------------------------------------------------------------
# Frontend -> _output/UI
# ---------------------------------------------------------------------------
FROM node:${NODE_VERSION}-bookworm-slim AS frontend

WORKDIR /build

# Install dependencies first so frontend edits don't invalidate the yarn layer.
COPY package.json yarn.lock .yarnrc ./
RUN yarn install --frozen-lockfile --network-timeout 300000

COPY tsconfig.json ./
COPY frontend ./frontend

# webpack resolves 'frontend/src/index.ejs' relative to the working directory,
# so this has to run from the repo root.
RUN yarn build --env production

# ---------------------------------------------------------------------------
# Backend -> _output/<framework>/<rid>/publish and _tests/...
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS backend
ARG FRAMEWORK
ARG RUNTIME_ID
ARG READARR_VERSION

WORKDIR /build

# Logo/64.png is an embedded resource in Readarr.Core, and .editorconfig drives
# EnforceCodeStyleInBuild - which is fatal here because TreatWarningsAsErrors is on.
COPY .editorconfig ./
COPY Logo ./Logo
COPY src ./src

RUN sed -i "s|<AssemblyVersion>[0-9.*]\+</AssemblyVersion>|<AssemblyVersion>${READARR_VERSION}</AssemblyVersion>|g" src/Directory.Build.props && \
    dotnet msbuild -restore src/Readarr.sln \
        -p:Configuration=Release \
        -p:Platform=Posix \
        -p:SelfContained=true \
        -p:RuntimeIdentifiers=${RUNTIME_ID} \
        -t:PublishAllRids

# ---------------------------------------------------------------------------
# Test (optional target)
#
# Deliberately not using ./test.sh: it ends with `if [ "$EXIT_CODE" -ge 0 ]; then
# exit 0`, which swallows failures. Calling dotnet test directly means a red test
# actually fails the build.
# ---------------------------------------------------------------------------
FROM backend AS test
ARG FRAMEWORK
ARG RUNTIME_ID

WORKDIR /build/_tests/${FRAMEWORK}/${RUNTIME_ID}/publish

ENV READARR_TESTS_LOG_OUTPUT=File

RUN dotnet test Readarr.Core.Test.dll Readarr.Common.Test.dll \
        --filter "Category!=IntegrationTest&Category!=AutomationTest&Category!=ManualTest&Category!=WINDOWS"

# ---------------------------------------------------------------------------
# Runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET_VERSION}-bookworm-slim AS runtime
ARG FRAMEWORK
ARG RUNTIME_ID

# Readarr__Update__Mechanism: without this the UI offers an in-place update that
# would replace this custom build with a stock Readarr release. "Docker" means the
# image is managed externally, which is what we want here.
ENV PUID=1000 \
    PGID=1000 \
    UMASK=002 \
    TZ=Etc/UTC \
    XDG_CONFIG_HOME=/config \
    Readarr__Update__Mechanism=Docker

# libsqlite3-0 is required: AssemblyLoader.LoadSqliteNativeLib maps "sqlite3" to
# the *system* "libsqlite3.so.0" on Linux, so System.Data.SQLite.Core.Servarr
# 1.0.115.5-18 ships no bundled native and the app dies on "Error creating main
# database" without it. (Lidarr's newer provider bundles its own libe_sqlite3.so
# and so needs no system package - do not copy that Dockerfile's apt list here.)
RUN apt-get update && \
    apt-get install --no-install-recommends -y \
        ca-certificates \
        curl \
        gosu \
        libsqlite3-0 \
        tzdata && \
    rm -rf /var/lib/apt/lists/* && \
    groupadd -g 1000 readarr && \
    useradd -u 1000 -g readarr -d /config -s /usr/sbin/nologin readarr && \
    mkdir -p /config /books /downloads

WORKDIR /app

COPY --from=backend /build/_output/${FRAMEWORK}/${RUNTIME_ID}/publish/ ./
COPY --from=frontend /build/_output/UI ./UI

# Windows service helpers and the Windows platform assembly are meaningless here.
RUN rm -f ServiceInstall.* ServiceUninstall.* Readarr.Windows.* && \
    chmod +x Readarr

COPY docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

VOLUME ["/config", "/books", "/downloads"]
EXPOSE 8787

# /ping is [AllowAnonymous], so this works regardless of the auth method.
HEALTHCHECK --interval=30s --timeout=10s --start-period=90s --retries=3 \
    CMD curl -fsS http://localhost:8787/ping || exit 1

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["/app/Readarr", "-nobrowser", "-data=/config"]
