FROM mcr.microsoft.com/dotnet/runtime-deps:8.0-alpine AS base
WORKDIR /app
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
RUN adduser -u 1000 --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "./src/SlimFaas/SlimFaas.csproj"
RUN dotnet build "./src/SlimFaas/SlimFaas.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "./src/SlimFaas/SlimFaas.csproj" -c Release -r linux-musl-x64 --self-contained=true -p:PublishSingleFile=true  -o /app/publish
#RUN ls /app/publish
#RUN rm /app/publish/*.pdb
RUN rm /app/publish/SlimData

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./SlimFaas"]
