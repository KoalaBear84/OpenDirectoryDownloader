FROM mcr.microsoft.com/dotnet/core/sdk:3.1

COPY . /app
WORKDIR /

RUN dotnet build /app/OpenDirectoryDownloader

ENTRYPOINT ["/app/OpenDirectoryDownloader/bin/Debug/netcoreapp3.1/OpenDirectoryDownloader"]
