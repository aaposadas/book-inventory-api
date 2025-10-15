# Stage 1: build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["BookInventory.Api.csproj", "./"]
RUN dotnet restore "./BookInventory.Api.csproj"
COPY . .
RUN dotnet publish "BookInventory.Api.csproj" -c Release -o /app/publish

# Stage 2: runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "BookInventory.Api.dll"]
