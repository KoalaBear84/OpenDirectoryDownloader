FROM mcr.microsoft.com/dotnet/runtime

COPY app /app
WORKDIR /app

RUN chmod +x OpenDirectoryDownloader

ENTRYPOINT ["./OpenDirectoryDownloader"]