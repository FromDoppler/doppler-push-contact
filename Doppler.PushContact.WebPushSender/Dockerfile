#See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG FROMDOPPLER_GITHUB_PACKAGES_TOKEN
COPY ["nuget.config", "."]
COPY ["Doppler.PushContact.Models/Doppler.PushContact.Models.csproj", "Doppler.PushContact.Models/"]
COPY ["Doppler.PushContact.WebPushSender/Doppler.PushContact.WebPushSender.csproj", "Doppler.PushContact.WebPushSender/"]
COPY ["Doppler.PushContact.QueuingService/Doppler.PushContact.QueuingService.csproj", "Doppler.PushContact.QueuingService/"]
RUN dotnet restore "Doppler.PushContact.WebPushSender/Doppler.PushContact.WebPushSender.csproj"
COPY . .
WORKDIR "/src/Doppler.PushContact.WebPushSender"
RUN dotnet build "Doppler.PushContact.WebPushSender.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Doppler.PushContact.WebPushSender.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Doppler.PushContact.WebPushSender.dll"]
