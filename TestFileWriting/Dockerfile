FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build-env
WORKDIR /app

COPY . ./
RUN dotnet publish -c Release -o out --self-contained true --runtime linux-x64 --framework net5.0

#FROM ubuntu:focal
FROM mcr.microsoft.com/dotnet/sdk:5.0

WORKDIR /app
COPY --from=build-env /app/out .
RUN apt update && apt install htop
ENTRYPOINT  ["dotnet", "TestFileWriting.dll"]
