FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props global.json ./
COPY src/STU.Worker/STU.Worker.csproj src/STU.Worker/
COPY src/STU.Application/STU.Application.csproj src/STU.Application/
COPY src/STU.Domain/STU.Domain.csproj src/STU.Domain/
COPY src/STU.Infrastructure/STU.Infrastructure.csproj src/STU.Infrastructure/
RUN dotnet restore src/STU.Worker/STU.Worker.csproj

COPY src/ src/
RUN dotnet publish src/STU.Worker/STU.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl gdal-bin \
    && install -d /usr/share/postgresql-common/pgdg \
    && curl -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc --fail https://www.postgresql.org/media/keys/ACCC4CF8.asc \
    && . /etc/os-release \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client-18 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "STU.Worker.dll"]
