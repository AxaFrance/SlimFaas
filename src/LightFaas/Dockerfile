FROM mcr.microsoft.com/dotnet/runtime-deps:7.0-alpine AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "LightFaas.csproj"
RUN dotnet build "LightFaas.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -r linux-musl-x64 --self-contained=true -p:PublishSingleFile=true  -p:PublishReadyToRun=true -p:PublishTrimmed=true -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./LightFaas"]
