ARG DOTNET_VERSION=8.0
ARG KUBECTL_VERSION=v1.35.5

FROM registry.k8s.io/kubectl:${KUBECTL_VERSION} AS kubectl

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-bookworm-slim AS build

WORKDIR /src

COPY . .

RUN dotnet restore ./SplunkAPI.csproj

RUN dotnet publish ./SplunkAPI.csproj \
    --configuration Release \
    --output /out \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION}-bookworm-slim AS runtime

WORKDIR /app

COPY --from=kubectl /bin/kubectl /usr/local/bin/kubectl
COPY --from=build /out ./

ENV HOME=/tmp
ENV DOTNET_EnableDiagnostics=0

EXPOSE 8080

USER app

ENTRYPOINT ["dotnet", "SplunkAPI.dll"]