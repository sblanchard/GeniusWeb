# Self-contained build. Repo includes the four FlexRadio Genius client
# libraries as git submodules under vendor/, so build context = repo root.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore GeniusOnline.slnx
RUN dotnet publish src/GeniusOnline.Web/GeniusOnline.Web.csproj \
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
