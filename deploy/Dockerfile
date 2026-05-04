FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY MSAgentFramework/MSAgentFramework.sln ./MSAgentFramework/
COPY MSAgentFramework/src/MSAgentFramework.Agent/MSAgentFramework.Agent.csproj                       MSAgentFramework/src/MSAgentFramework.Agent/
COPY MSAgentFramework/src/MSAgentFramework.Agent.Contracts/MSAgentFramework.Agent.Contracts.csproj   MSAgentFramework/src/MSAgentFramework.Agent.Contracts/
COPY MSAgentFramework/src/MSAgentFramework.ServiceDefaults/MSAgentFramework.ServiceDefaults.csproj   MSAgentFramework/src/MSAgentFramework.ServiceDefaults/

RUN dotnet restore MSAgentFramework/src/MSAgentFramework.Agent/MSAgentFramework.Agent.csproj

COPY MSAgentFramework/src/MSAgentFramework.Agent/                MSAgentFramework/src/MSAgentFramework.Agent/
COPY MSAgentFramework/src/MSAgentFramework.Agent.Contracts/      MSAgentFramework/src/MSAgentFramework.Agent.Contracts/
COPY MSAgentFramework/src/MSAgentFramework.ServiceDefaults/      MSAgentFramework/src/MSAgentFramework.ServiceDefaults/

RUN dotnet publish MSAgentFramework/src/MSAgentFramework.Agent/MSAgentFramework.Agent.csproj \
    -c $BUILD_CONFIGURATION -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8085
EXPOSE 8085

USER $APP_UID
ENTRYPOINT ["dotnet", "MSAgentFramework.Agent.dll"]
