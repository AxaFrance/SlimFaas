﻿FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "./src/SlimFaas/SlimFaas.csproj"
RUN dotnet build "./src/SlimFaas/SlimFaas.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "./src/SlimFaas/SlimFaas.csproj" -c Release -r linux-musl-x64 --self-contained=true -p:PublishSingleFile=true -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./SlimFaas"]