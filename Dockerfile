FROM alpine:3.18 AS base
RUN apk update && apk upgrade
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
WORKDIR /app
RUN adduser -u 1000 --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine3.18 AS build
RUN apk update && apk upgrade
RUN apk add --no-cache clang build-base zlib-dev
WORKDIR /src

FROM build AS publish
COPY . .
ARG RUNTIME_ID=linux-musl-x64
RUN dotnet restore -r $RUNTIME_ID
RUN dotnet publish "./src/SlimFaas/SlimFaas.csproj" -c Release -r $RUNTIME_ID  -o /app/publish --no-restore
RUN rm /app/publish/*.pdb
RUN rm /app/publish/SlimData

FROM base AS final
WORKDIR /app
COPY --chown=appuser --from=publish /app/publish .
RUN ls -la
ENTRYPOINT ["./SlimFaas"]


