# Multi-stage build. Build context MUST be the parent directory containing
# both GeniusOnline/ and the four sibling FlexRadio Genius repos that are
# referenced via ..\..\..\ project paths. docker-compose handles this with
# `build.context: ..`.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore GeniusOnline/GeniusOnline.slnx
RUN dotnet publish GeniusOnline/src/GeniusOnline.Web/GeniusOnline.Web.csproj \
    -c Release -o /app/publish \
    --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:9050
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__Default="Data Source=/data/geniusonline.db"

COPY --from=build /app/publish .

EXPOSE 9050
VOLUME ["/data"]

ENTRYPOINT ["dotnet", "GeniusOnline.Web.dll"]
