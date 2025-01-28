FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /app

COPY . ./

RUN dotnet restore ./Liquip.Patcher.Tests/Liquip.Patcher.Tests.csproj

RUN dotnet build ./Liquip.Patcher.Tests/Liquip.Patcher.Tests.csproj --configuration Debug --no-restore

CMD ["dotnet", "test", "./Liquip.Patcher.Tests/Liquip.Patcher.Tests.csproj", "--no-build", "--configuration", "Debug", "--logger", "trx;LogFileName=Liquip.Patcher.Tests.trx"]