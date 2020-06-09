FROM mcr.microsoft.com/dotnet/core-nightly/sdk:3.1-alpine3.10

COPY . /app
WORKDIR /app

RUN dotnet build OpenDirectoryDownloader

ENTRYPOINT ["./OpenDirectoryDownloader/bin/Debug/netcoreapp3.1/OpenDirectoryDownloader"]
