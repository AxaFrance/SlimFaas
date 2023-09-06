FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-preview-alpine AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0-preview AS build
RUN apt update
RUN apt install -y clang zlib1g-dev
WORKDIR /src
COPY . .
RUN dotnet restore "SlimFaas.csproj"
RUN dotnet build "SlimFaas.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -r linux-musl-x64 --self-contained=true -p:PublishAot=true  -p:PublishReadyToRun=true -p:PublishTrimmed=true -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./SlimFaas"]
