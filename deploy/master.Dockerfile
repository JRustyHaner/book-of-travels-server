# Builds the Braid master server into a .NET 8 runtime image.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY server/master/ ./master/
COPY server/proto/ ./proto/
RUN dotnet publish master -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out ./
ENV BOT_MASTER_HOST=0.0.0.0 \
    BOT_MASTER_PORT=1234 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
# master.db (accounts/rooms) lives in /app/data — persist it
VOLUME /app/data
EXPOSE 1234 7689
ENTRYPOINT ["dotnet", "Master.dll"]
