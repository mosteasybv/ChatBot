# WorkEasy A2A-agentservice (LlmTornado)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/WorkEasy.A2A/WorkEasy.A2A.csproj src/WorkEasy.A2A/
RUN dotnet restore src/WorkEasy.A2A/WorkEasy.A2A.csproj
COPY src/ src/
RUN dotnet publish src/WorkEasy.A2A/WorkEasy.A2A.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    A2A__DataPath=/app/data/agents.json
VOLUME /app/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "WorkEasy.A2A.dll"]
