dotnet build src/SafeSpeak.App/SafeSpeak.App.csproj -c Release -p:EnableWindowsTargeting=true
dotnet test tests/SafeSpeak.Core.Tests/SafeSpeak.Core.Tests.csproj -c Release -p:EnableWindowsTargeting=true
