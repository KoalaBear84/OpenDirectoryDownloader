FROM mcr.microsoft.com/dotnet/runtime

WORKDIR /app

ADD app /app

RUN chmod +x OpenDirectoryDownloader

ENTRYPOINT ["./OpenDirectoryDownloader"]