# Master server — build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /out

# Master server — runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out .
# data dir for SQLite (accounts/rooms) — mount a volume here
RUN mkdir -p /app/data
EXPOSE 1234 7689
ENV BOT_MASTER_HOST=0.0.0.0 \
    BOT_MASTER_PORT=1234 \
    BOT_PUBLIC_HOST=
ENTRYPOINT ["dotnet", "Master.dll"]
