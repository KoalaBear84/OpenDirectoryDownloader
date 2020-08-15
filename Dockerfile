FROM mcr.microsoft.com/dotnet/core/sdk:3.1

COPY . /app
WORKDIR /app

RUN dotnet build OpenDirectoryDownloader

ENTRYPOINT ["./OpenDirectoryDownloader/bin/Debug/netcoreapp3.1/OpenDirectoryDownloader"]
