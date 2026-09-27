# ---- build ----
# Build context is the repository root (see render.yaml dockerContext).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY U-AI/UAI.csproj ./
RUN dotnet restore
COPY U-AI/ ./
RUN dotnet publish -c Release -o /app --no-restore

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# 0.1 CPU: keep the runtime from sizing itself for many cores, and cap the
# GC heap so the 86 MB of weights always fit inside Render's 512 MB.
ENV DOTNET_gcServer=0 \
    DOTNET_GCHeapHardLimit=0x18000000 \
    OMP_NUM_THREADS=1 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

EXPOSE 8080
ENV PORT=8080

COPY --from=build /app ./

ENTRYPOINT ["dotnet", "UAI.dll"]
