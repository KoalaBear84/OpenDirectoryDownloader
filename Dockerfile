FROM mcr.microsoft.com/dotnet/runtime

ADD app /app
WORKDIR /app

RUN chmod +x OpenDirectoryDownloader

ENTRYPOINT ["./OpenDirectoryDownloader"]