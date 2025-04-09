FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /app

COPY . ./

RUN dotnet restore ./Cosmos.Patcher.Tests/Cosmos.Patcher.Tests.csproj

RUN dotnet build ./Cosmos.Patcher.Tests/Cosmos.Patcher.Tests.csproj --configuration Debug --no-restore

CMD ["dotnet", "test", "./Cosmos.Patcher.Tests/Cosmos.Patcher.Tests.csproj", "--no-build", "--configuration", "Debug", "--logger", "trx;LogFileName=Cosmos.Patcher.Tests.trx"]