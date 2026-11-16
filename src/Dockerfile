FROM mcr.microsoft.com/dotnet/runtime:11.0

WORKDIR /app

ADD app /app

RUN chmod +x OpenDirectoryDownloader

ENTRYPOINT ["./OpenDirectoryDownloader"]