# Multi-stage: the SDK never reaches the shipped image.
#
# Only what the server needs is copied. The tests, the probe and the hand-written artifact are
# deliberately absent — the artifact is a study piece referenced by nothing shipped, and
# building it here would put it in a production image for no reason.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, against the manifests alone. Source changes then do not invalidate the
# restore layer, which is most of the build time.
COPY global.json NuGet.Config Directory.Build.props Directory.Packages.props ./
COPY src/DotNetMcpServer.Server/DotNetMcpServer.Server.csproj src/DotNetMcpServer.Server/
RUN dotnet restore src/DotNetMcpServer.Server/DotNetMcpServer.Server.csproj

COPY src/DotNetMcpServer.Server/ src/DotNetMcpServer.Server/
RUN dotnet publish src/DotNetMcpServer.Server/DotNetMcpServer.Server.csproj \
        --configuration Release \
        --no-restore \
        --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# ICU stays. InvariantGlobalization is pinned off because get_current_datetime resolves IANA
# timezone ids through it, and the aspnet image carries ICU already — this is a note so nobody
# "slims" the image by removing it.

WORKDIR /app
COPY --from=build /app .

# The workspace is a mount point. An image with documents baked in would serve whatever was
# committed at build time rather than what the operator meant to expose.
RUN mkdir -p /workspace && chown app:app /workspace
VOLUME /workspace

# Non-root, which the aspnet image already provides as uid 1654.
USER app

# ASPNETCORE_URLS binds every interface, which is correct here and only here: the container
# boundary is what limits reach, and a server bound to 127.0.0.1 inside a container is
# reachable by nothing at all. On a host process the default stays loopback.
#
# The comment sits above the instruction rather than inside it: a Dockerfile does not allow a
# comment between continued lines, and putting one there is a parse error rather than a note.
ENV MCP_TRANSPORT=http \
    MCP_WORKSPACE_ROOT=/workspace \
    ASPNETCORE_URLS=http://0.0.0.0:3001

EXPOSE 3001

# The protected-resource metadata document is served without a token, which makes it the one
# endpoint a health check can use on an authenticated server.
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=5 \
    CMD ["/app/DotNetMcpServer.Server", "--health-check"]

ENTRYPOINT ["/app/DotNetMcpServer.Server"]
