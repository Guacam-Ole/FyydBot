FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /App

# Copy everything
COPY . ./

# Restore as distinct layers

RUN dotnet restore
# Build and publish a release
RUN dotnet publish -f net8.0 -c Release -o out

# Build runtime image
FROM ubuntu:22.04

ENV DOTNET_VERSION=8.0
	 
RUN apt-get update \
    && apt-get install -y wget \
    && wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y dotnet-sdk-$DOTNET_VERSION \
    && apt-get -y install libgomp1
	    
WORKDIR /App
COPY --from=build-env /App/out .



ENTRYPOINT ["dotnet", "FyydBot.dll"]