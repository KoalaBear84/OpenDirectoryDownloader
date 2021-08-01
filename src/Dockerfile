FROM mcr.microsoft.com/dotnet/runtime:6.0

WORKDIR /app

ADD app /app

RUN chmod +x OpenDirectoryDownloader

ENTRYPOINT ["./OpenDirectoryDownloader"]