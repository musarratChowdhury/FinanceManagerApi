#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["FinanceManagerApi/FinanceManagerApi.csproj", "FinanceManagerApi/"]
RUN dotnet restore "./FinanceManagerApi/./FinanceManagerApi.csproj"
COPY . .
WORKDIR "/src/FinanceManagerApi"
RUN dotnet build "./FinanceManagerApi.csproj" -c $BUILD_CONFIGURATION -o /app/build
# Install Entity Framework Core tools
RUN dotnet tool install --global dotnet-ef --version 7.0.2

# Add the EF Core tools to the PATH
ENV PATH="${PATH}:/root/.dotnet/tools"
# Execute Entity Framework Core migrations
# RUN dotnet ef database update 

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./FinanceManagerApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FinanceManagerApi.dll"]