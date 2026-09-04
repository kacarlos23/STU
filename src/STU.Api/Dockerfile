FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props global.json ./
COPY src/STU.Api/STU.Api.csproj src/STU.Api/
COPY src/STU.Application/STU.Application.csproj src/STU.Application/
COPY src/STU.Domain/STU.Domain.csproj src/STU.Domain/
COPY src/STU.Infrastructure/STU.Infrastructure.csproj src/STU.Infrastructure/
RUN dotnet restore src/STU.Api/STU.Api.csproj

COPY src/ src/
RUN dotnet publish src/STU.Api/STU.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "STU.Api.dll"]
