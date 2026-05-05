FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Gemma4Local.sln ./
COPY src/Gemma4Local.Api/Gemma4Local.Api.csproj src/Gemma4Local.Api/
RUN dotnet restore

COPY . .
RUN dotnet publish src/Gemma4Local.Api/Gemma4Local.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Gemma4Local.Api.dll"]
